using FleetOps.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using FleetOps.Infrastructure.Identity;
using FleetOps.Application.Security;

namespace FleetOps.Infrastructure.Persistence;

public sealed class FleetOpsDbContext(DbContextOptions<FleetOpsDbContext> options, ICurrentActor? actor = null)
    : IdentityDbContext<FleetOpsUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Technician> Technicians => Set<Technician>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<MaintenanceRecord> MaintenanceRecords => Set<MaintenanceRecord>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.Entity<FleetOpsUser>().HasOne<Technician>().WithMany()
            .HasForeignKey(u => u.TechnicianId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<FleetOpsUser>().HasIndex(u => u.TechnicianId).IsUnique().HasFilter("[TechnicianId] IS NOT NULL");
        model.Entity<FleetOpsUser>().HasIndex(u => u.NormalizedEmail).IsUnique().HasFilter("[NormalizedEmail] IS NOT NULL");
        model.UseCollation("Latin1_General_100_CI_AS_SC");
        var asset = model.Entity<Asset>();
        asset.ToTable("Assets", table =>
        {
            table.HasCheckConstraint("CK_Assets_Status", "[Status] IN (0,1,2)");
            table.HasCheckConstraint("CK_Assets_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
            table.HasCheckConstraint("CK_Assets_SerialNumber", "LEN(LTRIM(RTRIM([SerialNumber]))) > 0");
            table.HasCheckConstraint("CK_Assets_Timestamps", "[UpdatedAt] >= [CreatedAt]");
        });
        asset.HasKey(x => x.Id);
        asset.Property(x => x.Id).ValueGeneratedNever();
        asset.Property(x => x.Name).HasMaxLength(Asset.NameMaxLength).IsRequired();
        asset.Property(x => x.SerialNumber).HasMaxLength(Asset.SerialNumberMaxLength).IsRequired();
        asset.Property(x => x.RowVersion).IsRowVersion();
        asset.HasIndex(x => x.SerialNumber).IsUnique();
        asset.HasIndex(x => new { x.Name, x.Id });
        asset.HasIndex(x => new { x.Status, x.Name, x.Id });
        asset.HasIndex(x => new { x.CreatedAt, x.Id });

        var technician = model.Entity<Technician>();
        technician.ToTable("Technicians", table =>
        {
            table.HasCheckConstraint("CK_Technicians_Availability", "[Availability] IN (0,1)");
            table.HasCheckConstraint("CK_Technicians_Text", "LEN(LTRIM(RTRIM([Name]))) > 0 AND LEN(LTRIM(RTRIM([Email]))) > 0 AND LEN(LTRIM(RTRIM([Specialization]))) > 0");
        });
        technician.HasKey(x => x.Id);
        technician.Property(x => x.Id).ValueGeneratedNever();
        technician.Property(x => x.Name).HasMaxLength(200).IsRequired();
        technician.Property(x => x.Email).HasMaxLength(254).IsRequired();
        technician.Property(x => x.Specialization).HasMaxLength(200).IsRequired();
        technician.Property(x => x.RowVersion).IsRowVersion();
        technician.HasIndex(x => x.Email).IsUnique();
        technician.HasIndex(x => new { x.Availability, x.Name });

        var workOrder = model.Entity<WorkOrder>();
        workOrder.ToTable("WorkOrders", table =>
        {
            table.HasCheckConstraint("CK_WorkOrders_Status", "[Status] IN (0,1,2,3,4)");
            table.HasCheckConstraint("CK_WorkOrders_Priority", "[Priority] IN (0,1,2,3)");
            table.HasCheckConstraint("CK_WorkOrders_Text", "LEN(LTRIM(RTRIM([Title]))) > 0 AND LEN(LTRIM(RTRIM([Description]))) > 0");
            table.HasCheckConstraint("CK_WorkOrders_DueDate", "[DueDate] IS NULL OR [DueDate] >= [CreatedAt]");
            table.HasCheckConstraint("CK_WorkOrders_Completion", "([Status] = 3 AND [CompletedAt] IS NOT NULL AND [CompletedAt] >= [CreatedAt]) OR ([Status] <> 3 AND [CompletedAt] IS NULL)");
            table.HasCheckConstraint("CK_WorkOrders_Assignment", "([Status] = 0 AND [AssignedTechnicianId] IS NULL) OR ([Status] IN (1,2,3) AND [AssignedTechnicianId] IS NOT NULL) OR [Status] = 4");
        });
        workOrder.HasKey(x => x.Id);
        workOrder.Property(x => x.Id).ValueGeneratedNever();
        workOrder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        workOrder.Property(x => x.Description).HasMaxLength(4000).IsRequired();
        workOrder.Property(x => x.RowVersion).IsRowVersion();
        workOrder.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
        workOrder.HasOne<Technician>().WithMany().HasForeignKey(x => x.AssignedTechnicianId).OnDelete(DeleteBehavior.Restrict);
        workOrder.HasIndex(x => new { x.AssetId, x.Status });
        workOrder.HasIndex(x => new { x.AssignedTechnicianId, x.Status });
        workOrder.HasIndex(x => new { x.Status, x.DueDate });
        workOrder.HasIndex(x => new { x.Priority, x.CreatedAt });

        var record = model.Entity<MaintenanceRecord>();
        record.ToTable("MaintenanceRecords", table =>
            table.HasCheckConstraint("CK_MaintenanceRecords_Notes", "LEN(LTRIM(RTRIM([Notes]))) > 0"));
        record.HasKey(x => x.Id);
        record.Property(x => x.Id).ValueGeneratedNever();
        record.Property(x => x.Notes).HasMaxLength(4000).IsRequired();
        record.HasOne<WorkOrder>().WithOne().HasForeignKey<MaintenanceRecord>(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
        record.HasIndex(x => x.WorkOrderId).IsUnique();

        var audit = model.Entity<AuditLog>();
        audit.ToTable("AuditLogs", table =>
        {
            table.HasCheckConstraint("CK_AuditLogs_Action", "[Action] IN ('Created','Updated','Deleted')");
            table.HasCheckConstraint("CK_AuditLogs_Text", "LEN(LTRIM(RTRIM([ActorId]))) > 0 AND LEN(LTRIM(RTRIM([EntityType]))) > 0");
        });
        audit.HasKey(x => x.Id);
        audit.Property(x => x.Id).ValueGeneratedNever();
        audit.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
        audit.Property(x => x.Action).HasMaxLength(20).IsRequired();
        audit.Property(x => x.ActorId).HasMaxLength(200).IsRequired();
        audit.HasIndex(x => new { x.EntityType, x.EntityId, x.Timestamp });
        audit.HasIndex(x => x.Timestamp);
    }

    private AuditLog[] AddAuditEntries()
    {
        ChangeTracker.DetectChanges();
        var entries = ChangeTracker.Entries()
            .Where(e => e.Entity is Asset or Technician or WorkOrder or MaintenanceRecord
                && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToArray();
        var logs = new List<AuditLog>();
        foreach (var entry in entries)
        {
            var action = entry.State switch
            {
                EntityState.Added => "Created",
                EntityState.Modified => "Updated",
                _ => "Deleted"
            };
            logs.Add(new AuditLog((Guid)entry.Property("Id").CurrentValue!, entry.Metadata.ClrType.Name, action, actor?.UserId ?? "system"));
        }
        AuditLogs.AddRange(logs);
        return logs.ToArray();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var logs = AddAuditEntries();
        try { return base.SaveChanges(acceptAllChangesOnSuccess); }
        catch
        {
            foreach (var log in logs) Entry(log).State = EntityState.Detached;
            throw;
        }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var logs = AddAuditEntries();
        try { return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); }
        catch
        {
            foreach (var log in logs) Entry(log).State = EntityState.Detached;
            throw;
        }
    }
}
