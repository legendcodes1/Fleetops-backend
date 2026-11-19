using FleetOps.Application.Interfaces;
using FleetOps.Application.Models;
using FleetOps.Application.Security;
using FleetOps.Domain.Entities;
namespace FleetOps.Application.Services;

public sealed class AssetService(IAssetRepository repository, ICurrentActor actor) : IAssetService
{
    public Task<bool> TransitionAsync(Guid id, AssetTransition transition, byte[] expectedVersion, CancellationToken cancellationToken = default)
    {
        Access.Require(actor, transition == AssetTransition.Retire ? [Roles.Administrator] : [Roles.Administrator, Roles.Supervisor]);
        ValidateVersion(expectedVersion);
        if (!Enum.IsDefined(transition)) throw new ArgumentException("Invalid asset transition.");
        return repository.TransitionAsync(id, transition, expectedVersion, cancellationToken);
    }
    public Task<PagedResult<AssetDto>> GetAllAsync(AssetQuery query, CancellationToken cancellationToken = default)
    {
        Access.Require(actor, Roles.All);
        query.Validate();
        return repository.GetAllAsync(query, cancellationToken);
    }
    public Task<AssetDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Access.Require(actor, Roles.All);
        return repository.GetByIdAsync(id, cancellationToken);
    }
    public Task<AssetDto> CreateAsync(string name, string serialNumber, CancellationToken cancellationToken = default)
    {
        Access.Require(actor, Roles.Administrator);
        return repository.AddAsync(new Asset(name, serialNumber), cancellationToken);
    }
    public Task<bool> UpdateAsync(Guid id, string name, string serialNumber, byte[] expectedVersion, CancellationToken cancellationToken = default)
    {
        Access.Require(actor, Roles.Administrator);
        ValidateVersion(expectedVersion);
        return repository.UpdateDetailsAsync(id, name, serialNumber, expectedVersion, cancellationToken);
    }
    public Task<bool> DeleteAsync(Guid id, byte[] expectedVersion, CancellationToken cancellationToken = default)
    {
        Access.Require(actor, Roles.Administrator);
        ValidateVersion(expectedVersion);
        return repository.RemoveAsync(id, expectedVersion, cancellationToken);
    }
    private static void ValidateVersion(byte[] expectedVersion)
    {
        if (expectedVersion is not { Length: 8 }) throw new ArgumentException("Version must contain 8 bytes.");
    }
}
