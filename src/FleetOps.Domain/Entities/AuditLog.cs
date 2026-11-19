using FleetOps.Domain.Validation;
namespace FleetOps.Domain.Entities;

public class AuditLog
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EntityId { get; private set; }
    public string EntityType { get; private set; } = "";
    public string Action { get; private set; } = "";
    public string ActorId { get; private set; } = "";
    public DateTimeOffset Timestamp { get; private set; }

    private AuditLog() { }
    public AuditLog(Guid entityId, string entityType, string action, string actorId)
    {
        EntityId = Guard.Id(entityId, nameof(entityId));
        EntityType = Guard.Text(entityType, 100, nameof(entityType));
        Action = Guard.Text(action, 20, nameof(action));
        ActorId = Guard.Text(actorId, 200, nameof(actorId));
        Timestamp = DateTimeOffset.UtcNow;
    }
}
