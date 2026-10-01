using System.Net;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Api;

/// <summary>
/// HU-011 CA2: un usuario con formato inválido se rechaza antes de tocar la BD, con la misma respuesta que unas
/// credenciales incorrectas. Sin esto Postgres (collation ICU) e InMemory (OrdinalIgnoreCase) divergían
/// ("ｂｏｂ" entraba como "bob" solo en Postgres) y un "\0" daba 500 en Npgsql.
/// </summary>
public abstract class LoginValidationTestsBase
{
    protected abstract Task<TestServer> StartServerAsync();

    public static TheoryData<string> MalformedUsernames => new()
    {
        "bob\0",     // NUL: Postgres lo rechaza con excepción
        "Bob\n",     // salto de línea final
        "bob!",      // carácter no permitido
        "ｂｏｂ",     // ancho completo: ICU nivel 2 lo iguala a "bob"
        "b​ob", // espacio de ancho cero: ignorable en ICU
    };

    [Theory]
    [MemberData(nameof(MalformedUsernames))]
    public async Task Login_MalformedUsername_IsIndistinguishableFromWrongCredentials(string username)
    {
        await using var server = await StartServerAsync();
        using var api = new ApiClient(server);
        await api.Register("bob", "segura123"); // 201, o 409 si otro caso ya la creó en la misma BD

        var wrong = await api.Login("bob", "incorrecta");
        var malformed = await api.Login(username, "segura123"); // contraseña correcta: aun así no debe entrar

        malformed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        malformed.StatusCode.ShouldBe(wrong.StatusCode);
        (await malformed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .ShouldBe(await wrong.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}

public sealed class LoginValidationInMemoryTests : LoginValidationTestsBase
{
    protected override Task<TestServer> StartServerAsync() => TestServer.StartAsync();
}
