using System.Diagnostics;
using FleetOps.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

internal static class FactoryComparison
{
    public static async Task<int> RunAsync(string raw)
    {
        // No rewritten endpoint, timeout, retry, pooling, or application-name settings.
        // Both connections use precisely the same environment value as EF tooling.
        var environment = new SqlConnectionStringBuilder(raw);
        Report("Environment", environment);
        await using var context = new FleetOpsDbContextFactory().CreateDbContext(["--connection-diagnostics"]);
        var actual = context.Database.GetConnectionString();
        var configured = new SqlConnectionStringBuilder(actual);
        Report("Factory context", configured);
        Console.WriteLine($"Exact environment/context string match: {string.Equals(raw, actual, StringComparison.Ordinal)}");
        Console.WriteLine($"Equivalent parsed settings: {environment.EquivalentTo(configured)}");
        var rawOptions = context.GetService<IDbContextOptions>().Extensions.OfType<RelationalOptionsExtension>().Single().ConnectionString;
        Console.WriteLine($"Exact environment/options string match: {string.Equals(raw, rawOptions, StringComparison.Ordinal)}");
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Data Source", "Initial Catalog", "Integrated Security", "Authentication", "Encrypt", "Trust Server Certificate",
            "Pooling", "Connect Timeout", "Connect Retry Count", "Application Name", "Application Intent", "Packet Size",
            "Enlist", "Persist Security Info", "Multiple Active Result Sets", "Multi Subnet Failover", "IP Address Preference"
        };
        var differences = 0;
        foreach (var key in environment.Keys.Cast<string>())
        {
            if (Equals(environment[key], configured[key])) continue;
            differences++;
            Console.WriteLine(allowed.Contains(key)
                ? $"Effective setting difference: {key}: environment={environment[key]}; context={configured[key]}"
                : $"Effective setting difference: {key} (values suppressed)");
        }
        Console.WriteLine($"Effective property differences: {differences}");
        Console.WriteLine($"Provider: {context.Database.ProviderName}");
        Console.WriteLine($"DbConnection type: {context.Database.GetDbConnection().GetType().FullName}");
        Console.WriteLine($"Command timeout: {context.Database.GetCommandTimeout()?.ToString() ?? "provider default"}");
        Console.WriteLine($"Execution strategy: {context.Database.CreateExecutionStrategy().GetType().Name}");
        Console.WriteLine($"Factory assembly: {typeof(FleetOpsDbContextFactory).Assembly.Location}");
        Console.WriteLine($"Factory module ID: {typeof(FleetOpsDbContextFactory).Assembly.ManifestModule.ModuleVersionId}");
        Console.WriteLine($"Options extensions: {string.Join(", ", context.GetService<IDbContextOptions>().Extensions.Select(e => e.GetType().Name))}");

        var directPassed = await Probe("Direct SqlClient, exact environment", async token =>
        {
            await using var direct = new SqlConnection(raw);
            await direct.OpenAsync(token);
            Console.WriteLine($"  Expected database selected: {string.Equals(direct.Database, environment.InitialCatalog, StringComparison.OrdinalIgnoreCase)}");
        });
        var contextPassed = await Probe("Actual factory DbContext.OpenConnectionAsync", async token =>
        {
            try
            {
                await context.Database.OpenConnectionAsync(token);
                Console.WriteLine($"  Expected database selected: {string.Equals(context.Database.GetDbConnection().Database, environment.InitialCatalog, StringComparison.OrdinalIgnoreCase)}");
            }
            finally { await context.Database.CloseConnectionAsync(); }
        });
        var historyPassed = false;
        if (contextPassed)
        {
            historyPassed = await Probe("Factory context migration-history SELECT", async token =>
            {
                var applied = (await context.Database.GetAppliedMigrationsAsync(token)).ToHashSet();
                foreach (var migration in context.Database.GetMigrations())
                    Console.WriteLine($"  {migration}: {(applied.Contains(migration) ? "Applied" : "Pending")}");
            });
        }
        Console.WriteLine(directPassed && contextPassed
            ? "No connection failure reproduced; review effective setting differences above."
            : "Compare the exact-environment results with the earlier diagnostic, which changed timeout/retry/pooling settings.");
        return directPassed && contextPassed && historyPassed ? 0 : 1;
    }

    private static void Report(string source, SqlConnectionStringBuilder settings)
    {
        Console.WriteLine($"{source}: DataSource={settings.DataSource}; InitialCatalog={settings.InitialCatalog}");
        Console.WriteLine($"  Authentication={(settings.IntegratedSecurity ? "Integrated" : settings.Authentication == SqlAuthenticationMethod.NotSpecified ? "SQL login" : settings.Authentication.ToString())}; Encrypt={settings.Encrypt}; TrustServerCertificate={settings.TrustServerCertificate}");
        Console.WriteLine($"  Pooling={settings.Pooling}; ConnectTimeout={settings.ConnectTimeout}; ConnectRetryCount={settings.ConnectRetryCount}; MultipleActiveResultSets={settings.MultipleActiveResultSets}; MultiSubnetFailover={settings.MultiSubnetFailover}");
    }

    private static async Task<bool> Probe(string name, Func<CancellationToken, Task> action)
    {
        Console.WriteLine($"{name} (30-second diagnostic cancellation limit; connection settings unchanged)");
        var clock = Stopwatch.StartNew();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await action(cancellation.Token);
            Console.WriteLine($"  PASS in {clock.ElapsedMilliseconds} ms");
            return true;
        }
        catch (SqlException exception)
        {
            Console.WriteLine($"  FAIL in {clock.ElapsedMilliseconds} ms; SQL number/state/class: {string.Join(", ", exception.Errors.Cast<SqlError>().Select(e => $"{e.Number}/{e.State}/{e.Class}"))}");
            ReportFailureChain(exception);
            return false;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"  FAIL in {clock.ElapsedMilliseconds} ms; {exception.GetType().Name}; details suppressed");
            ReportFailureChain(exception);
            return false;
        }
    }

    private static void ReportFailureChain(Exception exception)
    {
        for (Exception? error = exception; error is not null; error = error.InnerException)
        {
            Console.WriteLine($"    {error.GetType().Name}; HResult=0x{error.HResult:X8}");
            if (error is SqlException sql)
                Console.WriteLine($"    SQL codes: {string.Join(", ", sql.Errors.Cast<SqlError>().Select(e => e.Number))}");
            if (error is System.ComponentModel.Win32Exception native)
                Console.WriteLine($"    Native code: {native.NativeErrorCode}");
            if (error.Message.Contains("pre-login", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine("    Category: pre-login handshake");
            if (error.Message.Contains("network-related", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine("    Category: network/SNI connection establishment");
            if (error.Message.Contains("blocked", StringComparison.OrdinalIgnoreCase)
                || error.Message.Contains("denied", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine("    Category: access/policy restriction");
        }
    }
}
