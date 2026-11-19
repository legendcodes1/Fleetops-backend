using FleetOps.Application.Models;
using FleetOps.Domain.Enums;

namespace FleetOps.Application.Tests;

public sealed class AssetQueryTests
{
    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(1000001, 20)]
    public void PaginationRejectsUnboundedOrInvalidRequests(int page, int size) =>
        Assert.Throws<ArgumentException>(() => new AssetQuery(page, size).Validate());

    [Fact]
    public void UnknownSortAndStatusAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new AssetQuery(SortBy: "Name; DROP TABLE Assets").Validate());
        Assert.Throws<ArgumentException>(() => new AssetQuery(Status: (AssetStatus)99).Validate());
        Assert.Throws<ArgumentException>(() => new AssetQuery(Search: new string('x', 201)).Validate());
    }

    [Theory]
    [InlineData("name")]
    [InlineData("serialNumber")]
    [InlineData("createdAt")]
    [InlineData("status")]
    public void AllowedSortsAndPaginationBoundariesAreAccepted(string sort)
    {
        new AssetQuery(Page: 1000000, PageSize: 100, SortBy: sort).Validate();
        new AssetQuery(Page: 1, PageSize: 1, SortBy: sort, Descending: true).Validate();
    }
}
