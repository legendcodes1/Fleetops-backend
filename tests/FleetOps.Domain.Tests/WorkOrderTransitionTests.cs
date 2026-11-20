using FleetOps.Domain.Entities;
using FleetOps.Domain.Enums;
using FleetOps.Domain.Exceptions;

namespace FleetOps.Domain.Tests;

public sealed class WorkOrderTransitionTests
{
    public static TheoryData<WorkOrderStatus> States => new()
    {
        WorkOrderStatus.Open, WorkOrderStatus.Assigned, WorkOrderStatus.InProgress,
        WorkOrderStatus.Completed, WorkOrderStatus.Cancelled
    };

    private static WorkOrder InState(WorkOrderStatus status)
    {
        var order = new WorkOrder(Guid.NewGuid(), "Inspect", "Inspect hydraulic seals", WorkOrderPriority.High);
        if (status is WorkOrderStatus.Assigned or WorkOrderStatus.InProgress or WorkOrderStatus.Completed)
            order.Assign(new Technician("Alex", "alex@example.test", "Hydraulics"));
        if (status is WorkOrderStatus.InProgress or WorkOrderStatus.Completed) order.Start();
        if (status == WorkOrderStatus.Completed) order.Complete("Replaced seal");
        if (status == WorkOrderStatus.Cancelled) order.Cancel();
        return order;
    }

    [Theory]
    [MemberData(nameof(States))]
    public void OnlyOpenOrdersCanBeAssigned(WorkOrderStatus initial)
    {
        var order = InState(initial);
        var originalAssignee = order.AssignedTechnicianId;
        var technician = new Technician("Second", "second@example.test", "Electrical");
        if (initial == WorkOrderStatus.Open)
        {
            order.Assign(technician);
            Assert.Equal(WorkOrderStatus.Assigned, order.Status);
            Assert.Equal(technician.Id, order.AssignedTechnicianId);
        }
        else
        {
            Assert.Throws<DomainRuleException>(() => order.Assign(technician));
            Assert.Equal(initial, order.Status);
            Assert.Equal(originalAssignee, order.AssignedTechnicianId);
        }
    }

    [Theory]
    [MemberData(nameof(States))]
    public void OnlyAssignedOrdersCanStart(WorkOrderStatus initial)
    {
        var order = InState(initial);
        var assignee = order.AssignedTechnicianId;
        if (initial == WorkOrderStatus.Assigned)
        {
            order.Start();
            Assert.Equal(WorkOrderStatus.InProgress, order.Status);
            Assert.Equal(assignee, order.AssignedTechnicianId);
        }
        else
        {
            Assert.Throws<DomainRuleException>(order.Start);
            Assert.Equal(initial, order.Status);
        }
    }

    [Theory]
    [MemberData(nameof(States))]
    public void OnlyInProgressOrdersCanComplete(WorkOrderStatus initial)
    {
        var order = InState(initial);
        var originalCompletion = order.CompletedAt;
        if (initial == WorkOrderStatus.InProgress)
        {
            var record = order.Complete("  Replaced seal  ");
            Assert.Equal(WorkOrderStatus.Completed, order.Status);
            Assert.Equal(order.Id, record.WorkOrderId);
            Assert.Equal(order.CompletedAt, record.CompletedAt);
            Assert.Equal("Replaced seal", record.Notes);
        }
        else
        {
            Assert.Throws<DomainRuleException>(() => order.Complete("Replaced seal"));
            Assert.Equal(initial, order.Status);
            Assert.Equal(originalCompletion, order.CompletedAt);
        }
    }

    [Theory]
    [MemberData(nameof(States))]
    public void OnlyNonterminalOrdersCanCancel(WorkOrderStatus initial)
    {
        var order = InState(initial);
        var assignee = order.AssignedTechnicianId;
        if (initial is WorkOrderStatus.Completed or WorkOrderStatus.Cancelled)
        {
            Assert.Throws<DomainRuleException>(order.Cancel);
            Assert.Equal(initial, order.Status);
        }
        else
        {
            order.Cancel();
            Assert.Equal(WorkOrderStatus.Cancelled, order.Status);
            Assert.Null(order.CompletedAt);
            Assert.Equal(assignee, order.AssignedTechnicianId);
        }
    }

    [Fact]
    public void DueDateValidationUsesTheCreationInstantAndFailedEditIsAtomic()
    {
        var order = InState(WorkOrderStatus.Assigned);
        var id = order.Id;
        var assetId = order.AssetId;
        var assignee = order.AssignedTechnicianId;
        Assert.Throws<ArgumentException>(() => order.UpdateDetails("Changed", "Changed", WorkOrderPriority.Low, order.CreatedAt.AddTicks(-1)));
        Assert.Equal("Inspect", order.Title);
        Assert.Equal(WorkOrderPriority.High, order.Priority);
        order.UpdateDetails("Changed", "Changed", WorkOrderPriority.Low, order.CreatedAt.ToOffset(TimeSpan.FromHours(5)));
        Assert.Equal(order.CreatedAt, order.DueDate);
        Assert.Equal(id, order.Id);
        Assert.Equal(assetId, order.AssetId);
        Assert.Equal(assignee, order.AssignedTechnicianId);
        Assert.Equal(WorkOrderStatus.Assigned, order.Status);
    }

    [Fact]
    public void OversizedCompletionNotesCannotPartiallyCompleteAnOrder()
    {
        var order = InState(WorkOrderStatus.InProgress);
        Assert.Throws<ArgumentException>(() => order.Complete(new string('x', 4001)));
        Assert.Equal(WorkOrderStatus.InProgress, order.Status);
        Assert.Null(order.CompletedAt);
        var record = order.Complete(new string('x', 4000));
        Assert.Equal(4000, record.Notes.Length);
        Assert.Equal(WorkOrderStatus.Completed, order.Status);
    }
}
