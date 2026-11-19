using System.Security.Claims;
using FleetOps.Application.Security;
namespace FleetOps.Api.Security;

public sealed class HttpCurrentActor(IHttpContextAccessor accessor) : ICurrentActor
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;
    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;
    public string? UserId => User?.FindFirstValue(ClaimTypes.NameIdentifier);
    public Guid? TechnicianId => Guid.TryParse(User?.FindFirstValue("technician_id"), out var id) ? id : null;
    public bool IsInRole(string role) => IsAuthenticated && User!.IsInRole(role);
}
public static class Policies
{
    public const string Administrator = "Administrator";
    public const string Operations = "Operations";
    public const string Staff = "Staff";
}
