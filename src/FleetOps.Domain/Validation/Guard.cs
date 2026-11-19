namespace FleetOps.Domain.Validation;

internal static class Guard
{
    public static string Text(string? value, int maxLength, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{parameter} cannot be empty.", parameter);
        value = value.Trim();
        if (value.Length > maxLength)
            throw new ArgumentException($"{parameter} cannot exceed {maxLength} characters.", parameter);
        return value;
    }

    public static Guid Id(Guid value, string parameter) =>
        value == Guid.Empty ? throw new ArgumentException($"{parameter} cannot be empty.", parameter) : value;

    public static T Defined<T>(T value, string parameter) where T : struct, Enum =>
        Enum.IsDefined(value) ? value : throw new ArgumentException($"Invalid {parameter}.", parameter);
}
