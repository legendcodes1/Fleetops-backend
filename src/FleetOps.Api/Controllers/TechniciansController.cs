using Microsoft.AspNetCore.Authorization;
using FleetOps.Api.Security;
using FleetOps.Api.Contracts;
using FleetOps.Api.Http;
using FleetOps.Application.Interfaces;
using FleetOps.Application.Models;
using Microsoft.AspNetCore.Mvc;
namespace FleetOps.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.Operations)]
[Route("api/technicians")]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
[ProducesResponseType<ProblemDetails>(412)]
[ProducesResponseType<ProblemDetails>(428)]
public sealed class TechniciansController(IMaintenanceService maintenance) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<TechnicianResponse>>> List([FromQuery] TechnicianListRequest request, CancellationToken token)
    {
        var page = await maintenance.ListTechniciansAsync(request.ToQuery(), token);
        return Ok(new PagedResult<TechnicianResponse>(page.Items.Select(TechnicianResponse.From).ToArray(), page.Page, page.PageSize, page.TotalCount));
    }
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TechnicianResponse>> Get(Guid id, CancellationToken token)
    {
        var result = await maintenance.GetTechnicianAsync(id, token);
        if (result is null) return NotFound();
        Response.Headers.ETag = EntityTags.Quote(result.RowVersion);
        return Ok(TechnicianResponse.From(result));
    }
    [HttpPost, Authorize(Policy = Policies.Administrator)]
    [ProducesResponseType<TechnicianResponse>(201)]
    public async Task<ActionResult<TechnicianResponse>> Create(TechnicianRequest request, CancellationToken token)
    {
        var result = await maintenance.CreateTechnicianAsync(request.ToDetails(), token);
        Response.Headers.ETag = EntityTags.Quote(result.RowVersion);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, TechnicianResponse.From(result));
    }
    [HttpPut("{id:guid}"), Authorize(Policy = Policies.Administrator)]
    public async Task<ActionResult<TechnicianResponse>> Update(Guid id, TechnicianRequest request, CancellationToken token)
    {
        var result = await maintenance.UpdateTechnicianAsync(id, request.ToDetails(), EntityTags.Read(Request), token);
        Response.Headers.ETag = EntityTags.Quote(result.RowVersion);
        return Ok(TechnicianResponse.From(result));
    }
}
