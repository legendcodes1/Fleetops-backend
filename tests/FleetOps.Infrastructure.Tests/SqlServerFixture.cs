using Testcontainers.MsSql;

namespace FleetOps.Infrastructure.Tests;

public sealed class SqlServerFixture : IAsyncLifetime
{
    public MsSqlContainer Container { get; } = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    public Task InitializeAsync() => Container.StartAsync();
    public async Task DisposeAsync() => await Container.DisposeAsync();
}
