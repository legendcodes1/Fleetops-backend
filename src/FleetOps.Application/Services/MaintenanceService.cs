using FleetOps.Application.Exceptions;
using FleetOps.Application.Interfaces;
using FleetOps.Application.Models;
using FleetOps.Domain.Entities;
using FleetOps.Domain.Enums;
using FleetOps.Domain.Exceptions;
namespace FleetOps.Application.Services;

public sealed class MaintenanceService(IMaintenanceStore store, IAssetRepository assets) : IMaintenanceService
{
    public Task<PagedResult<TechnicianDto>> ListTechniciansAsync(MaintenanceQuery query, CancellationToken token)
    {
        query.Validate(false);
        return store.ListTechniciansAsync(query, token);
    }
    public async Task<TechnicianDto?> GetTechnicianAsync(Guid id, CancellationToken token)
    {
        var technician = await store.FindTechnicianAsync(id, token);
        return technician is null ? null : Map(technician);
    }
    public async Task<TechnicianDto> CreateTechnicianAsync(TechnicianDetails details, CancellationToken token)
    {
        var technician = new Technician(details.Name, details.Email, details.Specialization);
        technician.UpdateDetails(details.Name, details.Email, details.Specialization, details.Availability);
        store.AddTechnician(technician);
        await store.SaveAsync(token);
        return Map(technician);
    }
    public async Task<TechnicianDto> UpdateTechnicianAsync(Guid id, TechnicianDetails details, byte[] version, CancellationToken token)
    {
        var technician = await store.FindTechnicianAsync(id, token) ?? throw Missing("Technician");
        store.ExpectVersion(technician, version);
        technician.UpdateDetails(details.Name, details.Email, details.Specialization, details.Availability);
        await store.SaveAsync(token);
        return Map(technician);
    }
    public Task<PagedResult<WorkOrderDto>> ListWorkOrdersAsync(MaintenanceQuery query, CancellationToken token)
    {
        query.Validate(true);
        return store.ListWorkOrdersAsync(query, token);
    }
    public Task<WorkOrderDto?> GetWorkOrderAsync(Guid id, CancellationToken token) => store.ReadWorkOrderAsync(id, token);
    public async Task<WorkOrderDto> CreateWorkOrderAsync(Guid assetId, WorkOrderDetails details, CancellationToken token)
    {
        await RequireActiveAsset(assetId, token);
        var order = new WorkOrder(assetId, details.Title, details.Description, details.Priority, details.DueDate);
        store.AddWorkOrder(order);
        await store.SaveAsync(token);
        return (await store.ReadWorkOrderAsync(order.Id, token))!;
    }
    public async Task<WorkOrderDto> UpdateWorkOrderAsync(Guid id, WorkOrderDetails details, byte[] version, CancellationToken token)
    {
        var order = await Load(id, version, token);
        order.UpdateDetails(details.Title, details.Description, details.Priority, details.DueDate);
        return await Save(order, token);
    }
    public async Task<WorkOrderDto> AssignAsync(Guid id, Guid technicianId, byte[] version, CancellationToken token)
    {
        var order = await Load(id, version, token);
        await RequireActiveAsset(order.AssetId, token);
        order.Assign(await store.FindTechnicianAsync(technicianId, token) ?? throw Missing("Technician"));
        return await Save(order, token);
    }
    public async Task<WorkOrderDto> StartAsync(Guid id, byte[] version, CancellationToken token)
    {
        var order = await Load(id, version, token);
        await RequireActiveAsset(order.AssetId, token);
        order.Start();
        return await Save(order, token);
    }
    public async Task<WorkOrderDto> CompleteAsync(Guid id, string notes, byte[] version, CancellationToken token)
    {
        var order = await Load(id, version, token);
        store.AddRecord(order.Complete(notes));
        return await Save(order, token);
    }
    public async Task<WorkOrderDto> CancelAsync(Guid id, byte[] version, CancellationToken token)
    {
        var order = await Load(id, version, token);
        order.Cancel();
        return await Save(order, token);
    }
    public Task<DashboardSummary> SummaryAsync(CancellationToken token) => store.SummaryAsync(token);
    private async Task<WorkOrder> Load(Guid id, byte[] version, CancellationToken token)
    {
        var order = await store.FindWorkOrderAsync(id, token) ?? throw Missing("Work order");
        store.ExpectVersion(order, version);
        return order;
    }
    private async Task<WorkOrderDto> Save(WorkOrder order, CancellationToken token)
    {
        await store.SaveAsync(token);
        return (await store.ReadWorkOrderAsync(order.Id, token))!;
    }
    private async Task RequireActiveAsset(Guid id, CancellationToken token)
    {
        var asset = await assets.GetByIdAsync(id, token) ?? throw Missing("Asset");
        if (asset.Status == AssetStatus.Retired) throw new DomainRuleException("Cannot create, assign, or start work for a retired asset.");
    }
    private static ResourceNotFoundException Missing(string entity) => new($"{entity} was not found.");
    private static TechnicianDto Map(Technician t) => new(t.Id, t.Name, t.Email, t.Specialization, t.Availability, t.RowVersion);
}
