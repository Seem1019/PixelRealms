using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PixelRealms.Server.Tests.Helpers;

/// <summary>HttpClient contra el TestServer con helpers de registro/login (reutilizable en M1–M5).</summary>
public sealed class ApiClient : IDisposable
{
    public ApiClient(TestServer server) => Http = new HttpClient { BaseAddress = new Uri(server.BaseUrl) };

    public HttpClient Http { get; }

    public string? Token { get; private set; }

    public Task<HttpResponseMessage> Register(string username, string password) =>
        Http.PostAsJsonAsync("/api/auth/register", new { username, password });

    public async Task<HttpResponseMessage> Login(string username, string password)
    {
        var r = await Http.PostAsJsonAsync("/api/auth/login", new { username, password });
        if (r.IsSuccessStatusCode)
        {
            var text = await r.Content.ReadAsStringAsync();
            var body = JsonDocument.Parse(text).RootElement;
            Token = body.GetProperty("token").GetString();
            Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            r.Content = new StringContent(text, System.Text.Encoding.UTF8, "application/json"); // el cuerpo sigue legible para el test
        }
        return r;
    }

    public async Task<ApiClient> RegisterAndLogin(string username = "ana", string password = "segura123")
    {
        (await Register(username, password)).EnsureSuccessStatusCode();
        (await Login(username, password)).EnsureSuccessStatusCode();
        return this;
    }

    public Task<HttpResponseMessage> CreateCharacter(string name, string classId) =>
        Http.PostAsJsonAsync("/api/characters", new { name, classId });

    public async Task<Guid> CreateCharacterId(string name, string classId)
    {
        var r = await CreateCharacter(name, classId);
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public async Task<string> Ticket(Guid characterId)
    {
        var r = await Http.PostAsJsonAsync("/api/game/ticket", new { characterId });
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ticket").GetString()!;
    }

    public void Dispose() => Http.Dispose();
}
