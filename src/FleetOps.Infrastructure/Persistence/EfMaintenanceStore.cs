using System.Linq.Expressions;
using FleetOps.Application.Exceptions;
using FleetOps.Application.Interfaces;
using FleetOps.Application.Models;
using FleetOps.Domain.Entities;
using FleetOps.Domain.Enums;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Infrastructure.Persistence;

public sealed class EfMaintenanceStore(FleetOpsDbContext db) : IMaintenanceStore
{
    private static readonly Expression<Func<Technician, TechnicianDto>> TechnicianProjection =
        t => new(t.Id, t.Name, t.Email, t.Specialization, t.Availability, t.RowVersion);

    private IQueryable<WorkOrderDto> OrderProjection(IQueryable<WorkOrder> orders) =>
        orders.Select(w => new WorkOrderDto(w.Id, w.AssetId, w.AssignedTechnicianId, w.Title, w.Description, w.Priority,
            w.Status, w.CreatedAt, w.DueDate, w.CompletedAt, w.RowVersion,
            db.MaintenanceRecords.Where(r => r.WorkOrderId == w.Id)
                .Select(r => new MaintenanceRecordDto(r.Id, r.Notes, r.CompletedAt)).SingleOrDefault()));

    public async Task<PagedResult<TechnicianDto>> ListTechniciansAsync(MaintenanceQuery query, CancellationToken token)
    {
        query.Validate(false);
        var rows = db.Technicians.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            rows = rows.Where(t => t.Name.Contains(search) || t.Email.Contains(search) || t.Specialization.Contains(search));
        }
        if (query.Availability.HasValue) rows = rows.Where(t => t.Availability == query.Availability);
        var count = await rows.CountAsync(token);
        var sorted = (query.SortBy, query.Descending) switch
        {
            ("email", false) => rows.OrderBy(t => t.Email), ("email", true) => rows.OrderByDescending(t => t.Email),
            ("specialization", false) => rows.OrderBy(t => t.Specialization), ("specialization", true) => rows.OrderByDescending(t => t.Specialization),
            ("availability", false) => rows.OrderBy(t => t.Availability), ("availability", true) => rows.OrderByDescending(t => t.Availability),
            ("name", true) => rows.OrderByDescending(t => t.Name), _ => rows.OrderBy(t => t.Name)
        };
        var items = await sorted.ThenBy(t => t.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(TechnicianProjection).ToListAsync(token);
        return new(items, query.Page, query.PageSize, count);
    }

    public async Task<PagedResult<WorkOrderDto>> ListWorkOrdersAsync(MaintenanceQuery query, CancellationToken token)
    {
        query.Validate(true);
        var rows = db.WorkOrders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            rows = rows.Where(w => w.Title.Contains(search) || w.Description.Contains(search));
        }
        if (query.Status.HasValue) rows = rows.Where(w => w.Status == query.Status);
        if (query.Priority.HasValue) rows = rows.Where(w => w.Priority == query.Priority);
        if (query.AssetId.HasValue) rows = rows.Where(w => w.AssetId == query.AssetId);
        if (query.TechnicianId.HasValue) rows = rows.Where(w => w.AssignedTechnicianId == query.TechnicianId);
        var count = await rows.CountAsync(token);
        var sorted = (query.SortBy, query.Descending) switch
        {
            ("createdAt", false) => rows.OrderBy(w => w.CreatedAt), ("createdAt", true) => rows.OrderByDescending(w => w.CreatedAt),
            ("dueDate", false) => rows.OrderBy(w => w.DueDate), ("dueDate", true) => rows.OrderByDescending(w => w.DueDate),
            ("priority", false) => rows.OrderBy(w => w.Priority), ("priority", true) => rows.OrderByDescending(w => w.Priority),
            ("status", false) => rows.OrderBy(w => w.Status), ("status", true) => rows.OrderByDescending(w => w.Status),
            ("title", true) => rows.OrderByDescending(w => w.Title), _ => rows.OrderBy(w => w.Title)
        };
        var page = sorted.ThenBy(w => w.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize);
        return new(await OrderProjection(page).ToListAsync(token), query.Page, query.PageSize, count);
    }

    public Task<Technician?> FindTechnicianAsync(Guid id, CancellationToken token) =>
        db.Technicians.SingleOrDefaultAsync(t => t.Id == id, token);
    public Task<WorkOrder?> FindWorkOrderAsync(Guid id, CancellationToken token) =>
        db.WorkOrders.SingleOrDefaultAsync(w => w.Id == id, token);
    public Task<WorkOrderDto?> ReadWorkOrderAsync(Guid id, CancellationToken token) =>
        OrderProjection(db.WorkOrders.AsNoTracking().Where(w => w.Id == id)).SingleOrDefaultAsync(token);
    public void AddTechnician(Technician technician) => db.Technicians.Add(technician);
    public void AddWorkOrder(WorkOrder order) => db.WorkOrders.Add(order);
    public void AddRecord(MaintenanceRecord record) => db.MaintenanceRecords.Add(record);

    public void ExpectVersion(object entity, byte[] expectedVersion)
    {
        if (expectedVersion is not { Length: 8 }) throw new ArgumentException("Version must contain 8 bytes.");
        var version = db.Entry(entity).Property("RowVersion");
        if (!((byte[])version.CurrentValue!).SequenceEqual(expectedVersion)) throw new ConcurrencyConflictException();
        version.OriginalValue = expectedVersion;
        // An identical PUT still writes a row, so SQL checks the version and returns a fresh token.
        var property = entity is Technician ? "Name" : "Title";
        db.Entry(entity).Property(property).IsModified = true;
    }
    public Task SaveAsync(CancellationToken token) => PersistenceWrites.SaveAsync(db, token);

    public async Task<DashboardSummary> SummaryAsync(CancellationToken token)
    {
        var assets = await db.Assets.AsNoTracking().GroupBy(a => a.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(token);
        var technicians = await db.Technicians.AsNoTracking().GroupBy(t => t.Availability).Select(g => new { Availability = g.Key, Count = g.Count() }).ToListAsync(token);
        var orders = await db.WorkOrders.AsNoTracking().GroupBy(w => w.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(token);
        var now = DateTimeOffset.UtcNow;
        var overdue = await db.WorkOrders.CountAsync(w => w.DueDate < now && w.Status != WorkOrderStatus.Completed && w.Status != WorkOrderStatus.Cancelled, token);
        int Assets(AssetStatus status) => assets.Where(a => a.Status == status).Sum(a => a.Count);
        int Orders(WorkOrderStatus status) => orders.Where(w => w.Status == status).Sum(w => w.Count);
        return new(assets.Sum(a => a.Count), Assets(AssetStatus.Operational), Assets(AssetStatus.Maintenance), Assets(AssetStatus.Retired),
            technicians.Sum(t => t.Count), technicians.Where(t => t.Availability == TechnicianAvailability.Available).Sum(t => t.Count),
            Orders(WorkOrderStatus.Open), Orders(WorkOrderStatus.Assigned), Orders(WorkOrderStatus.InProgress),
            Orders(WorkOrderStatus.Completed), Orders(WorkOrderStatus.Cancelled), overdue);
    }
}
