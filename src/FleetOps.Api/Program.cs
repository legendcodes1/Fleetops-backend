
using FleetOps.Api.Errors;
using FleetOps.Application.Interfaces;
using FleetOps.Application.Services;
using FleetOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using FleetOps.Api.Security;
using FleetOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddFleetOpsSecurity(builder.Configuration);
builder.Services.AddOpenApi(options => options.AddOperationTransformer((operation, context, token) =>
{
    if (context.Description.RelativePath is { } route && route.Contains("{id}")
        && (route.StartsWith("api/assets") || route.StartsWith("api/work-orders") || route.StartsWith("api/technicians"))
        && context.Description.HttpMethod is "PUT" or "DELETE" or "POST")
    {
        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "If-Match", In = ParameterLocation.Header, Required = true,
            Description = "One strong quoted ETag from the latest GET. Missing: 428; malformed: 400; stale: 412.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
        });
    }
    return Task.CompletedTask;
}));
builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BearerDocumentTransformer>());
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddScoped<IAssetService, AssetService>();
builder.Services.AddScoped<IMaintenanceService, MaintenanceService>();
builder.Services.AddScoped<IMaintenanceStore, EfMaintenanceStore>();
builder.Services.AddDbContext<FleetOpsDbContext>((services, options) =>
    options.UseSqlServer(RequiredConnection(services.GetRequiredService<IConfiguration>())));
builder.Services.AddScoped<IAssetRepository, EfAssetRepository>();

var app = builder.Build();
_ = RequiredConnection(app.Configuration);
if (args.Contains("--bootstrap-admin"))
{
    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        var created = await IdentityBootstrap.CreateAdministratorAsync(scope.ServiceProvider,
            app.Configuration["BootstrapAdmin:Email"] ?? "", app.Configuration["BootstrapAdmin:Password"] ?? "");
        Console.WriteLine(created ? "Administrator created." : "An administrator already exists; no account changed.");
    }
    catch (ArgumentException error)
    {
        Console.Error.WriteLine("Administrator was not created. " + error.Message);
        Environment.ExitCode = 1;
    }
    catch (FleetOps.Application.Exceptions.PersistenceConflictException)
    {
        Console.Error.WriteLine("Administrator was not created because the account already exists or changed concurrently.");
        Environment.ExitCode = 1;
    }
    return;
}
if (args.Contains("--initialize-database"))
{
    if (!app.Environment.IsDevelopment())
        throw new InvalidOperationException("Database initialization is only available in Development.");
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<FleetOpsDbContext>();
    await db.Database.MigrateAsync();
    await DevelopmentSeeder.SeedAsync(db);
    return;
}
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
    app.Use(async (context, next) =>
    {
        if (!context.Request.IsHttps)
        {
            await Results.Problem(statusCode: 400, title: "HTTPS is required.").ExecuteAsync(context);
            return;
        }
        await next(context);
    });
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<ValidateSessionMiddleware>();
app.UseAuthorization();
app.MapControllers();

app.Run();

static string RequiredConnection(IConfiguration configuration) => configuration.GetConnectionString("FleetOps")
    ?? throw new InvalidOperationException("Configure ConnectionStrings__FleetOps before starting FleetOps.");

public partial class Program { }
