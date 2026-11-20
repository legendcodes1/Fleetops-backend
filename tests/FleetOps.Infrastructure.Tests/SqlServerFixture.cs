using Testcontainers.MsSql;
using FleetOps.Testing;

namespace FleetOps.Infrastructure.Tests;

public sealed class SqlServerFixture : IAsyncLifetime
{
    public MsSqlContainer Container { get; } = new MsSqlBuilder(SqlTestDefaults.Image).Build();
    public Task InitializeAsync() => Container.StartAsync();
    public async Task DisposeAsync() => await Container.DisposeAsync();
}
