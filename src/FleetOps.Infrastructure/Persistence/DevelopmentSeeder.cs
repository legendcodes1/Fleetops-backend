using FleetOps.Domain.Entities;
using FleetOps.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Infrastructure.Persistence;

// Explicit development command, not automatic application startup migration/seeding.
public static class DevelopmentSeeder
{
    public static async Task SeedAsync(FleetOpsDbContext db, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        if (!await db.Assets.AnyAsync(a => a.SerialNumber == "DEV-HP-001", cancellationToken))
            db.Assets.Add(new Asset("Hydraulic Pump", "DEV-HP-001"));
        if (!await db.Technicians.AnyAsync(t => t.Email == "technician@example.test", cancellationToken))
            db.Technicians.Add(new Technician("Alex Morgan", "technician@example.test", "Hydraulics"));
        await db.SaveChangesAsync(cancellationToken);
        var asset = await db.Assets.SingleAsync(a => a.SerialNumber == "DEV-HP-001", cancellationToken);
        var technician = await db.Technicians.SingleAsync(t => t.Email == "technician@example.test", cancellationToken);
        if (!await db.WorkOrders.AnyAsync(w => w.AssetId == asset.Id, cancellationToken))
        {
            var workOrder = new WorkOrder(asset.Id, "Inspect hydraulic seals", "Check for leaks and document findings.", WorkOrderPriority.Medium);
            workOrder.Assign(technician);
            db.WorkOrders.Add(workOrder);
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
}
