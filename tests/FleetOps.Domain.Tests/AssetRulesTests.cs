using FleetOps.Domain.Entities;
using FleetOps.Domain.Enums;
using FleetOps.Domain.Exceptions;

namespace FleetOps.Domain.Tests;

public sealed class AssetRulesTests
{
    [Fact]
    public void DocumentedMaximumLengthsAndNormalizationAreSupported()
    {
        var asset = new Asset("  " + new string('n', 200) + " ", " " + new string('s', 100) + " ");
        Assert.Equal(200, asset.Name.Length);
        Assert.Equal(100, asset.SerialNumber.Length);
        Assert.NotEqual(Guid.Empty, asset.Id);
        Assert.Equal(asset.CreatedAt, asset.UpdatedAt);
        var id = asset.Id;
        var created = asset.CreatedAt;
        asset.UpdateDetails(" Updated ", " S-2 ");
        Assert.Equal("Updated", asset.Name);
        Assert.Equal("S-2", asset.SerialNumber);
        Assert.Equal(id, asset.Id);
        Assert.Equal(created, asset.CreatedAt);
        Assert.Equal(AssetStatus.Operational, asset.Status);
    }
    [Theory]
    [InlineData("", "P-1")]
    [InlineData(" ", "P-1")]
    [InlineData(null, "P-1")]
    [InlineData("Pump", "")]
    [InlineData("Pump", " ")]
    [InlineData("Pump", null)]
    public void ConstructorRejectsMissingDetails(string? name, string? serial)
    {
        Assert.Throws<ArgumentException>(() => new Asset(name!, serial!));
    }

    [Fact]
    public void MaintenanceLifecycleEnforcesSourceStates()
    {
        var asset = new Asset("Pump", "P-1");
        Assert.Throws<DomainRuleException>(asset.ReturnToService);
        asset.SendToMaintenance();
        Assert.Throws<DomainRuleException>(asset.SendToMaintenance);
        asset.ReturnToService();
        Assert.Equal(AssetStatus.Operational, asset.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetirementIsTerminalFromEitherActiveState(bool inMaintenance)
    {
        var asset = new Asset("Pump", "P-1");
        if (inMaintenance) asset.SendToMaintenance();
        asset.Retire();
        Assert.Equal(AssetStatus.Retired, asset.Status);
        Assert.Throws<DomainRuleException>(asset.Retire);
        Assert.Throws<DomainRuleException>(asset.SendToMaintenance);
        Assert.Throws<DomainRuleException>(asset.ReturnToService);
        Assert.Throws<DomainRuleException>(() => asset.UpdateDetails("Motor", "M-1"));
    }

    [Theory]
    [InlineData("", "M-1")]
    [InlineData("Motor", "")]
    public void InvalidUpdateDoesNotPartiallyMutateAsset(string name, string serial)
    {
        var asset = new Asset("Pump", "P-1");
        Assert.Throws<ArgumentException>(() => asset.UpdateDetails(name, serial));
        Assert.Equal("Pump", asset.Name);
        Assert.Equal("P-1", asset.SerialNumber);
    }
}
