using System.Text.Json;
using FleetOps.Api.Errors;
using FleetOps.Domain.Exceptions;
using FleetOps.Application.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FleetOps.Api.Tests;

public sealed class ApiExceptionHandlerTests
{
    [Theory]
    [InlineData("argument", 400)]
    [InlineData("domain", 409)]
    [InlineData("unexpected", 500)]
    [InlineData("persistence", 409)]
    [InlineData("concurrency", 412)]
    [InlineData("precondition", 428)]
    public async Task ErrorsUseProblemDetailsAndHideUnexpectedDetails(string kind, int expectedStatus)
    {
        using var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        using var body = new MemoryStream();
        context.Response.Body = body;
        Exception exception = kind switch
        {
            "argument" => new ArgumentException("Invalid asset"),
            "domain" => new DomainRuleException("Asset is retired"),
            "persistence" => new PersistenceConflictException("Duplicate serial"),
            "concurrency" => new ConcurrencyConflictException(),
            "precondition" => new PreconditionRequiredException(),
            _ => new Exception("Sensitive database information")
        };
        var handler = new ApiExceptionHandler(services.GetRequiredService<IProblemDetailsService>(),
            NullLogger<ApiExceptionHandler>.Instance);

        Assert.True(await handler.TryHandleAsync(context, exception, CancellationToken.None));
        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);
        body.Position = 0;
        using var json = await JsonDocument.ParseAsync(body);
        Assert.Equal(expectedStatus, json.RootElement.GetProperty("status").GetInt32());
        if (expectedStatus == 500)
            Assert.DoesNotContain("Sensitive", json.RootElement.GetRawText());
        else
            Assert.Equal(exception.Message, json.RootElement.GetProperty("detail").GetString());
    }
}
