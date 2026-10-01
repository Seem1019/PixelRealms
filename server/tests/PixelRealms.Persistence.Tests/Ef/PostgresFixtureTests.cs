using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace PixelRealms.Persistence.Tests.Ef;

/// <summary>Los tests deben correr sobre el mismo Postgres que producción (docker-compose: postgres:17-alpine).</summary>
public sealed class PostgresFixtureTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Container_IsPostgres17_WithIcuCollation()
    {
        await using var db = await pg.Factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var version = await db.Database.SqlQuery<int>($"SELECT current_setting('server_version_num')::int AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken);
        (version / 10000).ShouldBe(17);
        var icu = await db.Database.SqlQuery<string>($"SELECT collprovider::text AS \"Value\" FROM pg_collation WHERE collname = 'case_insensitive'").SingleAsync(TestContext.Current.CancellationToken);
        icu.ShouldBe("i");
    }
}
