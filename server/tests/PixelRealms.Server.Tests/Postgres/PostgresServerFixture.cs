using PixelRealms.Server.Tests.Helpers;
using Testcontainers.PostgreSql;
using Xunit;

namespace PixelRealms.Server.Tests.Postgres;

/// <summary>PostgreSQL efímero (misma imagen que producción) para arrancar el servidor real con persistencia EF.</summary>
public sealed class PostgresServerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    /// <summary>El servidor aplica las migraciones al arrancar (HU-002 CA3).</summary>
    public Task<TestServer> StartServerAsync() => TestServer.StartAsync(new Dictionary<string, string?>
    {
        ["Persistence:Provider"] = "Postgres",
        ["ConnectionStrings:Postgres"] = _container.GetConnectionString(),
    });

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
