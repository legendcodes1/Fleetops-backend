using FleetOps.Application.Models;
using FleetOps.Domain.Entities;
namespace FleetOps.Application.Interfaces;

// Persistence operations match asset use cases rather than a generic CRUD framework.
public interface IAssetRepository
{
    IReadOnlyList<AssetDto> GetAll();
    AssetDto? GetById(Guid id);
    AssetDto Add(Asset asset);
    bool UpdateDetails(Guid id, string name, string serialNumber);
    bool Remove(Guid id);
}
