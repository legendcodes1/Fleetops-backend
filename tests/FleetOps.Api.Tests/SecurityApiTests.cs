using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Application.Security;
using FleetOps.Infrastructure.Identity;
using FleetOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FleetOps.Api.Tests;

public sealed class SecurityApiTests(ApiSqlFixture fixture) : IClassFixture<ApiSqlFixture>, IAsyncLifetime
{
    private string connection = "";
    private FleetOpsTestApplication app = null!;
    private HttpClient anonymous = null!;
    private HttpClient administrator = null!;
    private TestAccount admin = null!;

    public async Task InitializeAsync()
    {
        connection = new SqlConnectionStringBuilder(fixture.Container.GetConnectionString())
        { InitialCatalog = "FleetOps_Security_" + Guid.NewGuid().ToString("N") }.ConnectionString;
        await using (var db = Db()) await db.Database.MigrateAsync();
        app = new FleetOpsTestApplication(connection);
        anonymous = app.CreateClient();
        administrator = app.CreateClient();
        admin = await TestAccounts.BootstrapAsync(app, anonymous);
        administrator.DefaultRequestHeaders.Authorization = new("Bearer", admin.AccessToken);
    }
    public async Task DisposeAsync()
    {
        administrator?.Dispose();
        anonymous?.Dispose();
        if (app is not null) await app.DisposeAsync();
        await using var db = Db();
        await db.Database.EnsureDeletedAsync();
    }
    private FleetOpsDbContext Db() => new(new DbContextOptionsBuilder<FleetOpsDbContext>().UseSqlServer(connection).Options);
    private static async Task<JsonElement> Json(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    private static async Task Problem(HttpResponseMessage response, int status)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(status, (await Json(response)).GetProperty("status").GetInt32());
    }
    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object? body = null,
        string? access = null, string? etag = null, bool spoof = false)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (access is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag);
        if (spoof)
        {
            request.Headers.TryAddWithoutValidation("X-Role", "Administrator");
            request.Headers.TryAddWithoutValidation("X-Technician-Id", Guid.NewGuid().ToString());
        }
        return await client.SendAsync(request);
    }
    private async Task<(Guid Id, string Tag)> Asset(string serial = "SEC-1")
    {
        using var response = await administrator.PostAsJsonAsync("/api/assets", new { name = "Pump", serialNumber = serial });
        Assert.Equal(201, (int)response.StatusCode);
        return ((await Json(response)).GetProperty("id").GetGuid(), response.Headers.ETag!.ToString());
    }
    private async Task<Guid> Technician(string email)
    {
        using var response = await administrator.PostAsJsonAsync("/api/technicians", new { name = "Technician", email, specialization = "Hydraulics" });
        Assert.Equal(201, (int)response.StatusCode);
        return (await Json(response)).GetProperty("id").GetGuid();
    }
    private async Task<(Guid Id, string Tag)> Order(Guid assetId, Guid? technicianId = null)
    {
        using var response = await administrator.PostAsJsonAsync("/api/work-orders",
            new { assetId, title = "Inspect", description = "Inspect seals", priority = 2 });
        Assert.Equal(201, (int)response.StatusCode);
        var id = (await Json(response)).GetProperty("id").GetGuid();
        var tag = response.Headers.ETag!.ToString();
        if (technicianId.HasValue)
        {
            using var assigned = await Send(administrator, HttpMethod.Post, $"/api/work-orders/{id}/assign",
                new { technicianId }, etag: tag);
            Assert.Equal(200, (int)assigned.StatusCode);
            tag = assigned.Headers.ETag!.ToString();
        }
        return (id, tag);
    }
    private Task<TestAccount> Account(string email, string role, Guid? technicianId = null) =>
        TestAccounts.CreateAsync(administrator, anonymous, email, role, technicianId);

    [Fact]
    public async Task AnonymousBusinessEndpointsAreDeniedAndDocsStayAvailable()
    {
        foreach (var path in new[] { "/api/assets", "/api/technicians", "/api/work-orders", "/api/dashboard/summary", "/api/users", "/api/auth/me" })
        {
            using var response = await anonymous.GetAsync(path);
            await Problem(response, 401);
        }
        using var mutation = await anonymous.PostAsJsonAsync("/api/assets", new { name = "Pump", serialNumber = "ANON" });
        await Problem(mutation, 401);
        using var docs = await anonymous.GetAsync("/openapi/v1.json");
        Assert.Equal(200, (int)docs.StatusCode);
        using var registration = await anonymous.PostAsJsonAsync("/api/auth/register", new { email = "nobody@example.test", password = TestAccounts.Password(), role = "Administrator" });
        Assert.NotEqual(200, (int)registration.StatusCode);
        Assert.NotEqual(201, (int)registration.StatusCode);
    }

    [Fact]
    public async Task InvalidCredentialsAndForgedTokensAreRejected()
    {
        using var missing = await anonymous.PostAsJsonAsync("/api/auth/login", new { email = "missing@example.test", password = TestAccounts.Password() });
        await Problem(missing, 401);
        using var wrong = await anonymous.PostAsJsonAsync("/api/auth/login", new { email = admin.Email, password = TestAccounts.Password() });
        await Problem(wrong, 401);
        var chars = admin.AccessToken.ToCharArray();
        chars[chars.Length / 2] = chars[chars.Length / 2] == 'A' ? 'B' : 'A';
        using var forged = await Send(anonymous, HttpMethod.Get, "/api/assets", access: new string(chars), spoof: true);
        await Problem(forged, 401);
        using var refresh = await anonymous.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = "not-a-protected-ticket" });
        await Problem(refresh, 401);
    }

    [Fact]
    public async Task ExpiredProtectedAccessAndRefreshTicketsAreRejected()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<SignInManager<FleetOpsUser>>();
        var user = (await manager.UserManager.FindByIdAsync(admin.Id.ToString()))!;
        var principal = await manager.CreateUserPrincipalAsync(user);
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<BearerTokenOptions>>().Get(IdentityConstants.BearerScheme);
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1) }, IdentityConstants.BearerScheme);
        using var access = await Send(anonymous, HttpMethod.Get, "/api/assets", access: options.BearerTokenProtector.Protect(ticket));
        await Problem(access, 401);
        using var refresh = await anonymous.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = options.RefreshTokenProtector.Protect(ticket) });
        await Problem(refresh, 401);
    }

    [Fact]
    public async Task LoginUsesIdentityLockoutWithoutDisclosingAccountState()
    {
        for (var i = 0; i < 5; i++)
        {
            using var wrong = await anonymous.PostAsJsonAsync("/api/auth/login", new { email = admin.Email, password = TestAccounts.Password() });
            await Problem(wrong, 401);
            Assert.Equal("Invalid credentials", (await Json(wrong)).GetProperty("title").GetString());
        }
        using var correct = await anonymous.PostAsJsonAsync("/api/auth/login", new { email = admin.Email, password = admin.Password });
        await Problem(correct, 401);
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<FleetOpsUser>>();
        Assert.True(await users.IsLockedOutAsync((await users.FindByIdAsync(admin.Id.ToString()))!));
    }

    [Fact]
    public async Task CredentialEndpointIsRateLimited()
    {
        HttpResponseMessage? last = null;
        try
        {
            for (var i = 0; i < 21; i++)
            {
                last?.Dispose();
                last = await anonymous.PostAsJsonAsync("/api/auth/login", new { email = "absent@example.test", password = TestAccounts.Password() });
            }
            await Problem(last!, 429);
        }
        finally { last?.Dispose(); }
    }

    [Fact]
    public async Task SupervisorCanManageOperationsButCannotManageAssetsUsersOrTechnicians()
    {
        var asset = await Asset();
        var supervisor = await Account("supervisor@example.test", Roles.Supervisor);
        foreach (var path in new[] { "/api/assets", "/api/technicians", "/api/work-orders", "/api/dashboard/summary" })
        {
            using var read = await Send(anonymous, HttpMethod.Get, path, access: supervisor.AccessToken);
            Assert.Equal(200, (int)read.StatusCode);
        }
        using var create = await Send(anonymous, HttpMethod.Post, "/api/work-orders",
            new { assetId = asset.Id, title = "Inspect", description = "Inspect seals", priority = 1 }, supervisor.AccessToken);
        Assert.Equal(201, (int)create.StatusCode);
        using var maintenance = await Send(anonymous, HttpMethod.Post, $"/api/assets/{asset.Id}/maintenance", access: supervisor.AccessToken, etag: asset.Tag);
        Assert.Equal(204, (int)maintenance.StatusCode);
        using var retire = await Send(anonymous, HttpMethod.Post, $"/api/assets/{asset.Id}/retire", access: supervisor.AccessToken, etag: asset.Tag);
        await Problem(retire, 403);
        using var newAsset = await Send(anonymous, HttpMethod.Post, "/api/assets", new { name = "Forbidden", serialNumber = "NO" }, supervisor.AccessToken);
        await Problem(newAsset, 403);
        using var users = await Send(anonymous, HttpMethod.Get, "/api/users", access: supervisor.AccessToken);
        await Problem(users, 403);
        using var technician = await Send(anonymous, HttpMethod.Post, "/api/technicians",
            new { name = "Forbidden", email = "forbidden@example.test", specialization = "Hydraulics" }, supervisor.AccessToken);
        await Problem(technician, 403);
    }

    [Fact]
    public async Task TechnicianCanOnlySeeAndProgressOwnAssignments()
    {
        var asset = await Asset();
        var firstId = await Technician("first@example.test");
        var secondId = await Technician("second@example.test");
        var technician = await Account("first-login@example.test", Roles.Technician, firstId);
        var own = await Order(asset.Id, firstId);
        var other = await Order(asset.Id, secondId);
        await Order(asset.Id);
        using var list = await Send(anonymous, HttpMethod.Get, "/api/work-orders?pageSize=1", access: technician.AccessToken);
        var page = await Json(list);
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(own.Id, page.GetProperty("items")[0].GetProperty("id").GetGuid());
        using var filter = await Send(anonymous, HttpMethod.Get, $"/api/work-orders?technicianId={secondId}", access: technician.AccessToken);
        await Problem(filter, 403);
        using var read = await Send(anonymous, HttpMethod.Get, $"/api/work-orders/{other.Id}", access: technician.AccessToken);
        await Problem(read, 403);
        using var foreignStart = await Send(anonymous, HttpMethod.Post, $"/api/work-orders/{other.Id}/start", access: technician.AccessToken, etag: other.Tag, spoof: true);
        await Problem(foreignStart, 403);
        using var foreignComplete = await Send(anonymous, HttpMethod.Post, $"/api/work-orders/{other.Id}/complete",
            new { notes = "Unauthorized completion" }, technician.AccessToken, other.Tag);
        await Problem(foreignComplete, 403);
        using var started = await Send(anonymous, HttpMethod.Post, $"/api/work-orders/{own.Id}/start", access: technician.AccessToken, etag: own.Tag);
        Assert.Equal(200, (int)started.StatusCode);
        using var completed = await Send(anonymous, HttpMethod.Post, $"/api/work-orders/{own.Id}/complete",
            new { notes = "Repaired seal" }, technician.AccessToken, started.Headers.ETag!.ToString());
        Assert.Equal(200, (int)completed.StatusCode);
        await using var db = Db();
        Assert.Contains(await db.AuditLogs.Where(a => a.EntityId == own.Id).ToListAsync(),
            a => a.Action == "Updated" && a.ActorId == technician.Id.ToString());
    }

    [Fact]
    public async Task TechnicianCannotEscalateThroughBodiesHeadersOrManagementEndpoints()
    {
        var asset = await Asset();
        var id = await Technician("tech@example.test");
        var technician = await Account("tech-login@example.test", Roles.Technician, id);
        var own = await Order(asset.Id, id);
        foreach (var path in new[] { "/api/users", "/api/dashboard/summary", "/api/technicians" })
        {
            using var result = await Send(anonymous, HttpMethod.Get, path, access: technician.AccessToken, spoof: true);
            await Problem(result, 403);
        }
        using var assign = await Send(anonymous, HttpMethod.Post, $"/api/work-orders/{own.Id}/assign", new { technicianId = id }, technician.AccessToken, own.Tag, true);
        await Problem(assign, 403);
        using var cancel = await Send(anonymous, HttpMethod.Post, $"/api/work-orders/{own.Id}/cancel", access: technician.AccessToken, etag: own.Tag);
        await Problem(cancel, 403);
        using var edit = await Send(anonymous, HttpMethod.Put, $"/api/work-orders/{own.Id}", new { title = "Changed", description = "Changed", priority = 3 }, technician.AccessToken, own.Tag);
        await Problem(edit, 403);
        using var login = await anonymous.PostAsJsonAsync("/api/auth/login",
            new { email = technician.Email, password = technician.Password, role = Roles.Administrator, technicianId = Guid.NewGuid() });
        Assert.Equal(200, (int)login.StatusCode);
        using var escalation = await Send(anonymous, HttpMethod.Get, "/api/users", access: (await Json(login)).GetProperty("accessToken").GetString());
        await Problem(escalation, 403);
    }

    [Fact]
    public async Task AdministratorValidatesRoleAndTechnicianMapping()
    {
        foreach (var input in new object[]
        {
            new { email = "unknown@example.test", password = TestAccounts.Password(), role = "Root" },
            new { email = "unlinked@example.test", password = TestAccounts.Password(), role = Roles.Technician },
            new { email = "weak@example.test", password = new string('a', 20), role = Roles.Supervisor }
        })
        {
            using var result = await administrator.PostAsJsonAsync("/api/users", input);
            await Problem(result, 400);
        }
        using var missing = await administrator.PostAsJsonAsync("/api/users",
            new { email = "missing-tech@example.test", password = TestAccounts.Password(), role = Roles.Technician, technicianId = Guid.NewGuid() });
        await Problem(missing, 404);
        var id = await Technician("linked@example.test");
        await Account("linked-login@example.test", Roles.Technician, id);
        using var duplicate = await administrator.PostAsJsonAsync("/api/users",
            new { email = "duplicate@example.test", password = TestAccounts.Password(), role = Roles.Technician, technicianId = id });
        await Problem(duplicate, 409);
        using var self = await administrator.PutAsJsonAsync($"/api/users/{admin.Id}/access", new { role = Roles.Supervisor, isEnabled = false });
        await Problem(self, 400);
        using var listing = await administrator.GetAsync("/api/users?pageSize=1");
        var json = await Json(listing);
        Assert.Equal(2, json.GetProperty("totalCount").GetInt32());
        Assert.False(json.GetProperty("items")[0].TryGetProperty("passwordHash", out _));
    }

    [Fact]
    public async Task DisablingAnAccountRevokesAccessRefreshAndLogin()
    {
        var supervisor = await Account("disable@example.test", Roles.Supervisor);
        using var update = await administrator.PutAsJsonAsync($"/api/users/{supervisor.Id}/access",
            new { role = Roles.Supervisor, isEnabled = false });
        Assert.Equal(200, (int)update.StatusCode);
        using var access = await Send(anonymous, HttpMethod.Get, "/api/assets", access: supervisor.AccessToken);
        await Problem(access, 401);
        using var refresh = await anonymous.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = supervisor.RefreshToken });
        await Problem(refresh, 401);
        using var login = await anonymous.PostAsJsonAsync("/api/auth/login", new { email = supervisor.Email, password = supervisor.Password });
        await Problem(login, 401);
    }

    [Fact]
    public async Task RoleChangesRevokeOldTicketsAndRefreshIssuesCurrentClaims()
    {
        var supervisor = await Account("promote@example.test", Roles.Supervisor);
        using var update = await administrator.PutAsJsonAsync($"/api/users/{supervisor.Id}/access",
            new { role = Roles.Administrator, isEnabled = true });
        Assert.Equal(200, (int)update.StatusCode);
        using var oldAccess = await Send(anonymous, HttpMethod.Get, "/api/assets", access: supervisor.AccessToken);
        await Problem(oldAccess, 401);
        using var oldRefresh = await anonymous.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = supervisor.RefreshToken });
        await Problem(oldRefresh, 401);
        using var reauthenticate = await Send(anonymous, HttpMethod.Post, "/api/auth/login",
            new { email = supervisor.Email, password = supervisor.Password }, supervisor.AccessToken);
        Assert.Equal(200, (int)reauthenticate.StatusCode);
        var current = await TestAccounts.LoginAsync(anonymous, supervisor.Email, supervisor.Password);
        using var refresh = await anonymous.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = current.RefreshToken });
        Assert.Equal(200, (int)refresh.StatusCode);
        using var users = await Send(anonymous, HttpMethod.Get, "/api/users", access: (await Json(refresh)).GetProperty("accessToken").GetString());
        Assert.Equal(200, (int)users.StatusCode);
    }

    [Fact]
    public async Task LogoutAndPasswordChangeRevokeExistingSessions()
    {
        var supervisor = await Account("logout@example.test", Roles.Supervisor);
        using var logout = await Send(anonymous, HttpMethod.Post, "/api/auth/logout", access: supervisor.AccessToken);
        Assert.Equal(204, (int)logout.StatusCode);
        using var old = await Send(anonymous, HttpMethod.Get, "/api/assets", access: supervisor.AccessToken);
        await Problem(old, 401);
        using var oldRefresh = await anonymous.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = supervisor.RefreshToken });
        await Problem(oldRefresh, 401);
        var current = await TestAccounts.LoginAsync(anonymous, supervisor.Email, supervisor.Password);
        var newPassword = TestAccounts.Password();
        using var change = await Send(anonymous, HttpMethod.Post, "/api/auth/change-password",
            new { currentPassword = supervisor.Password, newPassword }, current.AccessToken);
        Assert.Equal(204, (int)change.StatusCode);
        using var revoked = await Send(anonymous, HttpMethod.Get, "/api/assets", access: current.AccessToken);
        await Problem(revoked, 401);
        await TestAccounts.LoginAsync(anonymous, supervisor.Email, newPassword);
    }

    [Fact]
    public async Task AdministratorPasswordResetRevokesOldAccess()
    {
        var supervisor = await Account("reset@example.test", Roles.Supervisor);
        var newPassword = TestAccounts.Password();
        using var reset = await administrator.PostAsJsonAsync($"/api/users/{supervisor.Id}/reset-password", new { newPassword });
        Assert.Equal(204, (int)reset.StatusCode);
        using var access = await Send(anonymous, HttpMethod.Get, "/api/assets", access: supervisor.AccessToken);
        await Problem(access, 401);
        await TestAccounts.LoginAsync(anonymous, supervisor.Email, newPassword);
    }

    [Fact]
    public async Task ConcurrentAdministratorDemotionsCannotRemoveEveryAdministrator()
    {
        var second = await Account("other-admin@example.test", Roles.Administrator);
        var results = await Task.WhenAll(
            Send(administrator, HttpMethod.Put, $"/api/users/{second.Id}/access", new { role = Roles.Supervisor, isEnabled = true }),
            Send(anonymous, HttpMethod.Put, $"/api/users/{admin.Id}/access", new { role = Roles.Supervisor, isEnabled = true }, second.AccessToken));
        try
        {
            Assert.Contains(results, r => (int)r.StatusCode == 200);
            Assert.All(results, r => Assert.Contains((int)r.StatusCode, new[] { 200, 400, 401, 403, 409 }));
            await using var db = Db();
            Assert.True(await db.Users.AnyAsync(u => u.IsEnabled && db.UserRoles.Any(ur => ur.UserId == u.Id
                && db.Roles.Any(role => role.Id == ur.RoleId && role.Name == Roles.Administrator))));
        }
        finally { foreach (var result in results) result.Dispose(); }
    }

    [Fact]
    public async Task TechnicianRelinkingRevokesTheOldMappingAndScopesNewSessions()
    {
        var asset = await Asset();
        var firstId = await Technician("old-map@example.test");
        var secondId = await Technician("new-map@example.test");
        var technician = await Account("relink@example.test", Roles.Technician, firstId);
        var oldOrder = await Order(asset.Id, firstId);
        var newOrder = await Order(asset.Id, secondId);
        using var updated = await administrator.PutAsJsonAsync($"/api/users/{technician.Id}/access",
            new { role = Roles.Technician, technicianId = secondId, isEnabled = true });
        Assert.Equal(200, (int)updated.StatusCode);
        using var old = await Send(anonymous, HttpMethod.Get, "/api/work-orders", access: technician.AccessToken);
        await Problem(old, 401);
        var current = await TestAccounts.LoginAsync(anonymous, technician.Email, technician.Password);
        using var list = await Send(anonymous, HttpMethod.Get, "/api/work-orders", access: current.AccessToken);
        var page = await Json(list);
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(newOrder.Id, page.GetProperty("items")[0].GetProperty("id").GetGuid());
        using var foreign = await Send(anonymous, HttpMethod.Get, $"/api/work-orders/{oldOrder.Id}", access: current.AccessToken);
        await Problem(foreign, 403);
    }

    [Fact]
    public async Task ProductionRejectsPlainHttpEvenWithoutARedirectionPort()
    {
        await using var production = new FleetOpsTestApplication(connection, "Production");
        using var http = production.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        using var response = await http.PostAsJsonAsync("/api/auth/login", new { email = admin.Email, password = admin.Password });
        Assert.Contains((int)response.StatusCode, new[] { 400, 307, 308 });
        using var https = production.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var secure = await https.GetAsync("/api/assets");
        await Problem(secure, 401);
    }
}
