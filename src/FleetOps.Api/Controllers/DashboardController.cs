using Microsoft.AspNetCore.Authorization;
using FleetOps.Api.Security;
using FleetOps.Api.Contracts;
using FleetOps.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
namespace FleetOps.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.Operations)]
[Route("api/dashboard")]
public sealed class DashboardController(IMaintenanceService maintenance) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<DashboardResponse>> Summary(CancellationToken token) =>
        Ok(DashboardResponse.From(await maintenance.SummaryAsync(token)));
}
