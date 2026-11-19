using FleetOps.Api.Contracts;
using FleetOps.Application.Exceptions;
using FleetOps.Application.Interfaces;
using FleetOps.Application.Models;
using Microsoft.AspNetCore.Mvc;
namespace FleetOps.Api.Controllers;

[ApiController]
[Route("api/assets")]
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
        Response.Headers.ETag = QuoteVersion(asset.RowVersion);
        return Ok(AssetResponse.From(asset));
    }

    [HttpPost]
    public async Task<ActionResult<AssetResponse>> CreateAsset(CreateAssetRequest request, CancellationToken cancellationToken)
    {
        var asset = await assets.CreateAsync(request.Name, request.SerialNumber, cancellationToken);
        Response.Headers.ETag = QuoteVersion(asset.RowVersion);
        return CreatedAtAction(nameof(GetAssetById), new { id = asset.Id }, AssetResponse.From(asset));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateAsset(Guid id, UpdateAssetRequest request, CancellationToken cancellationToken) =>
        await assets.UpdateAsync(id, request.Name, request.SerialNumber, ReadExpectedVersion(), cancellationToken) ? NoContent() : NotFound();

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAsset(Guid id, CancellationToken cancellationToken) =>
        await assets.DeleteAsync(id, ReadExpectedVersion(), cancellationToken) ? NoContent() : NotFound();

    private byte[] ReadExpectedVersion()
    {
        var header = Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(header)) throw new PreconditionRequiredException();
        if (header.Length < 3 || header[0] != '"' || header[^1] != '"')
            throw new ArgumentException("If-Match must contain one quoted resource ETag.");
        try
        {
            var bytes = Convert.FromBase64String(header[1..^1]);
            if (bytes.Length != 8) throw new FormatException();
            return bytes;
        }
        catch (FormatException) { throw new ArgumentException("If-Match must contain one valid resource ETag."); }
    }

    private static string QuoteVersion(byte[] version) => $"\"{Convert.ToBase64String(version)}\"";
}
