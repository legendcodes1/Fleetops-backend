using Microsoft.AspNetCore.Authorization;
using FleetOps.Api.Security;
using FleetOps.Api.Contracts;
using FleetOps.Api.Http;
using FleetOps.Application.Interfaces;
using FleetOps.Application.Models;
using Microsoft.AspNetCore.Mvc;
namespace FleetOps.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.Staff)]
[Route("api/work-orders")]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
[ProducesResponseType<ProblemDetails>(412)]
[ProducesResponseType<ProblemDetails>(428)]
public sealed class WorkOrdersController(IMaintenanceService maintenance) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<WorkOrderResponse>>> List([FromQuery] WorkOrderListRequest request, CancellationToken token)
    {
        var page = await maintenance.ListWorkOrdersAsync(request.ToQuery(), token);
        return Ok(new PagedResult<WorkOrderResponse>(page.Items.Select(WorkOrderResponse.From).ToArray(), page.Page, page.PageSize, page.TotalCount));
    }
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WorkOrderResponse>> Get(Guid id, CancellationToken token)
    {
        var result = await maintenance.GetWorkOrderAsync(id, token);
        return result is null ? NotFound() : Respond(result);
    }
    [HttpPost, Authorize(Policy = Policies.Operations)]
    [ProducesResponseType<WorkOrderResponse>(201)]
    public async Task<ActionResult<WorkOrderResponse>> Create(CreateWorkOrderRequest request, CancellationToken token)
    {
        var result = await maintenance.CreateWorkOrderAsync(request.AssetId, request.ToDetails(), token);
        Response.Headers.ETag = EntityTags.Quote(result.RowVersion);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, WorkOrderResponse.From(result));
    }
    [HttpPut("{id:guid}"), Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<WorkOrderResponse>> Update(Guid id, WorkOrderRequest request, CancellationToken token) =>
        Respond(await maintenance.UpdateWorkOrderAsync(id, request.ToDetails(), EntityTags.Read(Request), token));
    [HttpPost("{id:guid}/assign"), Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<WorkOrderResponse>> Assign(Guid id, AssignWorkOrderRequest request, CancellationToken token) =>
        Respond(await maintenance.AssignAsync(id, request.TechnicianId, EntityTags.Read(Request), token));
    [HttpPost("{id:guid}/start")]
    public async Task<ActionResult<WorkOrderResponse>> Start(Guid id, CancellationToken token) =>
        Respond(await maintenance.StartAsync(id, EntityTags.Read(Request), token));
    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<WorkOrderResponse>> Complete(Guid id, CompleteWorkOrderRequest request, CancellationToken token) =>
        Respond(await maintenance.CompleteAsync(id, request.Notes, EntityTags.Read(Request), token));
    [HttpPost("{id:guid}/cancel"), Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<WorkOrderResponse>> Cancel(Guid id, CancellationToken token) =>
        Respond(await maintenance.CancelAsync(id, EntityTags.Read(Request), token));
    private ActionResult<WorkOrderResponse> Respond(WorkOrderDto result)
    {
        Response.Headers.ETag = EntityTags.Quote(result.RowVersion);
        return Ok(WorkOrderResponse.From(result));
    }
}
