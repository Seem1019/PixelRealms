using PixelRealms.Server.Tests.Api;
using PixelRealms.Server.Tests.Helpers;
using Xunit;

namespace PixelRealms.Server.Tests.Postgres;

public sealed class LoginValidationPostgresTests(PostgresServerFixture pg) : LoginValidationTestsBase, IClassFixture<PostgresServerFixture>
{
    protected override Task<TestServer> StartServerAsync() => pg.StartServerAsync();
}
