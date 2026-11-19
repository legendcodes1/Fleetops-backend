
using FleetOps.Api.Errors;
using FleetOps.Application.Interfaces;
using FleetOps.Application.Services;
using FleetOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddScoped<IAssetService, AssetService>();
var connection = builder.Configuration.GetConnectionString("FleetOps")
    ?? throw new InvalidOperationException("Configure ConnectionStrings__FleetOps before starting FleetOps.");
builder.Services.AddDbContext<FleetOpsDbContext>(options => options.UseSqlServer(connection));
builder.Services.AddScoped<IAssetRepository, EfAssetRepository>();

var app = builder.Build();
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
