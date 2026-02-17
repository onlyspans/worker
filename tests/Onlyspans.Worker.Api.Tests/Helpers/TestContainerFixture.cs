using DotNet.Testcontainers.Builders;
using Testcontainers.PostgreSql;

namespace Onlyspans.Worker.Api.Tests.Helpers;

// Pattern: https://dotnet.testcontainers.org/
// IAsyncLifetime for XUnit v2/v3 lifecycle management
public sealed class TestContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();
}
