using FleetOps.Domain.Validation;
namespace FleetOps.Domain.Entities;

public class MaintenanceRecord
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid WorkOrderId { get; private set; }
    public string Notes { get; private set; } = "";
    public DateTimeOffset CompletedAt { get; private set; }

    private MaintenanceRecord() { }
    internal MaintenanceRecord(Guid workOrderId, string notes, DateTimeOffset completedAt)
    {
        WorkOrderId = Guard.Id(workOrderId, nameof(workOrderId));
        Notes = Guard.Text(notes, 4000, nameof(notes));
        CompletedAt = completedAt;
    }
}
