using FleetOps.Application.Security;
namespace FleetOps.Infrastructure.Tests;

// Explicit application identity for persistence tests; not an HTTP authentication bypass.
internal sealed class PersistenceTestActor : ICurrentActor
{
    public bool IsAuthenticated => true;
    public string? UserId => "persistence-test";
    public Guid? TechnicianId => null;
    public bool IsInRole(string role) => role == Roles.Administrator;
}
