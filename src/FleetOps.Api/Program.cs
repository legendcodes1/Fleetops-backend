
using FleetOps.Api.Errors;
using FleetOps.Application.Interfaces;
using FleetOps.Application.Services;
using FleetOps.Infrastructure.Persistence;

using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddScoped<IAssetService, AssetService>();
builder.Services.AddSingleton<IAssetRepository>(_ => new InMemoryAssetRepository(builder.Environment.IsDevelopment()));

var app = builder.Build();
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
