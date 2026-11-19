using FleetOps.Application.Models;
namespace FleetOps.Application.Interfaces;

public interface IAssetService
{
    Task<bool> TransitionAsync(Guid id, AssetTransition transition, byte[] expectedVersion, CancellationToken cancellationToken = default);
    Task<PagedResult<AssetDto>> GetAllAsync(AssetQuery query, CancellationToken cancellationToken = default);
    Task<AssetDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AssetDto> CreateAsync(string name, string serialNumber, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(Guid id, string name, string serialNumber, byte[] expectedVersion, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, byte[] expectedVersion, CancellationToken cancellationToken = default);
}
