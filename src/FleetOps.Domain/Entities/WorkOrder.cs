using FleetOps.Domain.Enums;
using FleetOps.Domain.Exceptions;
using FleetOps.Domain.Validation;

namespace FleetOps.Domain.Entities;

public class WorkOrder
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid AssetId { get; private set; }
    public Guid? AssignedTechnicianId { get; private set; }
    public string Title { get; private set; } = "";
    public string Description { get; private set; } = "";
    public WorkOrderPriority Priority { get; private set; }
    public WorkOrderStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DueDate { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private WorkOrder() { }

    public WorkOrder(Guid assetId, string title, string description, WorkOrderPriority priority, DateTimeOffset? dueDate = null)
    {
        AssetId = Guard.Id(assetId, nameof(assetId));
        Title = Guard.Text(title, 200, nameof(title));
        Description = Guard.Text(description, 4000, nameof(description));
        Priority = Guard.Defined(priority, nameof(priority));
        if (dueDate < CreatedAt)
            throw new ArgumentException("Due date cannot precede creation.", nameof(dueDate));
        DueDate = dueDate;
    }

    public void Assign(Technician technician)
    {
        ArgumentNullException.ThrowIfNull(technician);
        if (Status != WorkOrderStatus.Open)
            throw new DomainRuleException("Only open work orders can be assigned.");
        if (technician.Availability != TechnicianAvailability.Available)
            throw new DomainRuleException("Technician is unavailable.");
        AssignedTechnicianId = technician.Id;
        Status = WorkOrderStatus.Assigned;
    }

    public void UpdateDetails(string title, string description, WorkOrderPriority priority, DateTimeOffset? dueDate)
    {
        if (Status is WorkOrderStatus.Completed or WorkOrderStatus.Cancelled)
            throw new DomainRuleException("Cannot update a terminal work order.");
        var validTitle = Guard.Text(title, 200, nameof(title));
        var validDescription = Guard.Text(description, 4000, nameof(description));
        Guard.Defined(priority, nameof(priority));
        if (dueDate < CreatedAt) throw new ArgumentException("Due date cannot precede creation.", nameof(dueDate));
        Title = validTitle;
        Description = validDescription;
        Priority = priority;
        DueDate = dueDate;
    }

    public void Start()
    {
        if (Status != WorkOrderStatus.Assigned || AssignedTechnicianId is null)
            throw new DomainRuleException("Work order must be assigned before starting.");
        Status = WorkOrderStatus.InProgress;
    }

    public MaintenanceRecord Complete(string notes)
    {
        if (Status != WorkOrderStatus.InProgress)
            throw new DomainRuleException("Only in-progress work orders can be completed.");
        var completedAt = DateTimeOffset.UtcNow;
        var record = new MaintenanceRecord(Id, notes, completedAt);
        CompletedAt = completedAt;
        Status = WorkOrderStatus.Completed;
        return record;
    }

    public void Cancel()
    {
        if (Status is WorkOrderStatus.Completed or WorkOrderStatus.Cancelled)
            throw new DomainRuleException("A terminal work order cannot be cancelled.");
        Status = WorkOrderStatus.Cancelled;
    }
}
