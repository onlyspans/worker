using DotNet.Testcontainers.Builders;
using Testcontainers.PostgreSql;

namespace Onlyspans.Worker.Api.Tests.Helpers;

// Pattern: https://dotnet.testcontainers.org/
// IAsyncLifetime v3: inherits IAsyncDisposable — both methods return ValueTask
// Migration ref: https://xunit.net/docs/getting-started/v3/migration
public sealed class TestContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync() => await _postgres.StartAsync();

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();
}
