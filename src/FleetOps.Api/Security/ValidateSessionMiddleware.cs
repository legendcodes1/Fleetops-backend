using FleetOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
namespace FleetOps.Api.Security;

// Protected bearer tickets are revalidated against the live account on every authenticated request.
public sealed class ValidateSessionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, SignInManager<FleetOpsUser> signIn)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is null
            && context.User.Identity?.IsAuthenticated == true)
        {
            var user = await signIn.ValidateSecurityStampAsync(context.User);
            if (user is null || !user.IsEnabled || await signIn.UserManager.IsLockedOutAsync(user))
            {
                context.User = new System.Security.Claims.ClaimsPrincipal();
                await context.ChallengeAsync(IdentityConstants.BearerScheme);
                return;
            }
        }
        await next(context);
    }
}
