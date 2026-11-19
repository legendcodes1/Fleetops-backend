using FleetOps.Application.Models;
namespace FleetOps.Application.Interfaces;

public interface IAssetService
{
    IReadOnlyList<AssetDto> GetAll();
    AssetDto? GetById(Guid id);
    AssetDto Create(string name, string serialNumber);
    bool Update(Guid id, string name, string serialNumber);
    bool Delete(Guid id);
}
