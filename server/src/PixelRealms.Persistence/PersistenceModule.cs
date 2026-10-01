using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PixelRealms.Persistence.InMemory;
using PixelRealms.Persistence.Repositories;

namespace PixelRealms.Persistence;

/// <summary>
/// Registro de la persistencia. `Persistence:Provider` = "Postgres" (por defecto, EF Core + Npgsql) o "InMemory" (tests y
/// arranque sin BD). Con OfflineBuild=true solo existe InMemory.
/// </summary>
public static class PersistenceModule
{
    public const string ProviderKey = "Persistence:Provider";

    public static bool UsesInMemory(IConfiguration config) =>
#if OFFLINE_BUILD
        true;
#else
        string.Equals(config[ProviderKey], "InMemory", StringComparison.OrdinalIgnoreCase);
#endif

    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration config)
    {
        if (UsesInMemory(config))
        {
            services.AddSingleton<InMemoryStore>();
            services.AddSingleton<IAccountRepository, InMemoryAccountRepository>();
            services.AddSingleton<ICharacterRepository, InMemoryCharacterRepository>();
            return services;
        }
#if OFFLINE_BUILD
        return services;
#else
        return Ef.EfPersistence.AddEfPersistence(services, config);
#endif
    }

    /// <summary>Migraciones al arrancar (HU-002 CA3). En memoria no hay nada que migrar.</summary>
    public static Task MigrateAsync(IServiceProvider services, IConfiguration config, CancellationToken ct = default)
    {
#if OFFLINE_BUILD
        return Task.CompletedTask;
#else
        return UsesInMemory(config) ? Task.CompletedTask : Ef.EfPersistence.MigrateAsync(services, ct);
#endif
    }
}
