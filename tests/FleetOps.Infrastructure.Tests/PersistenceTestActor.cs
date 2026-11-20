using FleetOps.Application.Security;
namespace FleetOps.Infrastructure.Tests;

// Explicit application identity for persistence tests; not an HTTP authentication bypass.
internal sealed class PersistenceTestActor(string actorRole = Roles.Administrator, Guid? technicianId = null) : ICurrentActor
{
    public bool IsAuthenticated => true;
    public string? UserId => "persistence-test";
    public Guid? TechnicianId => technicianId;
    public bool IsInRole(string role) => role == actorRole;
}
