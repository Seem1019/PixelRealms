using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PixelRealms.Server.Auth;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Api;

public sealed class AuthEndpointsTests
{
    [Fact]
    public async Task Register_Returns201_AndHashesPassword() // HU-010 CA1
    {
        await using var server = await TestServer.StartAsync();
        using var api = new ApiClient(server);
        var r = await api.Register("ana", "segura123");
        r.StatusCode.ShouldBe(HttpStatusCode.Created);
        var store = (Persistence.InMemory.InMemoryStore)server.Services.GetService(typeof(Persistence.InMemory.InMemoryStore))!;
        var account = store.Accounts.Values.Single();
        account.PasswordHash.ShouldNotContain("segura123");
        new Passwords().Verify(account.PasswordHash, "segura123").ShouldBeTrue();
    }

    [Fact]
    public async Task Register_DuplicateCaseInsensitive_409() // HU-010 CA2
    {
        await using var server = await TestServer.StartAsync();
        using var api = new ApiClient(server);
        (await api.Register("Ana", "segura123")).StatusCode.ShouldBe(HttpStatusCode.Created);
        var r = await api.Register("ana", "otraclave1");
        r.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await r.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("code").GetString().ShouldBe("username_taken");
    }

    [Theory] // HU-010 CA3
    [InlineData("ab", "segura123", "username")]
    [InlineData("abcdefghijklmnopqrstu", "segura123", "username")]
    [InlineData("ana!", "segura123", "username")]
    [InlineData("ana", "corta", "password")]
    public async Task Register_InvalidFields_400(string username, string password, string field)
    {
        await using var server = await TestServer.StartAsync();
        using var api = new ApiClient(server);
        var r = await api.Register(username, password);
        r.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        body.GetProperty("errors")[0].GetProperty("field").GetString().ShouldBe(field);
    }

    [Fact]
    public async Task Login_ReturnsJwt15Min_AndTouchesLastLogin() // HU-011 CA1
    {
        await using var server = await TestServer.StartAsync();
        using var api = new ApiClient(server);
        await api.Register("ana", "segura123");
        var r = await api.Login("ana", "segura123");
        r.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        var exp = body.GetProperty("expiresAt").GetDateTimeOffset();
        (exp - DateTimeOffset.UtcNow).TotalMinutes.ShouldBeInRange(14, 15.1);
        var jwt = (JwtService)server.Services.GetService(typeof(JwtService))!;
        jwt.Validate(api.Token!, DateTimeOffset.UtcNow)!.Username.ShouldBe("ana");
        var store = (Persistence.InMemory.InMemoryStore)server.Services.GetService(typeof(Persistence.InMemory.InMemoryStore))!;
        store.Accounts.Values.Single().LastLoginAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Login_Invalid_SameGenericMessage() // HU-011 CA2
    {
        await using var server = await TestServer.StartAsync();
        using var api = new ApiClient(server);
        await api.Register("ana", "segura123");
        var wrongPassword = await api.Login("ana", "incorrecta");
        var unknownUser = await api.Login("nadie", "segura123");
        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknownUser.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await wrongPassword.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe(await unknownUser.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Login_SixthAttemptInAMinute_429() // HU-011 CA3
    {
        await using var server = await TestServer.StartAsync();
        using var api = new ApiClient(server);
        await api.Register("ana", "segura123");
        for (var i = 0; i < 5; i++) (await api.Login("ana", "mal")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await api.Login("ana", "segura123")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Login_BehindTheProxy_LimitsEachPlayerByTheirRealIp() // HU-011 CA3 + HU-073: Caddy reenvía X-Forwarded-For
    {
        await using var server = await TestServer.StartAsync(new() { ["JWT_SIGNING_KEY"] = new string('k', 40) }, environment: "Production");
        using var api = new ApiClient(server);
        (await api.Register("ana", "segura123")).StatusCode.ShouldBe(HttpStatusCode.Created);
        // Seis jugadores distintos entran a la vez: todos llegan desde la IP del proxy, cada uno con su IP real reenviada.
        for (var i = 1; i <= 6; i++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { username = "ana", password = "segura123" }) };
            req.Headers.Add("X-Forwarded-For", $"203.0.113.{i}");
            (await api.Http.SendAsync(req, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        // El mismo jugador sí sigue limitado: 5 por minuto desde su IP.
        for (var i = 0; i < 5; i++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { username = "ana", password = "mal" }) };
            req.Headers.Add("X-Forwarded-For", "198.51.100.7");
            await api.Http.SendAsync(req, TestContext.Current.CancellationToken);
        }
        using var sixth = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { username = "ana", password = "segura123" }) };
        sixth.Headers.Add("X-Forwarded-For", "198.51.100.7");
        (await api.Http.SendAsync(sixth, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Refresh_WithAValidToken_IssuesANewOne_WithTheAdminFlagFromTheDatabase() // HU-015 CA1
    {
        await using var server = await TestServer.StartAsync();
        using var api = await new ApiClient(server).RegisterAndLogin("ana");
        var store = (Persistence.InMemory.InMemoryStore)server.Services.GetService(typeof(Persistence.InMemory.InMemoryStore))!;
        var accountId = store.Accounts.Values.Single().Id;
        var accounts = (Persistence.Repositories.IAccountRepository)server.Services.GetService(typeof(Persistence.Repositories.IAccountRepository))!;
        await accounts.SetAdminAsync(accountId, true, TestContext.Current.CancellationToken);

        var r = await api.Http.PostAsync("/api/auth/refresh", null, TestContext.Current.CancellationToken);
        r.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var jwt = (JwtService)server.Services.GetService(typeof(JwtService))!;
        var claims = jwt.Validate(body.GetProperty("token").GetString()!, DateTimeOffset.UtcNow)!;
        claims.AccountId.ShouldBe(accountId);
        claims.Admin.ShouldBeTrue(); // releído de la BD, no copiado del token anterior

        using var anon = new HttpClient { BaseAddress = new Uri(server.BaseUrl) };
        (await anon.PostAsync("/api/auth/refresh", null, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_KeepsTheLoginTime_AndStopsAfterTheMaximumSession() // revisión de autoridad: sesiones sin fin
    {
        await using var server = await TestServer.StartAsync();
        using var api = await new ApiClient(server).RegisterAndLogin("ana");
        var jwt = (JwtService)server.Services.GetService(typeof(JwtService))!;
        var accountId = jwt.Validate(api.Token!, DateTimeOffset.UtcNow)!.AccountId;
        var now = DateTimeOffset.UtcNow;
        // Login de hace 13 h renovado hasta hoy: el token aún no caduca, pero la sesión ya pasó el tope de 12 h.
        var (old, _) = jwt.Issue(accountId, "ana", false, now, authTime: now.AddHours(-13));
        using var stale = new HttpClient { BaseAddress = new Uri(server.BaseUrl) };
        stale.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", old);
        (await stale.PostAsync("/api/auth/refresh", null, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        // Uno reciente sí se renueva y conserva su hora de login.
        var r = await api.Http.PostAsync("/api/auth/refresh", null, TestContext.Current.CancellationToken);
        var renewed = jwt.Validate((await r.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("token").GetString()!, DateTimeOffset.UtcNow)!;
        renewed.AuthTime.ShouldBe(jwt.Validate(api.Token!, DateTimeOffset.UtcNow)!.AuthTime);
    }

    [Fact]
    public async Task ExpiredOrTamperedToken_IsRejected() // HU-011 CA5
    {
        var jwt = new JwtService(new string('k', 40), TimeSpan.FromMinutes(15));
        var now = DateTimeOffset.UtcNow;
        var (token, _) = jwt.Issue(Guid.NewGuid(), "ana", false, now);
        jwt.Validate(token, now).ShouldNotBeNull();
        jwt.Validate(token, now.AddMinutes(16)).ShouldBeNull();
        jwt.Validate(token[..^2] + "xx", now).ShouldBeNull();
        new JwtService(new string('z', 40), TimeSpan.FromMinutes(15)).Validate(token, now).ShouldBeNull();
    }
}
