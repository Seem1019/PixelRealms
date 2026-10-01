// NO COMPILADO EN LA SESIÓN DE LA FASE 1 (NuGet bloqueado): revisar con `dotnet build` antes de confiar en él.
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PixelRealms.Persistence.Repositories;

namespace PixelRealms.Persistence.Ef;

public static class EfPersistence
{
    /// <summary>Cadena de conexión desde `ConnectionStrings:Postgres` o, en su defecto, de las variables POSTGRES_* de .env (HU-002).</summary>
    public static string ConnectionString(IConfiguration config)
    {
        var cs = config.GetConnectionString("Postgres");
        if (!string.IsNullOrEmpty(cs)) return cs;
        var host = config["POSTGRES_HOST"] ?? "localhost";
        var port = config["POSTGRES_PORT"] ?? "5432";
        var user = config["POSTGRES_USER"] ?? "pixelrealms";
        var password = config["POSTGRES_PASSWORD"] ?? "pixelrealms";
        var dbName = config["POSTGRES_DB"] ?? "pixelrealms";
        return $"Host={host};Port={port};Username={user};Password={password};Database={dbName}";
    }

    public static IServiceCollection AddEfPersistence(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContextFactory<GameDbContext>(o => o.UseNpgsql(ConnectionString(config)).UseSnakeCaseNamingConvention());
        services.AddSingleton<IAccountRepository, EfAccountRepository>();
        services.AddSingleton<ICharacterRepository, EfCharacterRepository>();
        return services;
    }

    /// <summary>HU-002 CA3: aplica migraciones pendientes al arrancar y loguea cuáles.</summary>
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var factory = services.GetRequiredService<IDbContextFactory<GameDbContext>>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Persistence");
        await using var db = await factory.CreateDbContextAsync(ct);
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Count == 0) { logger.LogInformation("Base de datos al día (sin migraciones pendientes)"); return; }
        logger.LogInformation("Aplicando {Count} migración(es): {Names}", pending.Count, string.Join(", ", pending));
        await db.Database.MigrateAsync(ct);
    }
}
