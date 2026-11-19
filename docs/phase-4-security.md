# Phase 4: authentication and authorization

FleetOps now uses ASP.NET Core Identity and its built-in protected bearer tokens. All business endpoints require authenticated staff; accounts are provisioned by administrators. No default passwords, signing secrets, public registration, or paid identity-provider resources are added.

## Permission model

| Capability | Administrator | Supervisor | Technician |
| --- | --- | --- | --- |
| Read assets | Yes | Yes | Yes |
| Create/edit/delete/retire assets | Yes | No | No |
| Send assets to maintenance / return to service | Yes | Yes | No |
| Read technicians | Yes | Yes | No |
| Create/edit technicians | Yes | No | No |
| List/read work orders | All | All | Assigned to linked technician only |
| Create/edit/assign/cancel work orders | Yes | Yes | No |
| Start/complete work orders | All | All | Own assignments only |
| Dashboard summary | Yes | Yes | No |
| Manage user accounts / reset other passwords | Yes | No | No |
| Read own account / change own password / logout | Yes | Yes | Yes |

A technician's authorized task updates are start and complete. Assignment and order metadata remain supervisor-controlled. Client-supplied role/technician fields at login and spoofed headers never grant permissions.

Named controller policies restrict routes. Application services repeat role/ownership checks. Technician list filtering occurs in the SQL query before counting/pagination. Explicit access to another technician's order or a foreign technician filter returns 403. A missing order still returns 404.

## Authentication endpoints

| Method | Route | Behavior |
| --- | --- | --- |
| POST | /api/auth/login | Email/password to protected access and refresh tokens; anonymous |
| POST | /api/auth/refresh | Valid refresh ticket to current claims/new tickets; anonymous |
| GET | /api/auth/me | Account id, email, role, technician mapping, enabled state |
| POST | /api/auth/change-password | Validates current password, changes it, revokes prior tickets |
| POST | /api/auth/logout | Revokes all sessions for the current account |
| GET | /api/users?page=1&pageSize=20 | Administrator-only paginated account list |
| GET | /api/users/{id} | Administrator-only account details |
| POST | /api/users | Create an account with one role and optional technician mapping |
| PUT | /api/users/{id}/access | Change role/mapping/enabled state and revoke previous tickets |
| POST | /api/users/{id}/reset-password | Administrator reset for another account; revokes previous tickets |

User-management writes use Identity concurrency checks and transactions rather than the asset/work-order ETag protocol. Administrators cannot change their own access through this API. Access changes protect the last enabled administrator, including competing demotions.

Passwords require at least 12 characters, four distinct characters, uppercase/lowercase/digit/symbol, and are bounded to 128 characters by HTTP contracts. Identity supplies hashing and validation. Five failed password attempts lock an account for five minutes. Login returns the same generic 401 for invalid/disabled/locked accounts.

Login/refresh share an in-process per-IP limit of 20 requests/minute. Remote deployment requires HTTPS; Development permits local loopback HTTP. No authentication cookies are issued.

The access token expires in 15 minutes; refresh in 24 hours. These are opaque protected tickets, not JWTs. Send Authorization: Bearer followed by accessToken. Missing/invalid/expired/revoked credentials return 401; an authenticated but unauthorized operation returns 403. Errors use ProblemDetails.

Every protected request validates the account's security stamp and enabled/lockout state against SQL Server. Access changes, password changes/resets, and logout invalidate old access and refresh tokens. Refresh rebuilds claims from the current account. Anonymous login/refresh validate their own credentials, so a stale Authorization header does not prevent signing in again.

## Database changes

The additive AddIdentityAccounts migration creates standard AspNet Identity tables, a unique normalized-email index, and a unique optional TechnicianId mapping with a restrictive FK to Technicians. Existing business tables/data are preserved.

An enabled Technician account requires a mapping; other roles cannot have one. Two accounts cannot share a technician. Disabled technician accounts may be unlinked.

Business audit ActorId now records the authenticated account id. CLI/background persistence without an HTTP actor uses system. Identity's role/claim bookkeeping is excluded from the business audit hook; administrator account-management operations have explicit audit entries. This is not a complete authentication/security-event log.

## First administrator and local use

First build and apply the additive migration explicitly if it has not already been applied:

```powershell
. ./scripts/Use-LocalDatabase.ps1
dotnet build FleetOps.slnx -m:1 -p:UseSharedCompilation=false
dotnet ef database update --project src/FleetOps.Infrastructure
```

The initial account is an operator action, not an HTTP endpoint:

```powershell
./scripts/Initialize-Administrator.ps1 -InDocker
```

The script prompts for email/password, passes them through temporary process environment, and clears/restores those variables afterward. It does not print the password or apply migrations. It refuses to overwrite an existing administrator. Omit -InDocker when using a Windows build that the host permits executing.

The helper validates password composition locally before invoking Docker. A rejected password creates no account. Expected CLI validation failures report safe error codes without an unhandled stack trace. The Docker helper shares the running API's persistent key volume; local key-at-rest encryption remains a deployment responsibility described below.

No account is silently seeded by normal startup. Development sample data does not contain user passwords.

After initialization:

```powershell
./scripts/Connect-FleetOps.ps1
Invoke-RestMethod http://localhost:5252/api/auth/me -Headers $FleetOpsHeaders
Invoke-RestMethod http://localhost:5252/api/assets -Headers $FleetOpsHeaders
```

Connect-FleetOps prompts securely and stores bearer headers/refresh token in this PowerShell process without printing the tokens. Failed login clears previous helper credentials. Use HTTPS for remote addresses. Re-run it after expiry or revocation.

For Scalar, call /api/auth/login and paste the returned accessToken into its Bearer authorization field. Never commit real passwords or tokens to the HTTP example files. Existing .http examples now include a placeholder Authorization header.

To run the asset smoke script, authenticate as an administrator first, then run ./scripts/Test-Assets.ps1. That script creates/deletes local test assets and leaves their audit history; automated integration tests use isolated databases instead.

## Docker development runtime

The verified development image is fleetops-verify:phase4. A named volume persists Data Protection keys across restarts:

```powershell
. ./scripts/Use-LocalDatabase.ps1
$env:ConnectionStrings__FleetOps = $env:ConnectionStrings__FleetOps.Replace('tcp:127.0.0.1,14333', 'tcp:sqlserver,1433')
docker run -d --rm --name fleetops-phase4-api --network fleetops_default -p 127.0.0.1:5252:8080 --mount type=volume,source=fleetops-auth-keys,target=/keys -e DataProtection__KeysPath=/keys -e ConnectionStrings__FleetOps -e Logging__LogLevel__Default=Warning fleetops-verify:phase4 dotnet src/FleetOps.Api/bin/Debug/net10.0/FleetOps.Api.dll --environment Development --urls http://0.0.0.0:8080
```

Stop an older development API before binding the same port. Stop the new local API with docker stop fleetops-phase4-api. Reload Use-LocalDatabase.ps1 before running Windows EF tools again so they use IPv4 loopback instead of Docker DNS.

If Windows Application Control blocks host execution, migration application can run in the Linux image with the Docker-network connection above:

```powershell
docker run --rm --network fleetops_default -e ConnectionStrings__FleetOps fleetops-verify:phase4 dotnet ef database update --project src/FleetOps.Infrastructure --no-build
```

This SDK image is a local verification/development fallback. Production container packaging remains Phase 8. No cloud resources are provisioned.

## Key and deployment configuration

DataProtection__KeysPath optionally selects key storage. The local runtime uses /keys in a named Docker volume. Never commit these key files. Production needs shared/persistent keys and an at-rest protection mechanism such as platform key management/certificates, plus trusted TLS/proxy configuration. None of those paid services is created here.

The API checks security stamps on each protected request, so account revocation takes effect on subsequent requests, including after a role or technician-mapping change. It does not cancel requests already in progress.

Refresh tickets are reusable until expiry or security-stamp revocation. There is no single-use refresh-token rotation, per-device session list, MFA API, email confirmation/recovery workflow, enterprise SSO, or distributed rate limiter. These are known limits, not implemented features.

## Files and decisions

- Application/Security defines roles, the current-actor abstraction, permission checks, and user-management contracts.
- Application services enforce privileges and assignment scope.
- Infrastructure/Identity supplies the Identity user, trusted principal claims, administrator provisioning, and transactional user management.
- FleetOpsDbContext now includes Identity stores and authenticated business audit actors; the new migration preserves existing business schema.
- API/Security configures standard authentication, named policies, session validation, rate limits, and OpenAPI bearer metadata.
- AuthController and UsersController expose focused account endpoints; existing controllers have explicit policies.
- TestAccounts uses real login and protected tokens; SecurityApiTests tests access boundaries and revocation against SQL Server.
- Scripts provide secure-prompt bootstrap/login and updated verification helpers.

See decisions/0003-identity-authorization.md for alternatives and tradeoffs.

## Tests and learning

```powershell
dotnet test FleetOps.slnx -m:1 -p:UseSharedCompilation=false
./scripts/Test-InDocker.ps1
```

Security tests use real framework tokens and isolated migrated SQL Server databases. There is no test-only HTTP authentication bypass.

Verified on November 19, 2026:

- Windows and Linux builds passed with zero warnings and zero errors.
- The complete suite passed 67 cases: 30 API cases (16 security, 8 maintenance workflows, 6 error-handler cases), 10 application cases, 16 domain cases, and 11 SQL persistence cases; no failures or skips.
- Checks cover real login/refresh, forged and expired tickets, lockout/rate limits, role boundaries, foreign assignments and spoofed headers, technician relinking, disable/role/password/logout revocation, concurrent administrator demotions, authenticated audits, and production HTTP rejection.
- The additive Identity migration was applied to local SQL Server; both migrations report applied=true. No existing business-table data was changed by the migration.
- The secured local API replaced the Phase 3 container. Assets, orders, users, and dashboard reject anonymous requests with 401; local OpenAPI/Scalar remain available.
- The user created the first administrator successfully with a privately entered password. No default account password was supplied by the application.
- PowerShell helper syntax and local password rejection/environment cleanup were checked; expected missing-bootstrap-credential failures were checked without account changes.

Understand the separation: authentication establishes a principal; authorization decides whether that principal can perform an action on a specific resource. A valid token with the Technician role is insufficient to complete someone else's work order.

Interview questions:

1. Why does role authorization alone fail to prevent insecure direct object references on work orders?
2. Why do protected bearer tickets still need persisted keys and a revocation strategy?

Challenge: trace a technician's completion request from bearer-ticket validation through security-stamp checks, the Staff policy, application assignment checks, rowversion validation, and the atomic order/record/audit save.

Suggested commits:
- Add Identity accounts, role mappings, and additive migration.
- Enforce role policies, assignment ownership, and authenticated audit actors.
- Add account lifecycle, secure local setup, and authorization regression tests.

References: [ASP.NET Core Identity API authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0), [role authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles?view=aspnetcore-10.0).
