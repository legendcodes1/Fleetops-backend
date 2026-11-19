using System.ComponentModel.DataAnnotations;
namespace FleetOps.Api.Contracts;

public sealed class LoginRequest
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(128)] public string Password { get; set; } = "";
}
public sealed class RefreshRequest
{
    [Required, StringLength(16384)] public string RefreshToken { get; set; } = "";
}
public sealed class ChangePasswordRequest
{
    [Required, StringLength(128)] public string CurrentPassword { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12)] public string NewPassword { get; set; } = "";
}
public sealed class CreateUserRequest
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12)] public string Password { get; set; } = "";
    [Required, StringLength(30)] public string Role { get; set; } = "";
    public Guid? TechnicianId { get; set; }
}
public sealed class UserAccessRequest
{
    [Required, StringLength(30)] public string Role { get; set; } = "";
    public Guid? TechnicianId { get; set; }
    public bool IsEnabled { get; set; } = true;
}
public sealed class ResetUserPasswordRequest
{
    [Required, StringLength(128, MinimumLength = 12)] public string NewPassword { get; set; } = "";
}
public sealed record AccountResponse(Guid Id, string Email, string Role, Guid? TechnicianId, bool IsEnabled);
public sealed record TokenResponse(string TokenType, string AccessToken, long ExpiresIn, string RefreshToken);
