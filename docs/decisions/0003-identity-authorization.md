# ADR 0003: Identity accounts, protected bearer tokens, and scoped authorization

Status: accepted for Phase 4.

## Context

FleetOps needs password authentication, administrator-managed accounts, three roles, assignment ownership, and revocation. It is currently a single API with SQL Server, without an external identity provider or a browser frontend.

## Decision

Use ASP.NET Core Identity with EF Core stores in the existing FleetOpsDbContext. Identity owns password hashing, password validation, lockout, roles, security stamps, and token protection through the standard authentication handler.

Use the built-in Identity protected bearer-token handler rather than a custom JWT issuer. A focused AuthController exposes login, refresh, me, logout, and password change. Public registration, cookie authentication, email recovery, and MFA management endpoints are not mapped.

Access tokens last 15 minutes; refresh tokens last 24 hours. Both are opaque protected tickets, not JWTs or OAuth/OIDC access tokens. Client-supplied roles, technician ids, or headers never become trusted claims.

An administrator provisions each account with exactly one known role. Enabled Technician accounts require a unique FK-backed mapping to a Technician. This mapping is explicit; matching an email does not grant ownership.

On protected requests, the live account's enabled/lockout state and security stamp are validated before authorization. Access changes, password changes/resets, and logout invalidate previous tickets. Logout revokes all sessions for that account. Refresh validates its own ticket/stamp and reissues current claims. Anonymous login/refresh routes validate their own credentials; an obsolete Authorization header must not prevent reauthentication.

Controllers use named policies. Application services repeat role and assignment checks so HTTP routing is not the only enforcement boundary. Technician work-order filtering happens before pagination/counting, and explicit attempts to filter/read/mutate another technician's orders return 403.

User creation and role assignment are transactional. Access changes use serializable transactions and protect the last enabled administrator. Expected account conflicts and SQL deadlock victims return 409 for a caller-controlled retry. Administrators cannot change their own access through the management endpoint.

Business audits capture the authenticated account id. Identity bookkeeping is excluded from the business-entity audit hook because role/claim rows have different keys. Account-management create/update/reset operations write explicit audit entries.

The first administrator is created only by an explicit local/operator CLI command. It requires externally supplied credentials, refuses to overwrite an existing administrator, and never applies migrations. No production password or bootstrap HTTP endpoint is added.

## Alternatives

- External OIDC/JWT validation: preferred when enterprise SSO, federation, or independent clients are required, but would require provider configuration not currently present. No paid resource is created.
- Identity cookies: appropriate for same-origin browser applications with CSRF protection, but this API currently has token-based clients and no frontend.
- A custom password/token implementation: rejected because the standard framework already provides these primitives.

## Consequences and limits

- This is a self-contained application authentication approach, not an OAuth authorization server. Integrations expecting JWTs must use a real external authority in a future change.
- Refresh tokens are reusable until expiry/revocation, not single-use rotated sessions. There is no per-device session list.
- Revocation requires a database lookup per protected request and affects subsequent requests; in-flight requests are not cancelled.
- Data Protection keys must be persisted and shared across instances, retained for outstanding tickets, and protected at rest for deployment. The local Docker fallback uses a named key volume; it is not a production key-management solution.
- HTTPS is required for remote use. Deployment needs trusted proxy/TLS configuration; local Development allows loopback HTTP.
- Login/refresh have a per-process, per-IP limit of 20 requests/minute; Identity also locks accounts after five failed passwords for five minutes. Distributed rate limiting is not implemented.
- MFA, email confirmation/recovery, federation, and full security-event auditing remain unimplemented and are not claimed.
- Existing maintenance records and asset/work-order schema are preserved by the additive migration.

References: [ASP.NET Core Identity API authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0), [role authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles?view=aspnetcore-10.0).
