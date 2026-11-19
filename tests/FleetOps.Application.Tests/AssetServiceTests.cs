using FleetOps.Application.Services;
using FleetOps.Infrastructure.Persistence;

namespace FleetOps.Application.Tests;

public sealed class AssetServiceTests
{
    [Fact]
    public void SeparateServicesSharePersistenceAndCrudReturnsSnapshots()
    {
        var repository = new InMemoryAssetRepository();
        var first = new AssetService(repository);
        var second = new AssetService(repository);
        var created = first.Create("Pump", "P-1");
        Assert.Equal(created, second.GetById(created.Id));
        var snapshot = second.GetAll();
        Assert.True(second.Update(created.Id, "Motor", "M-1"));
        Assert.Equal("Pump", snapshot.Single().Name);
        Assert.Equal("Motor", first.GetById(created.Id)!.Name);
        Assert.True(first.Delete(created.Id));
        Assert.Null(second.GetById(created.Id));
        Assert.Empty(second.GetAll());
    }

    [Fact]
    public void MissingMutationsReturnFalse()
    {
        var service = new AssetService(new InMemoryAssetRepository());
        Assert.False(service.Update(Guid.NewGuid(), "Pump", "P-1"));
        Assert.False(service.Delete(Guid.NewGuid()));
    }

    [Fact]
    public void InvalidUpdateLeavesStoredDetailsUnchanged()
    {
        var service = new AssetService(new InMemoryAssetRepository());
        var asset = service.Create("Pump", "P-1");
        Assert.Throws<ArgumentException>(() => service.Update(asset.Id, "Motor", ""));
        Assert.Equal(asset, service.GetById(asset.Id));
        Assert.Throws<ArgumentException>(() => service.Create("Pump", " "));
        Assert.Single(service.GetAll());
    }

    [Fact]
    public async Task ConcurrentCreatesAreNotLost()
    {
        var repository = new InMemoryAssetRepository();
        await Task.WhenAll(Enumerable.Range(0, 100).Select(i =>
            Task.Run(() => new AssetService(repository).Create($"Pump {i}", $"P-{i}"))));
        Assert.Equal(100, repository.GetAll().Count);
        Assert.Equal(100, repository.GetAll().Select(a => a.Id).Distinct().Count());
    }

    [Fact]
    public void SeedDataIsExplicitAndRepositoriesAreIsolated()
    {
        Assert.Empty(new InMemoryAssetRepository().GetAll());
        Assert.Equal(2, new InMemoryAssetRepository(seedDevelopmentData: true).GetAll().Count);
    }
}
