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

    // From body Converts JSON into CreateAssetRequest
    [HttpPost]
    public IActionResult CreateAsset( [FromBody] CreateAssetRequest request)
    {
        // 1. Construct an Asset using request.Name
        //    and request.SerialNumber
        var asset = new Asset(request.Name, request.SerialNumber);
        // 2. Add it to your existing Assets collection
        Assets.Add(asset);
        // 3. Return CreatedAtAction(...)
        return CreatedAtAction(nameof(GetAssetById), new { id = asset.Id }, asset);
    }


}