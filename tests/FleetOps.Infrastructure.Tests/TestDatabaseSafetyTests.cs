using FleetOps.Testing;

namespace FleetOps.Infrastructure.Tests;

public sealed class TestDatabaseSafetyTests
{
    [Theory]
    [InlineData("FleetOps")]
    [InlineData("master")]
    [InlineData("")]
    [InlineData("FleetOps_Test_")]
    [InlineData("FleetOps_Test_not-a-guid")]
    [InlineData("FleetOps_Test_00000000-0000-0000-0000-000000000000")]
    public void CleanupRejectsNamesNotOwnedByTheTestHarness(string name) =>
        Assert.Throws<InvalidOperationException>(() => SqlTestDatabase.RequireOwnedDatabase(name));

    [Fact]
    public void CleanupAcceptsOnlyTheGeneratedNamespace() =>
        SqlTestDatabase.RequireOwnedDatabase("FleetOps_Test_" + Guid.NewGuid().ToString("N"));
}
