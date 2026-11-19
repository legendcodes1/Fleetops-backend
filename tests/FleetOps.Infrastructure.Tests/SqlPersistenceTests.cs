using FleetOps.Application.Exceptions;
using FleetOps.Application.Models;
using FleetOps.Application.Services;
using FleetOps.Domain.Entities;
using FleetOps.Domain.Enums;
using FleetOps.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Infrastructure.Tests;

public sealed class SqlPersistenceTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    private DbContextOptions<FleetOpsDbContext> options = null!;
    private FleetOpsDbContext Context() => new(options);

    public async Task InitializeAsync()
    {
        var connection = new SqlConnectionStringBuilder(fixture.Container.GetConnectionString())
        {
            InitialCatalog = "FleetOps_Test_" + Guid.NewGuid().ToString("N")
        };
        options = new DbContextOptionsBuilder<FleetOpsDbContext>().UseSqlServer(connection.ConnectionString).Options;
        await using var db = Context();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = Context();
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task MigrationAndSeedAreRepeatable()
    {
        await using var db = Context();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        await DevelopmentSeeder.SeedAsync(db);
        await DevelopmentSeeder.SeedAsync(db);
        Assert.Single(await db.Assets.ToListAsync());
        Assert.Single(await db.Technicians.ToListAsync());
        Assert.Single(await db.WorkOrders.ToListAsync());
        Assert.Equal(3, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task CrudPersistsAcrossContextsAndAuditSurvivesDeletion()
    {
        Guid id;
        byte[] version;
        await using (var db = Context())
        {
            var service = new AssetService(new EfAssetRepository(db));
            var created = await service.CreateAsync("Pump", "P-1");
            id = created.Id;
            version = created.RowVersion;
            Assert.Equal(8, version.Length);
            Assert.Equal(created.CreatedAt, created.UpdatedAt);
        }
        await using (var db = Context())
        {
            var service = new AssetService(new EfAssetRepository(db));
            Assert.True(await service.UpdateAsync(id, "Motor", "M-1", version));
        }
        await using (var db = Context())
        {
            var repository = new EfAssetRepository(db);
            var asset = (await repository.GetByIdAsync(id))!;
            Assert.Equal("Motor", asset.Name);
            Assert.False(version.SequenceEqual(asset.RowVersion));
            Assert.True(asset.UpdatedAt >= asset.CreatedAt);
            Assert.True(await repository.RemoveAsync(id, asset.RowVersion));
        }
        await using (var db = Context())
        {
            Assert.Empty(await db.Assets.ToListAsync());
            var logs = await db.AuditLogs.Where(a => a.EntityId == id).ToListAsync();
            Assert.Equal(3, logs.Count);
            Assert.Contains(logs, a => a.Action == "Created");
            Assert.Contains(logs, a => a.Action == "Updated");
            Assert.Contains(logs, a => a.Action == "Deleted");
        }
    }

    [Fact]
    public async Task SerialNumbersAreCaseInsensitiveUniqueAndFailedWritesHaveNoAudit()
    {
        await using (var db = Context())
            await new EfAssetRepository(db).AddAsync(new Asset("Pump", "P-1"));
        await using (var db = Context())
            await Assert.ThrowsAsync<PersistenceConflictException>(() => new EfAssetRepository(db).AddAsync(new Asset("Other", "p-1")));
        await using (var db = Context())
        {
            Assert.Equal(1, await db.Assets.CountAsync());
            Assert.Equal(1, await db.AuditLogs.CountAsync());
        }
    }

    [Fact]
    public async Task PaginationFiltersAndAllSortsExecuteInSql()
    {
        await using (var db = Context())
        {
            db.Assets.AddRange(new Asset("Pump Alpha", "A-1"), new Asset("Pump Beta", "B-1"), new Asset("Motor", "M-1"));
            var retired = new Asset("Pump Retired", "R-1");
            retired.Retire();
            db.Assets.Add(retired);
            await db.SaveChangesAsync();
        }
        await using (var db = Context())
        {
            var repository = new EfAssetRepository(db);
            var page = await repository.GetAllAsync(new AssetQuery(PageSize: 1, Search: "Pump", Status: AssetStatus.Operational, Descending: true));
            Assert.Equal(2, page.TotalCount);
            Assert.Equal("Pump Beta", Assert.Single(page.Items).Name);
            var second = await repository.GetAllAsync(new AssetQuery(Page: 2, PageSize: 1, Search: "Pump", Status: AssetStatus.Operational, Descending: true));
            Assert.Equal("Pump Alpha", Assert.Single(second.Items).Name);
            foreach (var sort in new[] { "name", "serialNumber", "createdAt", "status" })
            foreach (var descending in new[] { false, true })
                Assert.Equal(4, (await repository.GetAllAsync(new AssetQuery(SortBy: sort, Descending: descending))).Items.Count);
            Assert.Empty(db.ChangeTracker.Entries<Asset>());
        }
    }

    [Fact]
    public async Task StaleVersionCannotUpdateOrDelete()
    {
        AssetDto created;
        await using (var db = Context())
            created = await new EfAssetRepository(db).AddAsync(new Asset("Pump", "P-1"));
        await using (var db = Context())
            Assert.True(await new EfAssetRepository(db).UpdateDetailsAsync(created.Id, "First", "P-1", created.RowVersion));
        await using (var db = Context())
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => new EfAssetRepository(db).UpdateDetailsAsync(created.Id, "Stale", "P-1", created.RowVersion));
        await using (var db = Context())
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => new EfAssetRepository(db).RemoveAsync(created.Id, created.RowVersion));
        await using (var db = Context())
        {
            Assert.Equal("First", (await db.Assets.SingleAsync()).Name);
            Assert.Equal(2, await db.AuditLogs.CountAsync());
        }
    }

    [Fact]
    public async Task ForeignKeysRestrictDeletionAndRejectMissingAsset()
    {
        await using (var db = Context())
        {
            db.WorkOrders.Add(new WorkOrder(Guid.NewGuid(), "Inspect", "Inspect seals", WorkOrderPriority.Low));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = Context())
            await DevelopmentSeeder.SeedAsync(db);
        await using (var db = Context())
        {
            var asset = await db.Assets.SingleAsync();
            await Assert.ThrowsAsync<PersistenceConflictException>(() => new EfAssetRepository(db).RemoveAsync(asset.Id, asset.RowVersion));
        }
        await using (var db = Context())
            Assert.Equal(3, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task CompletionAndMaintenanceRecordCommitTogether()
    {
        await using (var db = Context())
            await DevelopmentSeeder.SeedAsync(db);
        await using (var db = Context())
        {
            var order = await db.WorkOrders.SingleAsync();
            order.Start();
            await db.SaveChangesAsync();
        }
        await using (var db = Context())
        {
            var order = await db.WorkOrders.SingleAsync();
            db.MaintenanceRecords.Add(order.Complete("Replaced hydraulic seal"));
            await db.SaveChangesAsync();
        }
        await using (var db = Context())
        {
            var order = await db.WorkOrders.SingleAsync();
            var record = await db.MaintenanceRecords.SingleAsync();
            Assert.Equal(WorkOrderStatus.Completed, order.Status);
            Assert.Equal(order.Id, record.WorkOrderId);
            Assert.Equal(order.CompletedAt, record.CompletedAt);
        }
    }

    [Fact]
    public async Task ExplicitTransactionRollsBackBusinessDataAndAudit()
    {
        await using (var db = Context())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.Assets.Add(new Asset("Rolled back", "ROLLBACK"));
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        await using (var db = Context())
        {
            Assert.Empty(await db.Assets.ToListAsync());
            Assert.Empty(await db.AuditLogs.ToListAsync());
        }
    }

    [Fact]
    public async Task ConcurrentTechnicianAssignmentsHaveExactlyOneWinner()
    {
        Guid orderId;
        Guid firstTechnician;
        Guid secondTechnician;
        await using (var db = Context())
        {
            var asset = new Asset("Pump", "P-1");
            var first = new Technician("First", "first@example.test", "Hydraulics");
            var second = new Technician("Second", "second@example.test", "Electrical");
            var order = new WorkOrder(asset.Id, "Inspect", "Inspect seals", WorkOrderPriority.High);
            db.AddRange(asset, first, second, order);
            await db.SaveChangesAsync();
            orderId = order.Id;
            firstTechnician = first.Id;
            secondTechnician = second.Id;
        }
        await using var left = Context();
        await using var right = Context();
        var leftOrder = await left.WorkOrders.SingleAsync(w => w.Id == orderId);
        var rightOrder = await right.WorkOrders.SingleAsync(w => w.Id == orderId);
        leftOrder.Assign(await left.Technicians.SingleAsync(t => t.Id == firstTechnician));
        rightOrder.Assign(await right.Technicians.SingleAsync(t => t.Id == secondTechnician));
        async Task<bool> Save(FleetOpsDbContext db)
        {
            try { await db.SaveChangesAsync(); return true; }
            catch (DbUpdateConcurrencyException) { return false; }
        }
        var outcomes = await Task.WhenAll(Save(left), Save(right));
        Assert.Single(outcomes, success => success);
        await using var check = Context();
        var winner = await check.WorkOrders.SingleAsync(w => w.Id == orderId);
        Assert.Equal(outcomes[0] ? firstTechnician : secondTechnician, winner.AssignedTechnicianId);
        Assert.Equal(1, await check.AuditLogs.CountAsync(a => a.EntityId == orderId && a.Action == "Updated"));
    }

    [Fact]
    public async Task DatabaseChecksRejectInvalidStatusAndMissingAssignment()
    {
        await using var db = Context();
        await DevelopmentSeeder.SeedAsync(db);
        var assetId = (await db.Assets.SingleAsync()).Id;
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Assets SET Status = 99 WHERE Id = {assetId}"));
        var orderId = (await db.WorkOrders.SingleAsync()).Id;
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE WorkOrders SET AssignedTechnicianId = NULL WHERE Id = {orderId}"));
    }

    [Fact]
    public async Task CancelledQueriesDoNotReturnData()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var db = Context();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new EfAssetRepository(db).GetAllAsync(new AssetQuery(), cancellation.Token));
    }
}
