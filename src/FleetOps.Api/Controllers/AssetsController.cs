using Microsoft.AspNetCore.Authorization;
using FleetOps.Api.Security;
using FleetOps.Api.Contracts;
using FleetOps.Api.Http;
using FleetOps.Application.Exceptions;
using FleetOps.Application.Interfaces;
using FleetOps.Application.Models;
using Microsoft.AspNetCore.Mvc;
namespace FleetOps.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.Staff)]
[Route("api/assets")]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
[ProducesResponseType<ProblemDetails>(412)]
[ProducesResponseType<ProblemDetails>(428)]
public sealed class AssetsController(IAssetService assets) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AssetResponse>>> GetAssets([FromQuery] AssetListRequest request, CancellationToken cancellationToken)
    {
        var page = await assets.GetAllAsync(request.ToQuery(), cancellationToken);
        return Ok(new PagedResult<AssetResponse>(page.Items.Select(AssetResponse.From).ToArray(), page.Page, page.PageSize, page.TotalCount));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AssetResponse>> GetAssetById(Guid id, CancellationToken cancellationToken)
    {
        var asset = await assets.GetByIdAsync(id, cancellationToken);
        if (asset is null) return NotFound();
        Response.Headers.ETag = EntityTags.Quote(asset.RowVersion);
        return Ok(AssetResponse.From(asset));
    }

    [HttpPost, Authorize(Policy = Policies.Administrator)]
    [ProducesResponseType<AssetResponse>(201)]
    public async Task<ActionResult<AssetResponse>> CreateAsset(CreateAssetRequest request, CancellationToken cancellationToken)
    {
        var asset = await assets.CreateAsync(request.Name, request.SerialNumber, cancellationToken);
        Response.Headers.ETag = EntityTags.Quote(asset.RowVersion);
        return CreatedAtAction(nameof(GetAssetById), new { id = asset.Id }, AssetResponse.From(asset));
    }

    [HttpPut("{id:guid}"), Authorize(Policy = Policies.Administrator)]
    public async Task<IActionResult> UpdateAsset(Guid id, UpdateAssetRequest request, CancellationToken cancellationToken) =>
        await assets.UpdateAsync(id, request.Name, request.SerialNumber, EntityTags.Read(Request), cancellationToken) ? NoContent() : NotFound();

    [HttpDelete("{id:guid}"), Authorize(Policy = Policies.Administrator)]
    public async Task<IActionResult> DeleteAsset(Guid id, CancellationToken cancellationToken) =>
        await assets.DeleteAsync(id, EntityTags.Read(Request), cancellationToken) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/maintenance"), Authorize(Policy = Policies.Operations)]
    [ProducesResponseType(204)]
    public Task<IActionResult> Maintenance(Guid id, CancellationToken token) => Transition(id, AssetTransition.Maintenance, token);
    [HttpPost("{id:guid}/return-to-service"), Authorize(Policy = Policies.Operations)]
    [ProducesResponseType(204)]
    public Task<IActionResult> ReturnToService(Guid id, CancellationToken token) => Transition(id, AssetTransition.ReturnToService, token);
    [HttpPost("{id:guid}/retire"), Authorize(Policy = Policies.Administrator)]
    [ProducesResponseType(204)]
    public Task<IActionResult> Retire(Guid id, CancellationToken token) => Transition(id, AssetTransition.Retire, token);
    private async Task<IActionResult> Transition(Guid id, AssetTransition transition, CancellationToken token) =>
        await assets.TransitionAsync(id, transition, EntityTags.Read(Request), token) ? NoContent() : NotFound();
}
