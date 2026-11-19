using FleetOps.Application.Models;
using FleetOps.Domain.Entities;
namespace FleetOps.Application.Interfaces;

// Shared persistence boundary for maintenance workflows; one Save commits order, record and audits together.
public interface IMaintenanceStore
{
    Task<PagedResult<TechnicianDto>> ListTechniciansAsync(MaintenanceQuery query, CancellationToken token);
    Task<PagedResult<WorkOrderDto>> ListWorkOrdersAsync(MaintenanceQuery query, CancellationToken token);
    Task<Technician?> FindTechnicianAsync(Guid id, CancellationToken token);
    Task<WorkOrder?> FindWorkOrderAsync(Guid id, CancellationToken token);
    Task<WorkOrderDto?> ReadWorkOrderAsync(Guid id, CancellationToken token);
    void AddTechnician(Technician technician);
    void AddWorkOrder(WorkOrder order);
    void AddRecord(MaintenanceRecord record);
    void ExpectVersion(object entity, byte[] expectedVersion);
    Task SaveAsync(CancellationToken token);
    Task<DashboardSummary> SummaryAsync(CancellationToken token);
}
