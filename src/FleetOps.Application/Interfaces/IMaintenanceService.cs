using FleetOps.Application.Models;
namespace FleetOps.Application.Interfaces;

public interface IMaintenanceService
{
    Task<PagedResult<TechnicianDto>> ListTechniciansAsync(MaintenanceQuery query, CancellationToken token);
    Task<TechnicianDto?> GetTechnicianAsync(Guid id, CancellationToken token);
    Task<TechnicianDto> CreateTechnicianAsync(TechnicianDetails details, CancellationToken token);
    Task<TechnicianDto> UpdateTechnicianAsync(Guid id, TechnicianDetails details, byte[] version, CancellationToken token);
    Task<PagedResult<WorkOrderDto>> ListWorkOrdersAsync(MaintenanceQuery query, CancellationToken token);
    Task<WorkOrderDto?> GetWorkOrderAsync(Guid id, CancellationToken token);
    Task<WorkOrderDto> CreateWorkOrderAsync(Guid assetId, WorkOrderDetails details, CancellationToken token);
    Task<WorkOrderDto> UpdateWorkOrderAsync(Guid id, WorkOrderDetails details, byte[] version, CancellationToken token);
    Task<WorkOrderDto> AssignAsync(Guid id, Guid technicianId, byte[] version, CancellationToken token);
    Task<WorkOrderDto> StartAsync(Guid id, byte[] version, CancellationToken token);
    Task<WorkOrderDto> CompleteAsync(Guid id, string notes, byte[] version, CancellationToken token);
    Task<WorkOrderDto> CancelAsync(Guid id, byte[] version, CancellationToken token);
    Task<DashboardSummary> SummaryAsync(CancellationToken token);
}
