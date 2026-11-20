using FleetOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Testcontainers.MsSql;
using FleetOps.Testing;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FleetOps.Api.Tests;

public sealed class ApiSqlFixture : IAsyncLifetime
{
    public MsSqlContainer Container { get; } = new MsSqlBuilder(SqlTestDefaults.Image).Build();
    public Task InitializeAsync() => Container.StartAsync();
    public async Task DisposeAsync() => await Container.DisposeAsync();
}

internal sealed class FleetOpsTestApplication(string connection, string environment = "Development", IInterceptor? interceptor = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        if (interceptor is not null)
            builder.ConfigureServices(services => services.AddDbContext<FleetOpsDbContext>(options => options.AddInterceptors(interceptor)));
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:FleetOps"] = connection,
            ["Logging:LogLevel:Default"] = "Warning",
            ["Logging:LogLevel:Microsoft.EntityFrameworkCore"] = "Critical"
        }));
    }
}

public sealed partial class MaintenanceApiTests(ApiSqlFixture fixture) : IClassFixture<ApiSqlFixture>, IAsyncLifetime
{
    private SqlTestDatabase database = null!;
    private readonly ConcurrentWriteGate writeGate = new();
    private FleetOpsTestApplication app = null!;
    private HttpClient client = null!;

    public async Task InitializeAsync()
    {
        database = await SqlTestDatabase.CreateAsync(fixture.Container.GetConnectionString());
        app = new FleetOpsTestApplication(database.ConnectionString, interceptor: writeGate);
        client = app.CreateClient();
        var administrator = await TestAccounts.BootstrapAsync(app, client);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", administrator.AccessToken);
    }

    public async Task DisposeAsync()
    {
        client?.Dispose();
        try { if (app is not null) await app.DisposeAsync(); }
        finally { if (database is not null) await database.DisposeAsync(); }
    }

    private FleetOpsDbContext Db() => database.CreateContext();
    private static async Task<System.Text.Json.JsonElement> Json(HttpResponseMessage response) =>
        System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null, string? tag = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = System.Net.Http.Json.JsonContent.Create(body);
        if (tag is not null) request.Headers.TryAddWithoutValidation("If-Match", tag);
        return await client.SendAsync(request);
    }
    private async Task<(Guid Id, string Tag)> Create(string path, object body)
    {
        using var response = await Send(HttpMethod.Post, path, body);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return ((await Json(response)).GetProperty("id").GetGuid(), response.Headers.ETag!.ToString());
    }
    private Task<(Guid Id, string Tag)> Asset(string serial = "P-1") =>
        Create("/api/assets", new { name = "Pump", serialNumber = serial });
    private Task<(Guid Id, string Tag)> Technician(string email = "alex@example.test", int availability = 0) =>
        Create("/api/technicians", new { name = "Alex", email, specialization = "Hydraulics", availability });
    private async Task<(Guid Id, string Tag)> Order(Guid assetId, string title = "Inspect") =>
        await Create("/api/work-orders", new { assetId, title, description = "Inspect seals", priority = 2 });
    private async Task<string> Tag(string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return response.Headers.ETag!.ToString();
    }
    private static async Task Problem(HttpResponseMessage response, int status)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(status, (await Json(response)).GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task WorkOrderLifecycleCommitsCompletionRecordAndAudit()
    {
        var asset = await Asset();
        var technician = await Technician();
        var order = await Order(asset.Id);
        var path = $"/api/work-orders/{order.Id}";
        using var updated = await Send(HttpMethod.Put, path, new { title = "Updated inspection", description = "Inspect seals", priority = 3 }, order.Tag);
        Assert.Equal(200, (int)updated.StatusCode);
        using var assigned = await Send(HttpMethod.Post, path + "/assign", new { technicianId = technician.Id }, updated.Headers.ETag!.ToString());
        Assert.Equal(200, (int)assigned.StatusCode);
        using var started = await Send(HttpMethod.Post, path + "/start", tag: assigned.Headers.ETag!.ToString());
        Assert.Equal(200, (int)started.StatusCode);
        using var completed = await Send(HttpMethod.Post, path + "/complete", new { notes = "Seal replaced" }, started.Headers.ETag!.ToString());
        Assert.Equal(200, (int)completed.StatusCode);
        var body = await Json(completed);
        Assert.Equal(3, body.GetProperty("status").GetInt32());
        Assert.Equal("Seal replaced", body.GetProperty("maintenanceRecord").GetProperty("notes").GetString());
        using var get = await client.GetAsync(path);
        Assert.Equal("Seal replaced", (await Json(get)).GetProperty("maintenanceRecord").GetProperty("notes").GetString());
        await using var db = Db();
        Assert.Single(await db.MaintenanceRecords.Where(r => r.WorkOrderId == order.Id).ToListAsync());
        Assert.Equal(5, await db.AuditLogs.CountAsync(a => a.EntityId == order.Id));
        using var invalidCancel = await Send(HttpMethod.Post, path + "/cancel", tag: completed.Headers.ETag!.ToString());
        await Problem(invalidCancel, 409);
    }

    [Fact]
    public async Task AssetLifecycleAndTerminalStateAreEnforced()
    {
        var asset = await Asset();
        var path = $"/api/assets/{asset.Id}";
        using var maintenance = await Send(HttpMethod.Post, path + "/maintenance", tag: asset.Tag);
        Assert.Equal(204, (int)maintenance.StatusCode);
        using var stale = await Send(HttpMethod.Post, path + "/return-to-service", tag: asset.Tag);
        await Problem(stale, 412);
        using var returned = await Send(HttpMethod.Post, path + "/return-to-service", tag: await Tag(path));
        Assert.Equal(204, (int)returned.StatusCode);
        using var retired = await Send(HttpMethod.Post, path + "/retire", tag: await Tag(path));
        Assert.Equal(204, (int)retired.StatusCode);
        using var invalid = await Send(HttpMethod.Post, path + "/maintenance", tag: await Tag(path));
        await Problem(invalid, 409);
        using var newOrder = await Send(HttpMethod.Post, "/api/work-orders", new { assetId = asset.Id, title = "Inspect", description = "Seals", priority = 0 });
        await Problem(newOrder, 409);
    }

    [Fact]
    public async Task InvalidInputsAndMissingResourcesReturnProblemDetails()
    {
        using var empty = await Send(HttpMethod.Post, "/api/work-orders", new { assetId = Guid.Empty, title = "", description = "", priority = 99 });
        await Problem(empty, 400);
        using var email = await Send(HttpMethod.Post, "/api/technicians", new { name = "Alex", email = "invalid", specialization = "Hydraulics" });
        await Problem(email, 400);
        using var missingAsset = await Send(HttpMethod.Post, "/api/work-orders", new { assetId = Guid.NewGuid(), title = "Inspect", description = "Seals", priority = 0 });
        await Problem(missingAsset, 404);
        foreach (var url in new[] { "/api/work-orders/" + Guid.NewGuid(), "/api/technicians/" + Guid.NewGuid(),
                     "/api/work-orders?status=99", "/api/technicians?pageSize=101", "/api/work-orders?sortBy=DROP" })
        {
            using var response = await client.GetAsync(url);
            await Problem(response, url.Contains('?') ? 400 : 404);
        }
        using var malformed = new HttpRequestMessage(HttpMethod.Post, "/api/technicians")
        { Content = new StringContent("{invalid", System.Text.Encoding.UTF8, "application/json") };
        using var malformedResult = await client.SendAsync(malformed);
        await Problem(malformedResult, 400);
    }

    [Fact]
    public async Task TechnicianEditAndAvailabilityControlAssignment()
    {
        var asset = await Asset();
        var technician = await Technician(availability: 1);
        var order = await Order(asset.Id);
        var path = $"/api/technicians/{technician.Id}";
        using var unavailable = await Send(HttpMethod.Post, $"/api/work-orders/{order.Id}/assign", new { technicianId = technician.Id }, order.Tag);
        await Problem(unavailable, 409);
        using var updated = await Send(HttpMethod.Put, path, new { name = "Alex Updated", email = "updated@example.test", specialization = "Electrical", availability = 0 }, technician.Tag);
        Assert.Equal(200, (int)updated.StatusCode);
        Assert.NotEqual(technician.Tag, updated.Headers.ETag!.ToString());
        using var stale = await Send(HttpMethod.Put, path, new { name = "Alex", email = "old@example.test", specialization = "Hydraulics", availability = 0 }, technician.Tag);
        await Problem(stale, 412);
        using var duplicate = await Send(HttpMethod.Post, "/api/technicians", new { name = "Duplicate", email = "UPDATED@example.test", specialization = "Electrical" });
        await Problem(duplicate, 409);
        using var assigned = await Send(HttpMethod.Post, $"/api/work-orders/{order.Id}/assign", new { technicianId = technician.Id }, order.Tag);
        Assert.Equal(200, (int)assigned.StatusCode);
    }

    [Fact]
    public async Task WorkOrderErrorsDoNotPartiallyCommit()
    {
        var asset = await Asset();
        var order = await Order(asset.Id);
        var path = $"/api/work-orders/{order.Id}";
        using var missingTag = await Send(HttpMethod.Post, path + "/start");
        await Problem(missingTag, 428);
        using var badTag = await Send(HttpMethod.Post, path + "/start", tag: "bad");
        await Problem(badTag, 400);
        using var invalidStart = await Send(HttpMethod.Post, path + "/start", tag: order.Tag);
        await Problem(invalidStart, 409);
        using var invalidComplete = await Send(HttpMethod.Post, path + "/complete", new { notes = "Attempt" }, order.Tag);
        await Problem(invalidComplete, 409);
        using var missingTech = await Send(HttpMethod.Post, path + "/assign", new { technicianId = Guid.NewGuid() }, order.Tag);
        await Problem(missingTech, 404);
        using var cancelled = await Send(HttpMethod.Post, path + "/cancel", tag: order.Tag);
        Assert.Equal(200, (int)cancelled.StatusCode);
        using var terminalEdit = await Send(HttpMethod.Put, path, new { title = "Changed", description = "Changed", priority = 1 }, cancelled.Headers.ETag!.ToString());
        await Problem(terminalEdit, 409);
        await using var db = Db();
        Assert.Empty(await db.MaintenanceRecords.ToListAsync());
        Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.EntityId == order.Id));
    }

    [Fact]
    public async Task PaginationFiltersSortsAndDashboardReflectState()
    {
        var asset = await Asset();
        var technician = await Technician();
        await Technician("other@example.test", 1);
        var first = await Order(asset.Id, "Alpha");
        await Order(asset.Id, "Beta");
        using var assigned = await Send(HttpMethod.Post, $"/api/work-orders/{first.Id}/assign", new { technicianId = technician.Id }, first.Tag);
        Assert.Equal(200, (int)assigned.StatusCode);
        using var page = await client.GetAsync("/api/work-orders?pageSize=1&sortBy=title&descending=true&priority=High");
        var data = await Json(page);
        Assert.Equal(2, data.GetProperty("totalCount").GetInt32());
        Assert.Equal("Beta", data.GetProperty("items")[0].GetProperty("title").GetString());
        using var filtered = await client.GetAsync($"/api/work-orders?status=Assigned&assetId={asset.Id}&technicianId={technician.Id}");
        Assert.Equal(1, (await Json(filtered)).GetProperty("totalCount").GetInt32());
        using var techs = await client.GetAsync("/api/technicians?availability=Available&search=Hydraulics");
        Assert.Equal(1, (await Json(techs)).GetProperty("totalCount").GetInt32());
        foreach (var sort in new[] { "title", "createdAt", "dueDate", "priority", "status" })
        foreach (var descending in new[] { false, true })
        {
            using var result = await client.GetAsync($"/api/work-orders?sortBy={sort}&descending={descending}");
            Assert.Equal(200, (int)result.StatusCode);
        }
        foreach (var sort in new[] { "name", "email", "specialization", "availability" })
        {
            using var result = await client.GetAsync($"/api/technicians?sortBy={sort}&descending=true");
            Assert.Equal(200, (int)result.StatusCode);
        }
        using var dashboard = await client.GetAsync("/api/dashboard/summary");
        var summary = await Json(dashboard);
        Assert.Equal(1, summary.GetProperty("totalAssets").GetInt32());
        Assert.Equal(2, summary.GetProperty("totalTechnicians").GetInt32());
        Assert.Equal(1, summary.GetProperty("availableTechnicians").GetInt32());
        Assert.Equal(1, summary.GetProperty("openWorkOrders").GetInt32());
        Assert.Equal(1, summary.GetProperty("assignedWorkOrders").GetInt32());
    }

    [Fact]
    public async Task ConcurrentHttpAssignmentsHaveOneWinner()
    {
        var asset = await Asset();
        var first = await Technician();
        var second = await Technician("second@example.test");
        var order = await Order(asset.Id);
        writeGate.WorkOrderId = order.Id;
        var results = await Task.WhenAll(
            Send(HttpMethod.Post, $"/api/work-orders/{order.Id}/assign", new { technicianId = first.Id }, order.Tag),
            Send(HttpMethod.Post, $"/api/work-orders/{order.Id}/assign", new { technicianId = second.Id }, order.Tag));
        using var left = results[0];
        using var right = results[1];
        Assert.Single(results, r => (int)r.StatusCode == 200);
        await Problem(Assert.Single(results, r => (int)r.StatusCode == 412), 412);
        await using var db = Db();
        Assert.Equal((int)left.StatusCode == 200 ? first.Id : second.Id, (await db.WorkOrders.SingleAsync()).AssignedTechnicianId);
        Assert.Equal(2, writeGate.Arrivals);
        Assert.All(writeGate.OriginalVersions, version => Assert.Equal(order.Tag.Trim('"'), version));
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.EntityId == order.Id && a.Action == "Updated"));
    }

    [Fact]
    public async Task OpenApiListsEveryPhaseThreeRoute()
    {
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(200, (int)response.StatusCode);
        var document = await Json(response);
        var paths = document.GetProperty("paths");
        Assert.Equal("bearer", document.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer").GetProperty("scheme").GetString());
        Assert.True(paths.GetProperty("/api/assets").GetProperty("get").GetProperty("security").GetArrayLength() > 0);
        Assert.False(paths.GetProperty("/api/auth/login").GetProperty("post").TryGetProperty("security", out var loginSecurity)
            && loginSecurity.GetArrayLength() > 0);
        foreach (var route in new[] { "/api/assets/{id}/maintenance", "/api/assets/{id}/return-to-service", "/api/assets/{id}/retire",
                     "/api/technicians", "/api/technicians/{id}", "/api/work-orders", "/api/work-orders/{id}",
                     "/api/work-orders/{id}/assign", "/api/work-orders/{id}/start", "/api/work-orders/{id}/complete",
                     "/api/work-orders/{id}/cancel", "/api/dashboard/summary" })
            Assert.True(paths.TryGetProperty(route, out _), route);
        var parameters = paths.GetProperty("/api/work-orders/{id}/assign").GetProperty("post").GetProperty("parameters");
        Assert.Contains(parameters.EnumerateArray(), p => p.GetProperty("name").GetString() == "If-Match" && p.GetProperty("required").GetBoolean());
    }
}
