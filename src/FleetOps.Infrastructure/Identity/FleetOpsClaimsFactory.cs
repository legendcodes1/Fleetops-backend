using System.Security.Claims;
using FleetOps.Application.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
namespace FleetOps.Infrastructure.Identity;

public sealed class FleetOpsClaimsFactory(UserManager<FleetOpsUser> users, RoleManager<IdentityRole<Guid>> roles,
    IOptions<IdentityOptions> options) : UserClaimsPrincipalFactory<FleetOpsUser, IdentityRole<Guid>>(users, roles, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(FleetOpsUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.TechnicianId is { } id) identity.AddClaim(new Claim("technician_id", id.ToString()));
        return identity;
    }
}
