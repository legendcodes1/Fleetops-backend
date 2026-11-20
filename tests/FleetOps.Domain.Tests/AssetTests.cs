using FleetOps.Domain.Enums;
using FleetOps.Domain.Entities;
using Xunit;

namespace FleetOps.Domain.Tests.Entities;

public class AssetTests
{   
    //Marks a method as an xUnit test
    [Fact]
    public void SendToMaintenance_WhenOperational_UpdatesStatus()
    {
        // Arrange
        var asset = new Asset("Hydraulic Pump", "HP-001");

        // Act
        asset.SendToMaintenance();

        // Assert
        Assert.Equal(AssetStatus.Maintenance, asset.Status);
    }
}