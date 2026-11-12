using Microsoft.AspNetCore.Mvc;
using FleetOps.Domain.Entities;
using FleetOps.Api.Contracts;

namespace FleetOps.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AssetsController : ControllerBase
{
        private static readonly List<Asset> Assets =
    [
        new Asset("Hydraulic Pump", "HP-001"),
        new Asset("Conveyor Motor", "CM-002")
    ];

    [HttpGet]
    public IActionResult GetAssets()
    {
        // TODO: Create two Asset objects
        //  var asset1 = new Asset("Hydraulic Pump", "HP-001");
        //  var asset2 = new Asset("Conveyor Motor", "CM-002");

        //  var assets = new List<Asset>();
     
        //  assets.Add(asset1);
        //  assets.Add(asset2);

         // shorthand 
        //   var assets = new List<Asset>
        //     {
        //         new Asset("Hydraulic Pump", "HP-001"),
        //         new Asset("Conveyor Motor", "CM-002")
        //     };

        return Ok(Assets);
    }


    [HttpGet("{id:guid}")]
    public IActionResult GetAssetById(Guid id)
    {
        Console.WriteLine($"Requested ID: {id}");

        foreach (var item in Assets)
        {
            Console.WriteLine($"Stored ID: {item.Id}");
        }

        var asset = Assets.FirstOrDefault(a => a.Id == id);

        if (asset == null)
        {
            return NotFound();
        }

        return Ok(asset);
    }

    [HttpPut("{id:guid}")]
    public IActionResult UpdateAsset( Guid id, [FromBody] UpdateAssetRequest request)
    {
        // 1. Find the asset by ID
        var asset = Assets.FirstOrDefault(a => a.Id == id);
        // 2. Return NotFound() if it doesn't exist
        if (asset == null)
        {
            return NotFound();
        }

        asset.UpdateDetails(request.Name, request.SerialNumber);

        return NoContent();
    }


    // From body Converts JSON into CreateAssetRequest
    [HttpPost]
    public IActionResult CreateAsset( [FromBody] CreateAssetRequest request)
    {
   
        var asset = new Asset(request.Name, request.SerialNumber);

        Assets.Add(asset);
     
        return CreatedAtAction(nameof(GetAssetById), new { id = asset.Id }, asset);
    }

    [HttpDelete("{id:guid}")]
    public IActionResult DeleteAsset(Guid id)
    {
 
        var asset = Assets.FirstOrDefault(a => a.Id == id);
     
        if (asset == null)
        {
            return NotFound();
        }
     
        Assets.Remove(asset);

        return NoContent();
    }


}