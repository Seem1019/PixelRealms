using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using Xunit;

namespace PixelRealms.Persistence.Tests.Ef;

/// <summary>Las migraciones se pueden revertir y volver a aplicar (up → down → up) sin dejar restos.</summary>
public sealed class MigrationRoundTripTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task UpDownUp_Succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await pg.Factory.CreateDbContextAsync(ct);
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(Migration.InitialDatabase, cancellationToken: ct); // down hasta vacío (el fixture ya hizo up)
        (await db.Database.GetAppliedMigrationsAsync(ct)).ShouldBeEmpty();

        await migrator.MigrateAsync(cancellationToken: ct); // up otra vez
        (await db.Database.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Model_HasNoChangesWithoutAMigration() // las migraciones de HU-104 en adelante se escribieron sin `dotnet ef`
    {
        await using var db = await pg.Factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        db.Database.HasPendingModelChanges().ShouldBeFalse();
    }
}
