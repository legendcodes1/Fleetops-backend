
using System.Text.Json.Serialization;

using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

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