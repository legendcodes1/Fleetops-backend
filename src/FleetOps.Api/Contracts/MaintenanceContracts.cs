using System.ComponentModel.DataAnnotations;
using FleetOps.Application.Models;
using FleetOps.Domain.Enums;
namespace FleetOps.Api.Contracts;

public sealed class TechnicianRequest
{
    [Required, StringLength(200)] public string Name { get; set; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(200)] public string Specialization { get; set; } = "";
    [EnumDataType(typeof(TechnicianAvailability))] public TechnicianAvailability Availability { get; set; }
    public TechnicianDetails ToDetails() => new(Name, Email, Specialization, Availability);
}
public class WorkOrderRequest
{
    [Required, StringLength(200)] public string Title { get; set; } = "";
    [Required, StringLength(4000)] public string Description { get; set; } = "";
    [EnumDataType(typeof(WorkOrderPriority))] public WorkOrderPriority Priority { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public WorkOrderDetails ToDetails() => new(Title, Description, Priority, DueDate);
}
public sealed class CreateWorkOrderRequest : WorkOrderRequest
{
    [Required, NotEmptyGuid] public Guid AssetId { get; set; }
}
public sealed class AssignWorkOrderRequest
{
    [Required, NotEmptyGuid] public Guid TechnicianId { get; set; }
}
public sealed class CompleteWorkOrderRequest
{
    [Required, StringLength(4000)] public string Notes { get; set; } = "";
}
internal sealed class NotEmptyGuidAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is Guid id && id != Guid.Empty;
    public override string FormatErrorMessage(string name) => $"{name} must be a non-empty GUID.";
}
public sealed class TechnicianListRequest
{
    [Range(1, 1_000_000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
    [StringLength(200)] public string? Search { get; set; }
    public string SortBy { get; set; } = "name";
    public bool Descending { get; set; }
    [EnumDataType(typeof(TechnicianAvailability))] public TechnicianAvailability? Availability { get; set; }
    public MaintenanceQuery ToQuery() => new(Page, PageSize, Search, SortBy, Descending, Availability);
}
public sealed class WorkOrderListRequest
{
    [Range(1, 1_000_000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
    [StringLength(200)] public string? Search { get; set; }
    public string SortBy { get; set; } = "createdAt";
    public bool Descending { get; set; }
    [EnumDataType(typeof(WorkOrderStatus))] public WorkOrderStatus? Status { get; set; }
    [EnumDataType(typeof(WorkOrderPriority))] public WorkOrderPriority? Priority { get; set; }
    [NotEmptyGuidIfPresent] public Guid? AssetId { get; set; }
    [NotEmptyGuidIfPresent] public Guid? TechnicianId { get; set; }
    public MaintenanceQuery ToQuery() => new(Page, PageSize, Search, SortBy, Descending, Status: Status,
        Priority: Priority, AssetId: AssetId, TechnicianId: TechnicianId);
}
internal sealed class NotEmptyGuidIfPresentAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is null || value is Guid id && id != Guid.Empty;
}
public sealed record TechnicianResponse(Guid Id, string Name, string Email, string Specialization,
    TechnicianAvailability Availability, byte[] RowVersion)
{
    public static TechnicianResponse From(TechnicianDto t) => new(t.Id, t.Name, t.Email, t.Specialization, t.Availability, t.RowVersion);
}
public sealed record MaintenanceRecordResponse(Guid Id, string Notes, DateTimeOffset CompletedAt);
public sealed record WorkOrderResponse(Guid Id, Guid AssetId, Guid? AssignedTechnicianId, string Title, string Description,
    WorkOrderPriority Priority, WorkOrderStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? DueDate,
    DateTimeOffset? CompletedAt, byte[] RowVersion, MaintenanceRecordResponse? MaintenanceRecord)
{
    public static WorkOrderResponse From(WorkOrderDto w) => new(w.Id, w.AssetId, w.AssignedTechnicianId,
        w.Title, w.Description, w.Priority, w.Status, w.CreatedAt, w.DueDate, w.CompletedAt, w.RowVersion,
        w.MaintenanceRecord is null ? null : new(w.MaintenanceRecord.Id, w.MaintenanceRecord.Notes, w.MaintenanceRecord.CompletedAt));
}
public sealed record DashboardResponse(int TotalAssets, int OperationalAssets, int AssetsInMaintenance, int RetiredAssets,
    int TotalTechnicians, int AvailableTechnicians, int OpenWorkOrders, int AssignedWorkOrders, int InProgressWorkOrders,
    int CompletedWorkOrders, int CancelledWorkOrders, int OverdueWorkOrders)
{
    public static DashboardResponse From(DashboardSummary s) => new(s.TotalAssets, s.OperationalAssets, s.AssetsInMaintenance,
        s.RetiredAssets, s.TotalTechnicians, s.AvailableTechnicians, s.OpenWorkOrders, s.AssignedWorkOrders, s.InProgressWorkOrders,
        s.CompletedWorkOrders, s.CancelledWorkOrders, s.OverdueWorkOrders);
}
