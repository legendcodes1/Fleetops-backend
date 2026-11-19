using FleetOps.Domain.Enums;
namespace FleetOps.Application.Models;

public enum AssetTransition { Maintenance, ReturnToService, Retire }
public sealed record TechnicianDto(Guid Id, string Name, string Email, string Specialization, TechnicianAvailability Availability, byte[] RowVersion);
public sealed record MaintenanceRecordDto(Guid Id, string Notes, DateTimeOffset CompletedAt);
public sealed record WorkOrderDto(Guid Id, Guid AssetId, Guid? AssignedTechnicianId, string Title, string Description,
    WorkOrderPriority Priority, WorkOrderStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? DueDate,
    DateTimeOffset? CompletedAt, byte[] RowVersion, MaintenanceRecordDto? MaintenanceRecord);
public sealed record WorkOrderDetails(string Title, string Description, WorkOrderPriority Priority, DateTimeOffset? DueDate);
public sealed record TechnicianDetails(string Name, string Email, string Specialization, TechnicianAvailability Availability);
public sealed record DashboardSummary(int TotalAssets, int OperationalAssets, int AssetsInMaintenance, int RetiredAssets,
    int TotalTechnicians, int AvailableTechnicians, int OpenWorkOrders, int AssignedWorkOrders, int InProgressWorkOrders,
    int CompletedWorkOrders, int CancelledWorkOrders, int OverdueWorkOrders);

public sealed record MaintenanceQuery(int Page = 1, int PageSize = 20, string? Search = null, string SortBy = "name",
    bool Descending = false, TechnicianAvailability? Availability = null, WorkOrderStatus? Status = null,
    WorkOrderPriority? Priority = null, Guid? AssetId = null, Guid? TechnicianId = null)
{
    public void Validate(bool workOrders)
    {
        if (Page is < 1 or > 1_000_000 || PageSize is < 1 or > 100)
            throw new ArgumentException("Page must be 1-1000000 and PageSize 1-100.");
        if (Search?.Length > 200) throw new ArgumentException("Search cannot exceed 200 characters.");
        var sorts = workOrders ? new[] { "title", "createdAt", "dueDate", "priority", "status" } : new[] { "name", "email", "specialization", "availability" };
        if (!sorts.Contains(SortBy)) throw new ArgumentException("Unsupported sort field.");
        if (Availability.HasValue && !Enum.IsDefined(Availability.Value) || Status.HasValue && !Enum.IsDefined(Status.Value)
            || Priority.HasValue && !Enum.IsDefined(Priority.Value)) throw new ArgumentException("Invalid enum filter.");
        if (AssetId == Guid.Empty || TechnicianId == Guid.Empty) throw new ArgumentException("Filter identifiers cannot be empty.");
    }
}
