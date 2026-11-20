# Phase 5 — automated testing

FleetOps uses xUnit for domain/application tests and ASP.NET Core WebApplicationFactory with real SQL Server Testcontainers for HTTP integration tests. Persistence tests also use SQL Server. The suite exercises real EF mappings, constraints, transactions, rowversion concurrency, Identity login, protected bearer tickets, and authorization. It does not substitute an in-memory database or mock DbContext.

## Run and inspect results

Docker Desktop must be running with Linux containers. From the repository root:

```powershell
./scripts/Test-InDocker.ps1 -Coverage
# Reuse the verification image if source has not changed:
./scripts/Test-InDocker.ps1 -SkipBuild -Coverage
```

The runner builds `fleetops-verify:phase5`, runs the solution, and retains TRX and per-project Cobertura reports under a unique `artifacts/test-results/<run-id>/` directory. Failed runs retain their reports too. These artifacts are Git-ignored. Generated migrations and test assemblies are excluded from coverage. Reports from different test projects overlap in the production assemblies they measure; their percentages must not be averaged into an overall percentage.

Where Windows execution policy permits generated assemblies, the equivalent native command is:

```powershell
dotnet test FleetOps.slnx -m:1 --settings tests.runsettings --collect:"XPlat Code Coverage"
```

The Docker runner is the verified fallback for this workstation's Application Control restrictions. It runs Linux tests; it does not prove Windows test-host execution works. SQL Server tests require Docker and enough resources to start the SQL Server 2022 image. The runner mounts the Docker socket so Testcontainers can manage its containers; use it only with trusted repository code.

## Behavioral coverage

Verified November 20, 2026: Windows and Linux solution builds passed with zero warnings/errors. The final Docker run passed **140 tests, zero failures and zero skips**, up from the Phase 4 baseline of 67:

| Project | Passed cases |
| --- | ---: |
| API | 50 |
| Application | 15 |
| Domain | 54 |
| Infrastructure (SQL persistence and cleanup guard) | 21 |

Final TRX and coverage reports are in `artifacts/test-results/ec49f77c0c7445499dda8a19c0780b19/`. The ETag regression was observed failing before the production fix and passed afterward. An earlier full run exposed an incorrectly constructed empty-body test (missing JSON content type); correcting that test required no application change. Both write races passed in both full runs. PowerShell parsing and `git diff --check` also passed. The existing development API and healthy SQL Server remained running.

| Layer | Important checks |
| --- | --- |
| Domain | All five work-order states against assign/start/complete/cancel; assignment eligibility; terminal-state restrictions; failed operations preserve state; technician and asset field boundaries; due-date instants; completion-note limits. |
| Application | Paging, filtering, sort validation; authenticated service authorization and technician ownership checked directly, without controller policy execution. |
| HTTP | Asset create/list/get/update/delete, Location and ETag headers, server-owned fields, unique serial conflicts, stale/missing/malformed preconditions, malformed bodies, 400/401/403/404/409/412/415/428 responses, work-order lifecycle, real account and session security. |
| SQL Server | Migrations into fresh test databases, durable data/audits, FK/unique/check constraints, stale writes, cancellation, transactional rollback of completion/order/record/audits after a forced SQL failure. |
| Concurrency | Authorized competing assignments and completions synchronize immediately before saving; both requests must have read the same original rowversion. Exactly one write wins and loser side effects do not persist. |

The original plan mentions two technicians claiming a work order. FleetOps currently permits only supervisors/administrators to assign; there is no self-claim endpoint. A test sends competing assignment requests using two real technician accounts and requires both to return 403 with the order unchanged. Separate authorized assignment races prove the one-winner behavior without changing that permission model.

The test-only `ConcurrentWriteGate` is an EF SaveChanges interceptor attached only to the test host. It waits for two writes targeting the same order and records their original rowversions. This proves overlap rather than relying on Task.WhenAll or timing sleeps. A bounded wait fails the test if the second writer never arrives. An assignment loser returns 412. A completion loser may return 409 from the unique-record constraint or 412 from optimistic concurrency, depending on SQL command ordering; either must leave exactly one completion record and no losing audit entries.

## Isolation and cleanup

`tests/Shared/SqlTestDatabase.cs` creates a new `FleetOps_Test_<32-digit-GUID>` database for each test instance, using only its Testcontainers connection. Tests never load the repository `.env` or use the running development API/database. Both integration suites use the same helper, with a fresh WebApplicationFactory per HTTP test. Cleanup refuses any database name outside that exact generated namespace. Tests cover the cleanup guard itself. SQL Server and verification SDK images are pinned by digest to reduce environment drift.

Applying existing migrations and creating test accounts/data occurs only in disposable test databases. Phase 5 does not regenerate migrations, apply migrations to the development database, recreate development containers, or modify the user's administrator.

## Regression fixed

The new noncanonical ETag regression failed before the fix: inserting a space inside a quoted Base64 rowversion yielded 204 and changed the asset. `Convert.FromBase64String` tolerates whitespace. `EntityTags.Read` now also requires the decoded bytes to re-encode to exactly the supplied quoted ETag. The same request must return 400 without changing data; normal server-issued ETags remain valid. This is the only production behavior change in this phase.

## Files and design decisions

- Domain: `TechnicianTests.cs`, `WorkOrderTransitionTests.cs`, expanded `AssetRulesTests.cs`; renamed `AssertTest.cs` to `AssetTests.cs`.
- Application: `MaintenanceQueryTests.cs`; renamed the existing query test file to `AssetQueryTests.cs`.
- HTTP: `MaintenanceApiTests.Assets.cs`, `ConcurrentWriteGate.cs`, expanded maintenance/security tests, shared isolated database setup.
- Persistence: expanded rollback/cancellation/service-authorization tests, parameterized test actor, `TestDatabaseSafetyTests.cs`, shared `SqlTestDatabase.cs` linked into both integration projects.
- Tooling: pinned `Dockerfile.verify`, `scripts/Test-InDocker.ps1`, `tests.runsettings`, artifact ignores.
- Production: the canonical-format check in `src/FleetOps.Api/Http/EntityTags.cs`.

The suite targets meaningful behavior rather than a coverage percentage. It does not add browser/UI, load, distributed-system, background-processing, or production deployment tests. Those concerns belong to later implementation and deployment work.

## Learning and interview practice

Read the completion race from HTTP If-Match through application version checks, EF original rowversion, SQL constraints/transaction, error mapping, and a fresh-context read of the persisted outcome. Distinguish a request rejected before a write from two valid requests actually racing at the database.

1. Why can two requests pass an application-level version check, and how does SQL Server rowversion prevent both updates from committing?
2. Why does a real SQL integration test reveal bugs that an in-memory repository can miss, and how do you keep those tests isolated?

Challenge: explain why a failed completion must roll back the order update, maintenance record, and audit entries together. Identify the test assertions that would catch a partial commit.

References: [EF Core testing strategies](https://learn.microsoft.com/en-us/ef/core/testing/choosing-a-testing-strategy), [testing against the database](https://learn.microsoft.com/en-us/ef/core/testing/testing-with-the-database), [EF Core interceptors](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors).
