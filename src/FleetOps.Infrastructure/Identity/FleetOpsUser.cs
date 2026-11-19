using Microsoft.AspNetCore.Identity;
namespace FleetOps.Infrastructure.Identity;

public sealed class FleetOpsUser : IdentityUser<Guid>
{
    public Guid? TechnicianId { get; set; }
    public bool IsEnabled { get; set; } = true;
}
