using FleetOps.Application.Exceptions;
namespace FleetOps.Api.Http;

internal static class EntityTags
{
    public static string Quote(byte[] version) => $"\"{Convert.ToBase64String(version)}\"";
    public static byte[] Read(HttpRequest request)
    {
        var header = request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(header)) throw new PreconditionRequiredException();
        if (header.Length < 3 || header[0] != '"' || header[^1] != '"')
            throw new ArgumentException("If-Match must contain one quoted resource ETag.");
        try
        {
            var bytes = Convert.FromBase64String(header[1..^1]);
            if (bytes.Length != 8) throw new FormatException();
            return bytes;
        }
        catch (FormatException) { throw new ArgumentException("If-Match must contain one valid resource ETag."); }
    }
}
