# Windows SQL Server connection diagnosis

## Final design-time comparison

The residual EF timeout was traced to the actual environment received by the factory, rather than inferred from the beginning of a connection string. The user's failing PowerShell trace reported:

```text
Factory settings: DataSource=localhost,14333; InitialCatalog=FleetOps; Authentication=SQL login; Encrypt=True; TrustServerCertificate=True
```

The successful Windows factory/direct comparison reported DataSource=tcp:127.0.0.1,14333. Both CLI runs loaded the same factory module (2cf27a69-bab4-4c4e-9756-dfe45baf3014). A fresh API startup-project build was also verified. Stale factory code and application startup choosing a different DbContext configuration were therefore not the observed difference.

In the successful comparison, the exact environment string reached DbContextOptions unchanged. EF Core 10 added only an Application Name to the underlying SqlClient connection string:

```text
Direct: Core Microsoft SqlClient Data Provider
EF:     EFCore/10.0.15 (Microsoft Windows 10.0.26200 X64)
```

That documented provider behavior was not causal: the factory-created DbContext.OpenConnectionAsync succeeded, and its migration-history SELECT reported InitialPersistence as Applied. There is no evidence to justify changing pooling, retries, encryption, timeouts, or the EF provider.

The smallest fix is to reload the corrected local environment into the same PowerShell process that invokes EF:

Use the single-command helper to make the refresh and status query inseparable:

```powershell
./scripts/Get-MigrationStatus.ps1
```

It reads the repository .env, invokes EF using explicit Infrastructure/API/context arguments, prints only allowlisted factory fields, and rejects a null applied status even if EF exits 0. It does not run initialization or apply migrations. Running this script in PowerShell updates that process's environment as well.

Equivalent manual steps:

```powershell
. ./scripts/Use-LocalDatabase.ps1
dotnet ef migrations list --project src/FleetOps.Infrastructure --startup-project src/FleetOps.Api --context FleetOpsDbContext --json -- --connection-diagnostics
```

Editing .env does not update an already-running PowerShell process. Also, starting a string with an IPv4 Server setting is not proof of its effective DataSource: a later duplicate Server/Data Source alias can override it. SqlConnectionStringBuilder's parsed value is authoritative. The observed trace proves the effective endpoint mismatch; it does not by itself distinguish a stale shell variable from a duplicate endpoint key in the user's full value.

The verified Windows command returned `"applied": true`. The user's failing session returned `"applied": null`, which must not be treated as a successful status check. The helper was additionally tested with an intentionally stale process-only localhost endpoint: it refreshed from the unchanged local file and returned InitialPersistence: Applied. A no-database simulated EF result with applied=null and exit code 0 was correctly rejected. Confirmation using the helper in the user's own session was requested separately.

The optional --connection-diagnostics factory argument reports only allowlisted settings and assembly identity. It never reports the full connection string, credentials, or connection fingerprints. Normal factory behavior is unchanged.

## Observed cause

Compose publishes `127.0.0.1:14333:1433`, an IPv4-only host binding. Windows resolves `localhost` to both `::1` and `127.0.0.1`. In the observed Windows SqlClient/SNI path, `localhost,14333` times out instead of reaching the working IPv4 listener. Explicit IPv4 succeeds with the exact same SQL credentials and encryption settings.

The failure is at connection establishment, before a successful SQL pre-login/TLS handshake or login. It is not an authentication failure, missing FleetOps database, certificate validation failure, or EF migration issue. A successful socket test against 127.0.0.1 and sqlcmd inside the container do not test the same localhost connection path used by Windows SqlClient.

Evidence from Microsoft.Data.SqlClient 6.1.7 on Windows, using five-second SQL timeouts, no retries, and no pooling:

| Probe | Result |
| --- | --- |
| Raw TCP to ::1:14333 | Failed |
| Raw TCP to 127.0.0.1:14333 | Passed |
| SQL localhost,14333 to master | Timed out during TCP/SNI connection establishment, SQL/native error 258 |
| SQL localhost,14333 to FleetOps | Same failure |
| SQL tcp:localhost,14333 | Same failure (TCP prefix alone is insufficient) |
| SQL 127.0.0.1,14333 without TCP prefix | Passed |
| SQL tcp:127.0.0.1,14333 to master | Passed, 113 ms in the post-fix run |
| SQL tcp:127.0.0.1,14333 to FleetOps | Passed, 11 ms in the post-fix run |

The endpoint comparison identifies an IPv4/localhost path mismatch. It does not claim to be a packet capture of SqlClient's internal address selection.

## Smallest fix

Change only the connection-string endpoint:

```text
Server=tcp:127.0.0.1,14333
```

The ignored local .env was updated without changing credentials. The tracked .env.example now uses the same endpoint. Encryption and TrustServerCertificate settings were preserved. Compose did not need changes, container recreation, port rebinding, or volume changes.

The design-time factory now fails fast when ConnectionStrings__FleetOps is missing, instead of silently choosing a localhost/Integrated Security fallback that is unsuitable for this SQL-authenticated container. Application startup already requires the configured FleetOps connection string; its behavior is unchanged.

Compose loads .env for its own interpolation, but that does not export variables into a Windows shell. Neither WebApplication.CreateBuilder nor this design-time factory automatically loads .env. Load local settings in the same shell as EF:

```powershell
. ./scripts/Use-LocalDatabase.ps1
dotnet ef migrations list --project src/FleetOps.Infrastructure --no-build
```

The factory must be rebuilt after changing its source. Do not use the application's --initialize-database option for connection diagnosis: that option applies migrations and seeds data.

## Minimal Windows SqlClient test

The tools/FleetOps.SqlDiagnostics project supports direct SqlClient probes and an additional --factory comparison mode referencing Infrastructure. Neither mode executes application startup. Its patched Pkcs dependency matches the existing EF provider dependency graph.

```powershell
. ./scripts/Use-LocalDatabase.ps1
dotnet build tools/FleetOps.SqlDiagnostics -m:1 -p:UseSharedCompilation=false
dotnet tools/FleetOps.SqlDiagnostics/bin/Debug/net10.0/FleetOps.SqlDiagnostics.dll --timeout 5
```

To reproduce the localhost comparison without modifying configuration:

```powershell
dotnet tools/FleetOps.SqlDiagnostics/bin/Debug/net10.0/FleetOps.SqlDiagnostics.dll --timeout 5 --compare-localhost
```

To compare the exact, unmodified environment value with the real design-time factory:

```powershell
dotnet tools/FleetOps.SqlDiagnostics/bin/Debug/net10.0/FleetOps.SqlDiagnostics.dll --factory
```

This mode reports parsed non-sensitive settings, compares environment/options/effective connection settings without printing credentials, opens a direct SqlConnection, calls OpenConnectionAsync on a context returned by FleetOpsDbContextFactory, then reads migration history. Unlike the original direct probe, it does not rewrite endpoint, pooling, retry, timeout, or application-name settings. Each operation has a 30-second diagnostic cancellation limit. Exit code 0 requires both connection opens and the migration-history query to succeed.

The test checks per-address TCP connectivity, opens master to isolate handshake/authentication, then opens the configured database to check selection. It issues only SELECT DB_NAME(). No credentials, connection strings, or raw exception messages are printed. No database objects or data are changed.

SqlClient OpenAsync does not expose public callbacks for each login phase. The tool therefore labels stages using error numbers and sanitized message classification: pre-login/TLS text identifies handshake failures, login error codes identify authentication failures, and database-access error codes identify database-selection failures. Generic failures remain explicitly undetermined rather than being called an authentication failure. Master success followed by target-database failure also helps isolate selection problems.

Exit codes: 0 means at least one configured/IPv4 endpoint selected the expected database, 1 means no SQL endpoint passed, and 2 means invalid/missing configuration. With --compare-localhost, read each endpoint's result; localhost may intentionally fail while IPv4 succeeds.

## Verification and preservation

After rebuilding the factory and loading the corrected local settings, Windows EF printed:

```text
20261119155841_InitialPersistence
```

There was no database-access warning or inability-to-determine-pending-status message. This matters because EF can list locally discovered migrations and return exit code 0 even when its database connection fails; the earlier failing probes did exactly that.

No migrations were applied. No database contents, Compose settings, containers, or volumes were changed. Existing Phase 2 edits and the user's API package-reference change were preserved.

References: [Microsoft connection-timeout troubleshooting](https://learn.microsoft.com/en-us/troubleshoot/sql/database-engine/connect/timeout-expired-error) and [connection-failure categories](https://learn.microsoft.com/en-US/troubleshoot/sql/connect/resolving-connectivity-errors).
