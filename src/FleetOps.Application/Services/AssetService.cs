using FleetOps.Application.Interfaces;
using FleetOps.Application.Models;
using FleetOps.Domain.Entities;
namespace FleetOps.Application.Services;

public sealed class AssetService(IAssetRepository repository) : IAssetService
{
    public IReadOnlyList<AssetDto> GetAll() => repository.GetAll();
    public AssetDto? GetById(Guid id) => repository.GetById(id);
    public AssetDto Create(string name, string serialNumber) => repository.Add(new Asset(name, serialNumber));
    public bool Update(Guid id, string name, string serialNumber) => repository.UpdateDetails(id, name, serialNumber);
    public bool Delete(Guid id) => repository.Remove(id);
}
