namespace FleetOps.Application.Security;

public static class Roles
{
    public const string Administrator = "Administrator";
    public const string Supervisor = "Supervisor";
    public const string Technician = "Technician";
    public static readonly string[] All = [Administrator, Supervisor, Technician];
}
public interface ICurrentActor
{
    bool IsAuthenticated { get; }
    string? UserId { get; }
    Guid? TechnicianId { get; }
    bool IsInRole(string role);
}
public sealed class AccessDeniedException() : Exception("You are not authorized to perform this operation.");
public static class Access
{
    public static void Require(ICurrentActor actor, params string[] roles)
    {
        if (!actor.IsAuthenticated || !roles.Any(actor.IsInRole)) throw new AccessDeniedException();
    }
    public static bool ManagesOperations(ICurrentActor actor) =>
        actor.IsInRole(Roles.Administrator) || actor.IsInRole(Roles.Supervisor);
    public static Guid Technician(ICurrentActor actor)
    {
        Require(actor, Roles.Technician);
        return actor.TechnicianId ?? throw new AccessDeniedException();
    }
}
