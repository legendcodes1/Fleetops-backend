using FleetOps.Application.Exceptions;
using FleetOps.Application.Models;
using FleetOps.Application.Security;
using FleetOps.Domain.Entities;
using FleetOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
namespace FleetOps.Infrastructure.Identity;

public sealed class UserAdministration(FleetOpsDbContext db, UserManager<FleetOpsUser> users,
    ICurrentActor actor) : IUserAdministration
{
    public async Task<PagedResult<UserSummary>> ListAsync(int page, int pageSize, CancellationToken token)
    {
        RequireAdmin();
        if (page < 1 || page > 1_000_000 || pageSize is < 1 or > 100) throw new ArgumentException("Invalid pagination.");
        var count = await db.Users.CountAsync(token);
        var rows = await Project(db.Users.AsNoTracking().OrderBy(u => u.Email).ThenBy(u => u.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(token);
        return new(rows, page, pageSize, count);
    }
    public async Task<UserSummary> GetAsync(Guid id, CancellationToken token)
    {
        RequireAdmin();
        return await Project(db.Users.AsNoTracking().Where(u => u.Id == id)).SingleOrDefaultAsync(token) ?? throw Missing();
    }
    public Task<UserSummary> CreateAsync(CreateUser request, CancellationToken token) =>
        HandleConflicts(() => CreateCoreAsync(request, token));
    private async Task<UserSummary> CreateCoreAsync(CreateUser request, CancellationToken token)
    {
        RequireAdmin();
        await ValidateAccess(new(request.Role, request.TechnicianId, true), null, token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var user = new FleetOpsUser { Id = Guid.NewGuid(), Email = request.Email, UserName = request.Email,
            TechnicianId = request.TechnicianId, LockoutEnabled = true };
        Check(await users.CreateAsync(user, request.Password));
        Check(await users.AddToRoleAsync(user, request.Role));
        db.AuditLogs.Add(new AuditLog(user.Id, "User", "Created", actor.UserId!));
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return new(user.Id, user.Email!, request.Role, user.TechnicianId, user.IsEnabled);
    }
    public Task<UserSummary> UpdateAccessAsync(Guid id, UserAccess request, CancellationToken token) =>
        HandleConflicts(() => UpdateAccessCoreAsync(id, request, token));
    private async Task<UserSummary> UpdateAccessCoreAsync(Guid id, UserAccess request, CancellationToken token)
    {
        RequireAdmin();
        if (id.ToString() == actor.UserId) throw new ArgumentException("An administrator cannot change their own access.");
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
        var user = await users.FindByIdAsync(id.ToString()) ?? throw Missing();
        await ValidateAccess(request, id, token);
        var oldRoles = await users.GetRolesAsync(user);
        if (oldRoles.Contains(Roles.Administrator) && (!request.IsEnabled || request.Role != Roles.Administrator))
        {
            var activeAdmins = await db.Users.CountAsync(u => u.IsEnabled && db.UserRoles.Any(ur => ur.UserId == u.Id
                && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == Roles.Administrator)), token);
            if (activeAdmins <= 1) throw new ArgumentException("The last enabled administrator cannot be removed.");
        }
        Check(await users.RemoveFromRolesAsync(user, oldRoles));
        user.IsEnabled = request.IsEnabled;
        user.TechnicianId = request.TechnicianId;
        Check(await users.AddToRoleAsync(user, request.Role));
        Check(await users.UpdateSecurityStampAsync(user));
        db.AuditLogs.Add(new AuditLog(user.Id, "User", "Updated", actor.UserId!));
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return new(user.Id, user.Email!, request.Role, user.TechnicianId, user.IsEnabled);
    }
    public async Task ResetPasswordAsync(Guid id, string password, CancellationToken token) =>
        await HandleConflicts(async () => { await ResetPasswordCoreAsync(id, password, token); return true; });
    private async Task ResetPasswordCoreAsync(Guid id, string password, CancellationToken token)
    {
        RequireAdmin();
        if (id.ToString() == actor.UserId) throw new ArgumentException("Use the change-password endpoint for your own account.");
        var user = await users.FindByIdAsync(id.ToString()) ?? throw Missing();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var reset = await users.GeneratePasswordResetTokenAsync(user);
        Check(await users.ResetPasswordAsync(user, reset, password));
        db.AuditLogs.Add(new AuditLog(user.Id, "User", "Updated", actor.UserId!));
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }
    private async Task ValidateAccess(UserAccess request, Guid? userId, CancellationToken token)
    {
        if (!Roles.All.Contains(request.Role)) throw new ArgumentException("Unknown role.");
        if (request.Role != Roles.Technician && request.TechnicianId is not null)
            throw new ArgumentException("Only Technician accounts can have a technician mapping.");
        if (request.Role == Roles.Technician && request.IsEnabled && request.TechnicianId is null)
            throw new ArgumentException("An enabled Technician account requires a technician mapping.");
        if (request.TechnicianId is { } id)
        {
            if (!await db.Technicians.AnyAsync(t => t.Id == id, token)) throw new ResourceNotFoundException("Technician was not found.");
            if (await db.Users.AnyAsync(u => u.TechnicianId == id && u.Id != userId, token))
                throw new PersistenceConflictException("That technician already has an account.");
        }
    }
    private IQueryable<UserSummary> Project(IQueryable<FleetOpsUser> query) => query.Select(u => new UserSummary(u.Id, u.Email!,
        (from ur in db.UserRoles join role in db.Roles on ur.RoleId equals role.Id
         where ur.UserId == u.Id select role.Name).FirstOrDefault() ?? "", u.TechnicianId, u.IsEnabled));
    private void RequireAdmin() => Access.Require(actor, Roles.Administrator);
    private static async Task<T> HandleConflicts<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (Exception error) when (IsDatabaseConflict(error))
        { throw new PersistenceConflictException("Account access changed concurrently or a unique value already exists. Reload and retry."); }
    }
    private static bool IsDatabaseConflict(Exception error)
    {
        // EF's non-retrying SQL execution strategy can wrap a deadlock in InvalidOperationException.
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is DbUpdateConcurrencyException || current is SqlException { Number: 2601 or 2627 or 1205 }) return true;
        return false;
    }
    private static ResourceNotFoundException Missing() => new("User was not found.");
    internal static void Check(IdentityResult result)
    {
        if (result.Succeeded) return;
        if (result.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail" or "ConcurrencyFailure"))
            throw new PersistenceConflictException("The account already exists or was changed concurrently.");
        throw new ArgumentException("Account validation failed: " + string.Join(", ", result.Errors.Select(e => e.Code)));
    }
}
