using FleetOps.Application.Models;
using FleetOps.Domain.Entities;
namespace FleetOps.Application.Interfaces;

public interface IAssetRepository
{
    Task<bool> TransitionAsync(Guid id, AssetTransition transition, byte[] expectedVersion, CancellationToken cancellationToken = default);
    Task<PagedResult<AssetDto>> GetAllAsync(AssetQuery query, CancellationToken cancellationToken = default);
    Task<AssetDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AssetDto> AddAsync(Asset asset, CancellationToken cancellationToken = default);
    Task<bool> UpdateDetailsAsync(Guid id, string name, string serialNumber, byte[] expectedVersion, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(Guid id, byte[] expectedVersion, CancellationToken cancellationToken = default);
}
