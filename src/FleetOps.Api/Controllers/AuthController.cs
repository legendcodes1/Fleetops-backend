using FleetOps.Api.Contracts;
using FleetOps.Api.Security;
using FleetOps.Application.Security;
using FleetOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
namespace FleetOps.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(SignInManager<FleetOpsUser> signIn, UserManager<FleetOpsUser> users,
    IOptionsMonitor<BearerTokenOptions> tokens, TimeProvider clock) : ControllerBase
{
    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("credentials")]
    [ProducesResponseType<TokenResponse>(200)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(429)]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        Response.Headers.CacheControl = "no-store";
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null || !user.IsEnabled) return Problem(statusCode: 401, title: "Invalid credentials");
        signIn.AuthenticationScheme = IdentityConstants.BearerScheme;
        var result = await signIn.PasswordSignInAsync(user, request.Password, false, lockoutOnFailure: true);
        if (!result.Succeeded) return Problem(statusCode: 401, title: "Invalid credentials");
        return new EmptyResult();
    }

    [HttpPost("refresh"), AllowAnonymous, EnableRateLimiting("credentials")]
    [ProducesResponseType<TokenResponse>(200)]
    [ProducesResponseType<ProblemDetails>(401)]
    public async Task<IActionResult> Refresh(RefreshRequest request)
    {
        var ticket = tokens.Get(IdentityConstants.BearerScheme).RefreshTokenProtector.Unprotect(request.RefreshToken);
        if (ticket?.Properties.ExpiresUtc is not { } expires || clock.GetUtcNow() >= expires)
            return Problem(statusCode: 401, title: "Invalid session");
        var user = await signIn.ValidateSecurityStampAsync(ticket.Principal);
        if (user is null || !user.IsEnabled || await users.IsLockedOutAsync(user))
            return Problem(statusCode: 401, title: "Invalid session");
        Response.Headers.CacheControl = "no-store";
        return SignIn(await signIn.CreateUserPrincipalAsync(user), IdentityConstants.BearerScheme);
    }

    [HttpGet("me")]
    public async Task<ActionResult<AccountResponse>> Me()
    {
        var user = (await users.GetUserAsync(User))!;
        return Ok(new AccountResponse(user.Id, user.Email!, (await users.GetRolesAsync(user)).Single(), user.TechnicianId, user.IsEnabled));
    }

    [HttpPost("logout")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Logout()
    {
        var user = (await users.GetUserAsync(User))!;
        Check(await users.UpdateSecurityStampAsync(user));
        return NoContent();
    }

    [HttpPost("change-password")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = (await users.GetUserAsync(User))!;
        Check(await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword));
        return NoContent();
    }
    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new ArgumentException("Account validation failed: " + string.Join(", ", result.Errors.Select(e => e.Code)));
    }
}
