
using FleetOps.Api.Errors;
using FleetOps.Application.Interfaces;
using FleetOps.Application.Services;
using FleetOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi(options => options.AddOperationTransformer((operation, context, token) =>
{
    if (context.Description.RelativePath?.Contains("{id}") == true
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
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// HTTPS redirection disabled temporarily for local HTTP testing
// app.UseHttpsRedirection();

app.UseAuthorization();
app.MapControllers();

app.Run();

static string RequiredConnection(IConfiguration configuration) => configuration.GetConnectionString("FleetOps")
    ?? throw new InvalidOperationException("Configure ConnectionStrings__FleetOps before starting FleetOps.");

public partial class Program { }
