using FleetOps.Domain.Entities;

namespace FleetOps.Application.Interfaces;

public interface IAssetService
{
    IEnumerable<Asset> GetAll();

    Asset? GetById(Guid id);
}