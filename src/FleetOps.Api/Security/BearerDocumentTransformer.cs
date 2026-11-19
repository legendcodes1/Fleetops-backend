using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
namespace FleetOps.Api.Security;

public sealed class BearerDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken token)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "ASP.NET Core Identity protected token",
            Description = "Use accessToken from POST /api/auth/login. This is an opaque protected token, not a JWT."
        };
        foreach (var (path, item) in document.Paths)
        {
            if (path is "/api/auth/login" or "/api/auth/refresh") continue;
            if (item.Operations is null) continue;
            foreach (var operation in item.Operations.Values)
            {
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
                operation.Responses ??= new OpenApiResponses();
                operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Authentication required or session expired/revoked." });
                operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Role or assignment does not authorize this operation." });
            }
        }
        return Task.CompletedTask;
    }
}
