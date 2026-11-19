using FleetOps.Application.Security;
using FleetOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace FleetOps.Infrastructure.Identity;

public static class IdentityBootstrap
{
    public static async Task<bool> CreateAdministratorAsync(IServiceProvider services, string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Bootstrap credentials must be supplied through environment configuration.");
        var db = services.GetRequiredService<FleetOpsDbContext>();
        var users = services.GetRequiredService<UserManager<FleetOpsUser>>();
        var roles = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        if (await db.Users.AnyAsync(u => db.UserRoles.Any(ur => ur.UserId == u.Id
            && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == Roles.Administrator))))
            return false;
        foreach (var name in Roles.All)
            if (!await roles.RoleExistsAsync(name))
                UserAdministration.Check(await roles.CreateAsync(new IdentityRole<Guid>(name) { Id = Guid.NewGuid() }));
        var user = new FleetOpsUser { Id = Guid.NewGuid(), UserName = email, Email = email, LockoutEnabled = true };
        UserAdministration.Check(await users.CreateAsync(user, password));
        UserAdministration.Check(await users.AddToRoleAsync(user, Roles.Administrator));
        await transaction.CommitAsync();
        return true;
    }
}
