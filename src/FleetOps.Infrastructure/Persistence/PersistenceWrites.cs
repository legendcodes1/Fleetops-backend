using FleetOps.Application.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Infrastructure.Persistence;

internal static class PersistenceWrites
{
    public static async Task SaveAsync(FleetOpsDbContext db, CancellationToken token)
    {
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException(); }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
        { throw new PersistenceConflictException("A resource with this unique value already exists."); }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 547 })
        { throw new PersistenceConflictException("The change conflicts with a relationship or database constraint."); }
    }
}
