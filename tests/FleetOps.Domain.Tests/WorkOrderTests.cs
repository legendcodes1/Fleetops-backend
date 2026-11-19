using FleetOps.Domain.Entities;
using FleetOps.Domain.Enums;
using FleetOps.Domain.Exceptions;

namespace FleetOps.Domain.Tests;

public sealed class WorkOrderTests
{
    [Fact]
    public void AssignmentRequiresAvailabilityAndOpenState()
    {
        var technician = new Technician("Alex", "alex@example.test", "Hydraulics");
        var order = new WorkOrder(Guid.NewGuid(), "Inspect", "Inspect seals", WorkOrderPriority.High);
        Assert.Throws<DomainRuleException>(order.Start);
        technician.UpdateDetails("Alex", "alex@example.test", "Hydraulics", TechnicianAvailability.Unavailable);
        Assert.Throws<DomainRuleException>(() => order.Assign(technician));
        technician.UpdateDetails("Alex", "alex@example.test", "Hydraulics", TechnicianAvailability.Available);
        order.Assign(technician);
        Assert.Throws<DomainRuleException>(() => order.Assign(technician));
        order.Start();
        Assert.Throws<ArgumentException>(() => order.Complete(" "));
        Assert.Equal(WorkOrderStatus.InProgress, order.Status);
        var record = order.Complete("Replaced seal");
        Assert.Equal(order.Id, record.WorkOrderId);
        Assert.Equal(order.CompletedAt, record.CompletedAt);
        Assert.Throws<DomainRuleException>(() => order.Complete("Again"));
        Assert.Throws<DomainRuleException>(order.Cancel);
    }

    [Fact]
    public void CancellationIsTerminal()
    {
        var order = new WorkOrder(Guid.NewGuid(), "Inspect", "Inspect seals", WorkOrderPriority.Low);
        order.Cancel();
        Assert.Throws<DomainRuleException>(order.Start);
        Assert.Throws<DomainRuleException>(order.Cancel);
    }

    [Fact]
    public void DomainRejectsInvalidValuesAndOversizedDetails()
    {
        Assert.Throws<ArgumentException>(() => new Asset(new string('x', 201), "P-1"));
        Assert.Throws<ArgumentException>(() => new Asset("Pump", new string('x', 101)));
        Assert.Throws<ArgumentException>(() => new Technician("Alex", "not-an-email", "Hydraulics"));
        Assert.Throws<ArgumentException>(() => new WorkOrder(Guid.Empty, "Inspect", "Inspect seals", WorkOrderPriority.Low));
        Assert.Throws<ArgumentException>(() => new WorkOrder(Guid.NewGuid(), "Inspect", "Inspect seals", (WorkOrderPriority)99));
        Assert.Throws<ArgumentException>(() => new WorkOrder(Guid.NewGuid(), "Inspect", "Inspect seals", WorkOrderPriority.Low, DateTimeOffset.MinValue));
    }
}
