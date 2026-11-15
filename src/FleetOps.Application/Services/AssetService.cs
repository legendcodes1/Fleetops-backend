using FleetOps.Domain.Entities;
using FleetOps.Application.Interfaces;

namespace FleetOps.Application.Services;

public class AssetService : IAssetService
{
    private readonly List<Asset> _assets =
    [
        new Asset("Hydraulic Pump", "HP-001"),
        new Asset("Conveyor Motor", "CM-002")
    ];

    public IEnumerable<Asset> GetAll()
    {
        return _assets;
    }

    public Asset? GetById(Guid id)
    {
        return _assets.FirstOrDefault(a => a.Id == id);
    }
}