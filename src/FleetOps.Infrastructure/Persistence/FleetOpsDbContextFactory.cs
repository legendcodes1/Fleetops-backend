using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Data.SqlClient;

namespace FleetOps.Infrastructure.Persistence;

public sealed class FleetOpsDbContextFactory : IDesignTimeDbContextFactory<FleetOpsDbContext>
{
    public FleetOpsDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__FleetOps");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException(
                "Set ConnectionStrings__FleetOps before running EF tools. In PowerShell, run: . ./scripts/Use-LocalDatabase.ps1");
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>().UseSqlServer(connection);
        if (args.Contains("--connection-diagnostics"))
        {
            var safe = new SqlConnectionStringBuilder(connection);
            Console.Error.WriteLine($"Factory: {GetType().Assembly.Location}; module={GetType().Assembly.ManifestModule.ModuleVersionId}");
            Console.Error.WriteLine($"Factory settings: DataSource={safe.DataSource}; InitialCatalog={safe.InitialCatalog}; Authentication={(safe.IntegratedSecurity ? "Integrated" : safe.Authentication == SqlAuthenticationMethod.NotSpecified ? "SQL login" : safe.Authentication.ToString())}; Encrypt={safe.Encrypt}; TrustServerCertificate={safe.TrustServerCertificate}; Pooling={safe.Pooling}; ConnectTimeout={safe.ConnectTimeout}; ConnectRetryCount={safe.ConnectRetryCount}");
        }
        return new FleetOpsDbContext(options.Options);
    }
}
