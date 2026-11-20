using FleetOps.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Testing;

internal static class SqlTestDefaults
{
    // Resolved from the SQL Server 2022 image verified for this test suite.
    public const string Image = "mcr.microsoft.com/mssql/server:2022-latest@sha256:4525e3efac9f3e241cb003bb2ff2a82818b502f37e12049d788b1a9fa7e13b7f";
}

internal sealed class SqlTestDatabase : IAsyncDisposable
{
    private const string Prefix = "FleetOps_Test_";
    public string ConnectionString { get; }
    public DbContextOptions<FleetOpsDbContext> Options { get; }

    private SqlTestDatabase(string containerConnection)
    {
        var connection = new SqlConnectionStringBuilder(containerConnection)
        { InitialCatalog = Prefix + Guid.NewGuid().ToString("N") };
        ConnectionString = connection.ConnectionString;
        Options = new DbContextOptionsBuilder<FleetOpsDbContext>().UseSqlServer(ConnectionString).Options;
    }

    public static async Task<SqlTestDatabase> CreateAsync(string containerConnection)
    {
        var database = new SqlTestDatabase(containerConnection);
        try
        {
            await using var context = database.CreateContext();
            await context.Database.MigrateAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public FleetOpsDbContext CreateContext() => new(Options);

    internal static void RequireOwnedDatabase(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(name[Prefix.Length..], "N", out _))
            throw new InvalidOperationException("Refusing to delete a database outside this test's generated namespace.");
    }

    public async ValueTask DisposeAsync()
    {
        RequireOwnedDatabase(new SqlConnectionStringBuilder(ConnectionString).InitialCatalog);
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }
}
