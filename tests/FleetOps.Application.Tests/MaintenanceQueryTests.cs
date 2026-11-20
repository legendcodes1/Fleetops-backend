using FleetOps.Application.Models;
using FleetOps.Domain.Enums;
namespace FleetOps.Application.Tests;

public sealed class MaintenanceQueryTests
{
    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(1000001, 20)]
    public void PaginationBoundsApplyToBothCollections(int page, int size)
    {
        Assert.Throws<ArgumentException>(() => new MaintenanceQuery(Page: page, PageSize: size).Validate(false));
        Assert.Throws<ArgumentException>(() => new MaintenanceQuery(Page: page, PageSize: size, SortBy: "title").Validate(true));
    }

    [Fact]
    public void InvalidFiltersAndCrossResourceSortsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new MaintenanceQuery(Availability: (TechnicianAvailability)99).Validate(false));
        Assert.Throws<ArgumentException>(() => new MaintenanceQuery(SortBy: "title", Status: (WorkOrderStatus)99).Validate(true));
        Assert.Throws<ArgumentException>(() => new MaintenanceQuery(SortBy: "title", Priority: (WorkOrderPriority)99).Validate(true));
        Assert.Throws<ArgumentException>(() => new MaintenanceQuery(SortBy: "title", AssetId: Guid.Empty).Validate(true));
        Assert.Throws<ArgumentException>(() => new MaintenanceQuery(SortBy: "title", TechnicianId: Guid.Empty).Validate(true));
        Assert.Throws<ArgumentException>(() => new MaintenanceQuery(Search: new string('x', 201)).Validate(false));
        Assert.Throws<ArgumentException>(() => new MaintenanceQuery(SortBy: "dueDate").Validate(false));
        Assert.Throws<ArgumentException>(() => new MaintenanceQuery(SortBy: "email").Validate(true));
    }
}
