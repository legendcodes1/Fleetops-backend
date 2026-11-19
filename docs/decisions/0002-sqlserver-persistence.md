# ADR 0002: SQL Server persistence and optimistic concurrency

Status: accepted for Phase 2.

## Context

The Phase 1 singleton repository only stored assets for the life of one process. FleetOps needs persistent relationships, enforceable constraints, and protection against concurrent edits.

## Decision

Use EF Core 10 with SQL Server and a scoped FleetOpsDbContext. Keep the existing asset persistence interface and replace its in-memory implementation with EfAssetRepository. Do not add generic repositories or a separate Unit of Work: DbContext already tracks changes and commits them together.

Asset reads use AsNoTracking and projections into application DTOs. Listing supports bounded offset pagination, search, status filtering, and a fixed list of sort fields. Every sort adds Id as a tie-breaker. Total count and page contents are separate statements at the default isolation level, so concurrent changes can make the count and page briefly disagree.

Use native SQL Server rowversion columns for assets, technicians, and work orders. The asset API returns a strong quoted base64 ETag. PUT and DELETE require one If-Match ETag: missing is 428, malformed is 400, stale is 412. SQL rowversion also catches a race after the server reads the entity. A 404 means the resource was absent when the operation loaded it.

Enforce serial uniqueness with a database unique index and an explicit case-insensitive, accent-sensitive collation. Domain inputs are trimmed. Email is also normalized and uniquely indexed. Application-side existence checks alone would race.

Restrict foreign-key deletes to preserve maintenance history. One maintenance record belongs to one work order. AuditLog uses EntityType plus EntityId instead of a foreign key because it spans multiple tables and must survive deletion.

SaveChanges adds audit rows to the same transaction as business changes. The placeholder actor is "system" until authentication provides a trusted actor in Phase 4. This records persistence actions, not security-grade before/after diffs. Bulk SQL bypasses this application auditing.

Normal requests do not apply migrations. A Development-only initialization command explicitly runs migrations and an idempotent seed routine. Production schema changes should use reviewed scripts or migration bundles with separate permissions. Multi-step seeding uses an explicit serializable transaction; ordinary SaveChanges uses EF Core's transaction.

## Consequences

- SQL Server owns row versions; EF includes the original value in UPDATE/DELETE predicates.
- Request cancellation flows to async database operations.
- Clients must adapt to the paginated asset-list envelope and conditional writes.
- Other entity endpoints remain Phase 3; their tables and domain rules exist now.
- No automatic retry policy is added yet. Explicit transactions and retries need coordinated execution strategies.
- Contains search can scan rows. Indexes support common filters/orderings but performance is not yet benchmarked.
- No production credentials or cloud resources are introduced.
