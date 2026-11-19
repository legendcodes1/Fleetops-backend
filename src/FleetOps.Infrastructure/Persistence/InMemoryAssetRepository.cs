using FleetOps.Application.Interfaces;
using FleetOps.Application.Models;
using FleetOps.Domain.Entities;
namespace FleetOps.Infrastructure.Persistence;

public sealed class InMemoryAssetRepository : IAssetRepository
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, Asset> assets = new();

    public InMemoryAssetRepository(bool seedDevelopmentData = false)
    {
        if (!seedDevelopmentData) return;
        Add(new Asset("Hydraulic Pump", "HP-001"));
        Add(new Asset("Conveyor Motor", "CM-002"));
    }

    public IReadOnlyList<AssetDto> GetAll()
    {
        lock (gate) return assets.Values.Select(AssetDto.From).ToArray();
    }

    public AssetDto? GetById(Guid id)
    {
        lock (gate) return assets.TryGetValue(id, out var asset) ? AssetDto.From(asset) : null;
    }

    public AssetDto Add(Asset asset)
    {
        lock (gate)
        {
            assets.Add(asset.Id, asset);
            return AssetDto.From(asset);
        }
    }

    public bool UpdateDetails(Guid id, string name, string serialNumber)
    {
        lock (gate)
        {
            if (!assets.TryGetValue(id, out var asset)) return false;
            asset.UpdateDetails(name, serialNumber);
            return true;
        }
    }

    public bool Remove(Guid id)
    {
        lock (gate) return assets.Remove(id);
    }
}
