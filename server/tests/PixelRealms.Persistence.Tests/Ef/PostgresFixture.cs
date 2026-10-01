// NO COMPILADO EN LA SESIÓN DE LA FASE 1 (NuGet bloqueado): Testcontainers + Docker. Excluido con OfflineBuild=true.
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PixelRealms.Persistence.Ef;
using Testcontainers.PostgreSql;
using Xunit;

namespace PixelRealms.Persistence.Tests.Ef;

/// <summary>PostgreSQL 17 efímero por clase de tests (HU-002 CA4: nunca la BD local).</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // Misma imagen que producción (docker-compose): la collation ICU debe probarse sobre el mismo Postgres y el mismo ICU.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();

    public IDbContextFactory<GameDbContext> Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        var services = new ServiceCollection();
        services.AddDbContextFactory<GameDbContext>(o => o.UseNpgsql(_container.GetConnectionString()).UseSnakeCaseNamingConvention());
        Factory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<GameDbContext>>();
        await using var db = await Factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
