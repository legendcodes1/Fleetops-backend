using FleetOps.Application.Security;
using FleetOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using System.Threading.RateLimiting;

namespace FleetOps.Api.Security;

public static class SecurityConfiguration
{
    public static void AddFleetOpsSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentActor, HttpCurrentActor>();
        services.AddScoped<IUserAdministration, UserAdministration>();
        services.AddAuthentication(IdentityConstants.BearerScheme).AddBearerToken(IdentityConstants.BearerScheme, options =>
        {
            options.BearerTokenExpiration = TimeSpan.FromMinutes(15);
            options.RefreshTokenExpiration = TimeSpan.FromHours(24);
        });
        services.AddIdentityCore<FleetOpsUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
            options.Password.RequiredUniqueChars = 4;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        }).AddRoles<IdentityRole<Guid>>()
          .AddEntityFrameworkStores<FleetOps.Infrastructure.Persistence.FleetOpsDbContext>()
          .AddSignInManager()
          .AddDefaultTokenProviders();
        services.AddScoped<IUserClaimsPrincipalFactory<FleetOpsUser>, FleetOpsClaimsFactory>();
        var protection = services.AddDataProtection().SetApplicationName("FleetOps");
        if (configuration["DataProtection:KeysPath"] is { Length: > 0 } path)
            protection.PersistKeysToFileSystem(new DirectoryInfo(path));
        services.AddAuthorization(options =>
        {
            var staff = new AuthorizationPolicyBuilder(IdentityConstants.BearerScheme)
                .RequireAuthenticatedUser().RequireRole(Roles.All).Build();
            options.DefaultPolicy = staff;
            options.FallbackPolicy = staff;
            options.AddPolicy(Policies.Staff, staff);
            options.AddPolicy(Policies.Administrator, p => p.RequireAuthenticatedUser().RequireRole(Roles.Administrator));
            options.AddPolicy(Policies.Operations, p => p.RequireAuthenticatedUser().RequireRole(Roles.Administrator, Roles.Supervisor));
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("credentials", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
    }
}
