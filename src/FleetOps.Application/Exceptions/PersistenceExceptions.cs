namespace FleetOps.Application.Exceptions;

public sealed class PersistenceConflictException(string message) : Exception(message);
public sealed class ConcurrencyConflictException() : Exception("The resource changed. Reload it and retry with its current version.");
public sealed class PreconditionRequiredException() : Exception("Supply the resource ETag in the If-Match header.");
