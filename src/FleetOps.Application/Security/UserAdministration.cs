using FleetOps.Application.Models;
namespace FleetOps.Application.Security;

public sealed record UserSummary(Guid Id, string Email, string Role, Guid? TechnicianId, bool IsEnabled);
public sealed record CreateUser(string Email, string Password, string Role, Guid? TechnicianId);
public sealed record UserAccess(string Role, Guid? TechnicianId, bool IsEnabled);
public interface IUserAdministration
{
    Task<PagedResult<UserSummary>> ListAsync(int page, int pageSize, CancellationToken token);
    Task<UserSummary> GetAsync(Guid id, CancellationToken token);
    Task<UserSummary> CreateAsync(CreateUser request, CancellationToken token);
    Task<UserSummary> UpdateAccessAsync(Guid id, UserAccess request, CancellationToken token);
    Task ResetPasswordAsync(Guid id, string password, CancellationToken token);
}
