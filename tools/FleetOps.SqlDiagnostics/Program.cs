using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

// Read-only: socket checks, SQL login, and SELECT DB_NAME(). No schema/data changes.
// Never print the connection string, user name, password, or raw exception text.
var raw = Environment.GetEnvironmentVariable("ConnectionStrings__FleetOps");
if (string.IsNullOrWhiteSpace(raw))
{
    Console.WriteLine("Configuration: ConnectionStrings__FleetOps is missing. Load local settings first.");
    return 2;
}

SqlConnectionStringBuilder original;
try { original = new SqlConnectionStringBuilder(raw); }
catch (ArgumentException)
{
    Console.WriteLine("Configuration: invalid SQL connection-string syntax (details suppressed).");
    return 2;
}
if (args.Contains("--factory"))
    return await FactoryComparison.RunAsync(raw);
var timeout = 5;
var timeoutArgument = Array.IndexOf(args, "--timeout");
if (timeoutArgument >= 0 && (timeoutArgument + 1 >= args.Length ||
    !int.TryParse(args[timeoutArgument + 1], out timeout) || timeout is < 1 or > 30))
{
    Console.WriteLine("--timeout must be 1 to 30 seconds.");
    return 2;
}
Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
var driverVersion = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(SqlConnection).Assembly)?.InformationalVersion.Split('+')[0];
Console.WriteLine($"Driver: Microsoft.Data.SqlClient {driverVersion}");
Console.WriteLine($"Authentication: {(original.IntegratedSecurity ? "Integrated" : original.Authentication == SqlAuthenticationMethod.NotSpecified ? "SQL login" : "configured external authentication")}");
Console.WriteLine($"Encryption: {original.Encrypt}; certificate trusted: {original.TrustServerCertificate}");
Console.WriteLine($"Each SQL probe: timeout {timeout}s; retries disabled; pooling disabled.");
Console.WriteLine("SQL stages are inferred from SqlClient errors; OpenAsync does not expose separate public handshake/authentication calls.");

var endpoints = (args.Contains("--compare-localhost")
    ? new[] { original.DataSource, "localhost,14333", "tcp:127.0.0.1,14333" }
    : new[] { original.DataSource, "tcp:127.0.0.1,14333" }).Distinct().ToArray();
var anySuccess = false;
foreach (var endpoint in endpoints)
{
    Console.WriteLine($"\nEndpoint: {endpoint}");
    var match = Regex.Match(endpoint, @"^(?:tcp:)?(?<host>localhost|127\.0\.0\.1|\[::1\]),(?<port>\d+)$", RegexOptions.IgnoreCase);
    if (!match.Success)
    {
        Console.WriteLine("TCP probe: skipped; this minimal diagnostic supports explicit loopback host,port endpoints only.");
        continue;
    }
    var host = match.Groups["host"].Value.Trim('[', ']');
    var port = int.Parse(match.Groups["port"].Value);
    foreach (var address in await Dns.GetHostAddressesAsync(host))
    {
        var clock = Stopwatch.StartNew();
        try
        {
            using var socket = new TcpClient(address.AddressFamily);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
            await socket.ConnectAsync(address, port, cancellation.Token);
            Console.WriteLine($"TCP {address}:{port}: PASS ({clock.ElapsedMilliseconds} ms)");
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException)
        {
            var reason = exception is SocketException network ? $"{network.SocketErrorCode} ({network.NativeErrorCode})" : "timeout";
            Console.WriteLine($"TCP {address}:{port}: FAIL ({clock.ElapsedMilliseconds} ms; {reason})");
        }
    }

    var masterSucceeded = await SqlProbe(endpoint, "master", "master (handshake + authentication)", timeout);
    if (!masterSucceeded)
        Console.WriteLine("Database selection: not independently verified because master login failed.");
    var targetSucceeded = await SqlProbe(endpoint, original.InitialCatalog, "configured database", timeout);
    anySuccess |= targetSucceeded;
    if (masterSucceeded && targetSucceeded)
        Console.WriteLine("Result: TCP, SQL pre-login/TLS, authentication, and database selection all PASS.");
}
return anySuccess ? 0 : 1;

async Task<bool> SqlProbe(string endpoint, string database, string label, int seconds)
{
    var settings = new SqlConnectionStringBuilder(original.ConnectionString)
    {
        DataSource = endpoint,
        InitialCatalog = database,
        ConnectTimeout = seconds,
        ConnectRetryCount = 0,
        Pooling = false,
        ApplicationName = "FleetOps.ReadOnlySqlDiagnostic"
    };
    var clock = Stopwatch.StartNew();
    try
    {
        using var connection = new SqlConnection(settings.ConnectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DB_NAME()";
        command.CommandTimeout = seconds;
        var selected = (string?)await command.ExecuteScalarAsync();
        Console.WriteLine($"SQL {label}: PASS ({clock.ElapsedMilliseconds} ms; expected database selected: {string.Equals(selected, database, StringComparison.OrdinalIgnoreCase)})");
        return string.Equals(selected, database, StringComparison.OrdinalIgnoreCase);
    }
    catch (SqlException exception)
    {
        var message = exception.Message; // Used for classification only; never emitted.
        var numbers = exception.Errors.Cast<SqlError>().Select(e => e.Number).ToArray();
        var stage = numbers.Any(n => n is 4060 or 911 or 916) ? "database selection"
            : numbers.Any(n => n is 18456 or 18452 or 18486 or 18487 or 18488) ? "authentication"
            : message.Contains("pre-login", StringComparison.OrdinalIgnoreCase) ? "SQL pre-login/TLS handshake"
            : message.Contains("SSL Provider", StringComparison.OrdinalIgnoreCase) || message.Contains("certificate", StringComparison.OrdinalIgnoreCase) ? "TLS handshake/certificate validation"
            : message.Contains("post-login", StringComparison.OrdinalIgnoreCase) ? "post-login/session setup"
            : message.Contains("TCP Provider", StringComparison.OrdinalIgnoreCase) || message.Contains("network-related", StringComparison.OrdinalIgnoreCase) ? "TCP/SNI connection establishment"
            : "undetermined SQL connection stage";
        Console.WriteLine($"SQL {label}: FAIL during {stage} ({clock.ElapsedMilliseconds} ms)");
        Console.WriteLine($"  SqlError number/state/class: {string.Join(", ", exception.Errors.Cast<SqlError>().Select(e => $"{e.Number}/{e.State}/{e.Class}"))}");
        var timing = Regex.Match(message, @"\[Pre-Login\]\s*initialization=\d+;\s*handshake=\d+");
        if (timing.Success) Console.WriteLine($"  {timing.Value}");
        if (exception.InnerException is System.ComponentModel.Win32Exception native)
            Console.WriteLine($"  Native error code: {native.NativeErrorCode}");
        return false;
    }
    catch (Exception exception)
    {
        Console.WriteLine($"SQL {label}: diagnostic/runtime failure ({exception.GetType().Name}); raw error suppressed.");
        return false;
    }
}
