using System.Collections.Concurrent;
using FleetOps.Domain.Entities;
using FleetOps.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FleetOps.Api.Tests;

// Per-test synchronization: both requests must finish their reads/domain checks before either writes.
// It never changes a result, concurrency token, or production service.
internal sealed class ConcurrentWriteGate : SaveChangesInterceptor
{
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int arrivals;
    public Guid? WorkOrderId { get; set; }
    public WorkOrderStatus Status { get; set; } = WorkOrderStatus.Assigned;
    public int Arrivals => Volatile.Read(ref arrivals);
    public ConcurrentQueue<string> OriginalVersions { get; } = new();

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var entry = eventData.Context!.ChangeTracker.Entries<WorkOrder>()
            .SingleOrDefault(e => e.State == EntityState.Modified && e.Entity.Id == WorkOrderId && e.Entity.Status == Status);
        if (entry is null) return result;
        OriginalVersions.Enqueue(Convert.ToBase64String(entry.Property(w => w.RowVersion).OriginalValue));
        if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        return result;
    }
}
