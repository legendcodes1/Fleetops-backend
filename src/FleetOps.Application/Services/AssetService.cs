using FleetOps.Application.Interfaces;
using FleetOps.Application.Models;
using FleetOps.Domain.Entities;
namespace FleetOps.Application.Services;

public sealed class AssetService(IAssetRepository repository) : IAssetService
{
    public Task<PagedResult<AssetDto>> GetAllAsync(AssetQuery query, CancellationToken cancellationToken = default)
    {
        query.Validate();
        return repository.GetAllAsync(query, cancellationToken);
    }
    public Task<AssetDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        repository.GetByIdAsync(id, cancellationToken);
    public Task<AssetDto> CreateAsync(string name, string serialNumber, CancellationToken cancellationToken = default) =>
        repository.AddAsync(new Asset(name, serialNumber), cancellationToken);
    public Task<bool> UpdateAsync(Guid id, string name, string serialNumber, byte[] expectedVersion, CancellationToken cancellationToken = default)
    {
        ValidateVersion(expectedVersion);
        return repository.UpdateDetailsAsync(id, name, serialNumber, expectedVersion, cancellationToken);
    }
    public Task<bool> DeleteAsync(Guid id, byte[] expectedVersion, CancellationToken cancellationToken = default)
    {
        ValidateVersion(expectedVersion);
        return repository.RemoveAsync(id, expectedVersion, cancellationToken);
    }
    private static void ValidateVersion(byte[] expectedVersion)
    {
        if (expectedVersion is not { Length: 8 }) throw new ArgumentException("Version must contain 8 bytes.");
    }
}
