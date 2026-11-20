using FleetOps.Application.Exceptions;
using FleetOps.Application.Models;
using FleetOps.Application.Services;
using FleetOps.Domain.Entities;
using FleetOps.Domain.Enums;
using FleetOps.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using FleetOps.Testing;
using FleetOps.Application.Security;

namespace FleetOps.Infrastructure.Tests;

public sealed class SqlPersistenceTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    private SqlTestDatabase database = null!;
    private FleetOpsDbContext Context() => database.CreateContext();

    public async Task InitializeAsync()
    {
        database = await SqlTestDatabase.CreateAsync(fixture.Container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (database is not null) await database.DisposeAsync();
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
            var service = new AssetService(new EfAssetRepository(db), new PersistenceTestActor());
            var created = await service.CreateAsync("Pump", "P-1");
            id = created.Id;
            version = created.RowVersion;
            Assert.Equal(8, version.Length);
            Assert.Equal(created.CreatedAt, created.UpdatedAt);
        }
        await using (var db = Context())
        {
            var service = new AssetService(new EfAssetRepository(db), new PersistenceTestActor());
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

    [Fact]
    public async Task FailedCompletionSaveRollsBackOrderRecordAndGeneratedAudits()
    {
        Guid orderId;
        await using (var db = Context())
        {
            await DevelopmentSeeder.SeedAsync(db);
            var order = await db.WorkOrders.SingleAsync();
            order.Start();
            await db.SaveChangesAsync();
            orderId = order.Id;
        }
        await using (var db = Context())
        {
            var order = await db.WorkOrders.SingleAsync();
            var record = order.Complete("Valid domain notes");
            db.MaintenanceRecords.Add(record);
            // Test-only fault injection bypasses the domain to force a real SQL length-constraint failure.
            db.Entry(record).Property(r => r.Notes).CurrentValue = new string('x', 4001);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = Context())
        {
            var stored = await db.WorkOrders.SingleAsync();
            Assert.Equal(WorkOrderStatus.InProgress, stored.Status);
            Assert.Null(stored.CompletedAt);
            Assert.Empty(await db.MaintenanceRecords.ToListAsync());
            Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.EntityId == orderId));
            Assert.False(await db.AuditLogs.AnyAsync(a => a.EntityType == "MaintenanceRecord"));
        }
    }

    [Fact]
    public async Task CancelledWritePersistsNeitherAssetNorAudit()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using (var db = Context())
        {
            db.Assets.Add(new Asset("Cancelled", "CANCELLED-1"));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.SaveChangesAsync(cancellation.Token));
        }
        await using (var db = Context())
        {
            Assert.Empty(await db.Assets.ToListAsync());
            Assert.Empty(await db.AuditLogs.ToListAsync());
        }
    }

    [Fact]
    public async Task ApplicationServicesEnforceTechnicianScopeWithoutControllerPolicies()
    {
        Guid ownId;
        Guid otherOrderId;
        byte[] otherVersion;
        Guid assetId;
        await using (var db = Context())
        {
            await DevelopmentSeeder.SeedAsync(db);
            var own = await db.Technicians.SingleAsync();
            var other = new Technician("Other", "other@example.test", "Electrical");
            assetId = (await db.Assets.SingleAsync()).Id;
            var otherOrder = new WorkOrder(assetId, "Other assignment", "Inspect motor", WorkOrderPriority.High);
            otherOrder.Assign(other);
            db.AddRange(other, otherOrder);
            await db.SaveChangesAsync();
            ownId = own.Id;
            otherOrderId = otherOrder.Id;
            otherVersion = otherOrder.RowVersion;
        }
        await using (var db = Context())
        {
            var actor = new PersistenceTestActor(Roles.Technician, ownId);
            var assets = new EfAssetRepository(db);
            var maintenance = new MaintenanceService(new EfMaintenanceStore(db), assets, actor);
            var page = await maintenance.ListWorkOrdersAsync(new MaintenanceQuery(SortBy: "title"), CancellationToken.None);
            Assert.Equal(1, page.TotalCount);
            Assert.Equal(ownId, Assert.Single(page.Items).AssignedTechnicianId);
            await Assert.ThrowsAsync<AccessDeniedException>(() => maintenance.GetWorkOrderAsync(otherOrderId, CancellationToken.None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => maintenance.StartAsync(otherOrderId, otherVersion, CancellationToken.None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => maintenance.CreateWorkOrderAsync(assetId,
                new WorkOrderDetails("Forbidden", "Forbidden", WorkOrderPriority.Low, null), CancellationToken.None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => new AssetService(assets, actor).CreateAsync("Forbidden", "FORBIDDEN"));
        }
        await using (var db = Context())
        {
            Assert.Equal(WorkOrderStatus.Assigned, (await db.WorkOrders.SingleAsync(w => w.Id == otherOrderId)).Status);
            Assert.Equal(2, await db.WorkOrders.CountAsync());
            Assert.Single(await db.Assets.ToListAsync());
            Assert.False(await db.AuditLogs.AnyAsync(a => a.Action == "Updated"));
        }
    }
}
