using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Application.Security;
using FleetOps.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
namespace FleetOps.Api.Tests;

internal sealed record TestAccount(Guid Id, string Email, string Password, string AccessToken, string RefreshToken);
internal static class TestAccounts
{
    public static string Password() => "Aa9!" + Guid.NewGuid().ToString("N");
    public static async Task<TestAccount> BootstrapAsync(FleetOpsTestApplication app, HttpClient client)
    {
        var email = "admin@example.test";
        var password = Password();
        await using var scope = app.Services.CreateAsyncScope();
        await IdentityBootstrap.CreateAdministratorAsync(scope.ServiceProvider, email, password);
        return await LoginAsync(client, email, password);
    }
    public static async Task<TestAccount> CreateAsync(HttpClient administrator, HttpClient anonymous, string email, string role, Guid? technicianId = null)
    {
        var password = Password();
        using var response = await administrator.PostAsJsonAsync("/api/users", new { email, password, role, technicianId });
        Assert.Equal(201, (int)response.StatusCode);
        return await LoginAsync(anonymous, email, password);
    }
    public static async Task<TestAccount> LoginAsync(HttpClient client, string email, string password)
    {
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(200, (int)login.StatusCode);
        var tokens = await login.Content.ReadFromJsonAsync<JsonElement>();
        var access = tokens.GetProperty("accessToken").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        using var me = await client.SendAsync(request);
        Assert.Equal(200, (int)me.StatusCode);
        var id = (await me.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        return new(id, email, password, access, tokens.GetProperty("refreshToken").GetString()!);
    }
}
