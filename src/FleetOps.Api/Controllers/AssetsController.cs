using FleetOps.Api.Contracts;
using FleetOps.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
namespace FleetOps.Api.Controllers;

[ApiController]
[Route("api/assets")]
public sealed class AssetsController(IAssetService assets) : ControllerBase
{
    [HttpGet]
    public IActionResult GetAssets() => Ok(assets.GetAll().Select(AssetResponse.From));

    [HttpGet("{id:guid}")]
    public IActionResult GetAssetById(Guid id)
    {
        var asset = assets.GetById(id);
        return asset is null ? NotFound() : Ok(AssetResponse.From(asset));
    }

    [HttpPost]
    public IActionResult CreateAsset(CreateAssetRequest request)
    {
        var asset = assets.Create(request.Name, request.SerialNumber);
        return CreatedAtAction(nameof(GetAssetById), new { id = asset.Id }, AssetResponse.From(asset));
    }

    [HttpPut("{id:guid}")]
    public IActionResult UpdateAsset(Guid id, UpdateAssetRequest request) =>
        assets.Update(id, request.Name, request.SerialNumber) ? NoContent() : NotFound();

    [HttpDelete("{id:guid}")]
    public IActionResult DeleteAsset(Guid id) => assets.Delete(id) ? NoContent() : NotFound();
}
