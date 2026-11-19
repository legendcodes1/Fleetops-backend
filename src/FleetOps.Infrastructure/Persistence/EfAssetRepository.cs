using FleetOps.Application.Exceptions;
using FleetOps.Application.Interfaces;
using FleetOps.Application.Models;
using FleetOps.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Infrastructure.Persistence;

public sealed class EfAssetRepository(FleetOpsDbContext db) : IAssetRepository
{
    private static readonly System.Linq.Expressions.Expression<Func<Asset, AssetDto>> Projection =
        asset => new AssetDto(asset.Id, asset.Name, asset.SerialNumber, asset.Status,
            asset.CreatedAt, asset.UpdatedAt, asset.RowVersion);

    public async Task<PagedResult<AssetDto>> GetAllAsync(AssetQuery query, CancellationToken cancellationToken = default)
    {
        query.Validate();
        var assets = db.Assets.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            assets = assets.Where(a => a.Name.Contains(search) || a.SerialNumber.Contains(search));
        }
        if (query.Status.HasValue) assets = assets.Where(a => a.Status == query.Status);
        var total = await assets.CountAsync(cancellationToken);
        var sorted = (query.SortBy, query.Descending) switch
        {
            ("serialNumber", false) => assets.OrderBy(a => a.SerialNumber),
            ("serialNumber", true) => assets.OrderByDescending(a => a.SerialNumber),
            ("createdAt", false) => assets.OrderBy(a => a.CreatedAt),
            ("createdAt", true) => assets.OrderByDescending(a => a.CreatedAt),
            ("status", false) => assets.OrderBy(a => a.Status),
            ("status", true) => assets.OrderByDescending(a => a.Status),
            ("name", true) => assets.OrderByDescending(a => a.Name),
            _ => assets.OrderBy(a => a.Name)
        };
        var items = await sorted.ThenBy(a => a.Id).Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize).Select(Projection).ToListAsync(cancellationToken);
        return new PagedResult<AssetDto>(items, query.Page, query.PageSize, total);
    }

    public Task<AssetDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Assets.AsNoTracking().Where(a => a.Id == id).Select(Projection).SingleOrDefaultAsync(cancellationToken);

    public async Task<AssetDto> AddAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        db.Assets.Add(asset);
        await SaveAsync(cancellationToken);
        return new AssetDto(asset.Id, asset.Name, asset.SerialNumber, asset.Status,
            asset.CreatedAt, asset.UpdatedAt, asset.RowVersion);
    }

    public async Task<bool> UpdateDetailsAsync(Guid id, string name, string serialNumber, byte[] expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var asset = await db.Assets.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (asset is null) return false;
        // Check even no-op edits; the original value also protects the write against a race after this read.
        if (!asset.RowVersion.SequenceEqual(expectedVersion)) throw new ConcurrencyConflictException();
        db.Entry(asset).Property(a => a.RowVersion).OriginalValue = expectedVersion;
        asset.UpdateDetails(name, serialNumber);
        await SaveAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RemoveAsync(Guid id, byte[] expectedVersion, CancellationToken cancellationToken = default)
    {
        var asset = await db.Assets.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (asset is null) return false;
        if (!asset.RowVersion.SequenceEqual(expectedVersion)) throw new ConcurrencyConflictException();
        db.Entry(asset).Property(a => a.RowVersion).OriginalValue = expectedVersion;
        db.Assets.Remove(asset);
        await SaveAsync(cancellationToken);
        return true;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException(); }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new PersistenceConflictException("An asset with this serial number already exists.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 547 })
        {
            throw new PersistenceConflictException("This change conflicts with a database relationship or constraint.");
        }
    }
}
