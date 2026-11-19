using FleetOps.Api.Contracts;
using FleetOps.Api.Security;
using FleetOps.Application.Models;
using FleetOps.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace FleetOps.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = Policies.Administrator)]
public sealed class UsersController(IUserAdministration users) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AccountResponse>>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken token = default)
    {
        var result = await users.ListAsync(page, pageSize, token);
        return Ok(new PagedResult<AccountResponse>(result.Items.Select(Map).ToArray(), page, pageSize, result.TotalCount));
    }
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AccountResponse>> Get(Guid id, CancellationToken token) => Ok(Map(await users.GetAsync(id, token)));
    [HttpPost]
    [ProducesResponseType<AccountResponse>(201)]
    public async Task<ActionResult<AccountResponse>> Create(CreateUserRequest request, CancellationToken token)
    {
        var user = await users.CreateAsync(new(request.Email, request.Password, request.Role, request.TechnicianId), token);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, Map(user));
    }
    [HttpPut("{id:guid}/access")]
    public async Task<ActionResult<AccountResponse>> UpdateAccess(Guid id, UserAccessRequest request, CancellationToken token) =>
        Ok(Map(await users.UpdateAccessAsync(id, new(request.Role, request.TechnicianId, request.IsEnabled), token)));
    [HttpPost("{id:guid}/reset-password")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> ResetPassword(Guid id, ResetUserPasswordRequest request, CancellationToken token)
    {
        await users.ResetPasswordAsync(id, request.NewPassword, token);
        return NoContent();
    }
    private static AccountResponse Map(UserSummary u) => new(u.Id, u.Email, u.Role, u.TechnicianId, u.IsEnabled);
}
