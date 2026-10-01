using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PixelRealms.Persistence.InMemory;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Api;

public sealed class CharacterEndpointsTests
{
    [Fact]
    public async Task Create_Level1_AtStartMap_WithStartingGear() // HU-012 CA2/CA5, HU-058
    {
        await using var server = await TestServer.StartAsync();
        using var api = await new ApiClient(server).RegisterAndLogin();
        var r = await api.CreateCharacter("Ana", "warrior");
        r.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("level").GetInt32().ShouldBe(1);
        body.GetProperty("mapId").GetString().ShouldBe("meadow");
        var store = (InMemoryStore)server.Services.GetService(typeof(InMemoryStore))!;
        var c = store.Characters.Values.Single();
        c.Hp.ShouldBe(60 + 12 * 12); // baseHp 60 + sta 12 × hpPerSta 12 (el equipo inicial no da stats)
        c.Resource.ShouldBe(0); // la ira empieza en 0
        c.Items.Count.ShouldBe(4); // espada, cota, escudo equipados + 5 panes en la bolsa
        c.Items.Count(i => i.Container == 1).ShouldBe(3);
        c.Items.Single(i => i.TemplateId == "bread").Quantity.ShouldBe(5);
        c.Hotbar.ShouldHaveSingleItem().Ref.ShouldBe("warrior_heroic_strike");
    }

    [Fact]
    public async Task Create_Mage_FullMana()
    {
        await using var server = await TestServer.StartAsync();
        using var api = await new ApiClient(server).RegisterAndLogin();
        await api.CreateCharacterId("Mago", "mage");
        var store = (InMemoryStore)server.Services.GetService(typeof(InMemoryStore))!;
        var c = store.Characters.Values.Single();
        // int 14 + 1 (bastón de aprendiz, alta) = 15 → maná 60 + 15·8 = 180; vida 40 + 8·10 = 120
        c.Resource.ShouldBe(180);
        c.Hp.ShouldBe(120);
    }

    [Theory] // HU-012 CA2/CA3
    [InlineData("ab", "warrior", HttpStatusCode.BadRequest)]
    [InlineData("1ana", "warrior", HttpStatusCode.BadRequest)]
    [InlineData("ana_x", "warrior", HttpStatusCode.BadRequest)]
    [InlineData("admin", "warrior", HttpStatusCode.BadRequest)]
    [InlineData("Ana", "paladin", HttpStatusCode.BadRequest)]
    public async Task Create_InvalidNameOrClass(string name, string classId, HttpStatusCode expected)
    {
        await using var server = await TestServer.StartAsync();
        using var api = await new ApiClient(server).RegisterAndLogin();
        (await api.CreateCharacter(name, classId)).StatusCode.ShouldBe(expected);
    }

    [Fact]
    public async Task Create_DuplicateName_409_AndMax4() // HU-012 CA3/CA4
    {
        await using var server = await TestServer.StartAsync();
        using var api = await new ApiClient(server).RegisterAndLogin();
        await api.CreateCharacterId("Ana", "warrior");
        var dup = await api.CreateCharacter("ANA", "mage");
        dup.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await api.CreateCharacterId("Bea", "mage");
        await api.CreateCharacterId("Cris", "rogue");
        await api.CreateCharacterId("Dani", "priest");
        var fifth = await api.CreateCharacter("Eva", "priest");
        fifth.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await fifth.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("max_characters");
    }

    [Fact]
    public async Task List_Delete_AndNameFreed() // HU-013
    {
        await using var server = await TestServer.StartAsync();
        using var api = await new ApiClient(server).RegisterAndLogin();
        var id = await api.CreateCharacterId("Ana", "warrior");
        var list = await api.Http.GetFromJsonAsync<JsonElement>("/api/characters");
        list.GetArrayLength().ShouldBe(1);
        list[0].GetProperty("name").GetString().ShouldBe("Ana");
        (await api.Http.DeleteAsync($"/api/characters/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.Http.GetFromJsonAsync<JsonElement>("/api/characters")).GetArrayLength().ShouldBe(0);
        (await api.CreateCharacter("Ana", "mage")).StatusCode.ShouldBe(HttpStatusCode.Created); // nombre libre (CA4, MVP)
    }

    [Fact]
    public async Task Delete_OtherAccountsCharacter_404() // HU-013 CA3
    {
        await using var server = await TestServer.StartAsync();
        using var ana = await new ApiClient(server).RegisterAndLogin("ana");
        using var bob = await new ApiClient(server).RegisterAndLogin("bob");
        var id = await ana.CreateCharacterId("Ana", "warrior");
        (await bob.Http.DeleteAsync($"/api/characters/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bob.Http.DeleteAsync($"/api/characters/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Endpoints_RequireJwt()
    {
        await using var server = await TestServer.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(server.BaseUrl) };
        (await http.GetAsync("/api/characters")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        http.DefaultRequestHeaders.Authorization = new("Bearer", "a.b.c");
        (await http.GetAsync("/api/characters")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ticket_OnlyForOwnCharacter() // HU-014 CA1 (parte REST)
    {
        await using var server = await TestServer.StartAsync();
        using var ana = await new ApiClient(server).RegisterAndLogin("ana");
        using var bob = await new ApiClient(server).RegisterAndLogin("bob");
        var id = await ana.CreateCharacterId("Ana", "warrior");
        var ticket = await ana.Ticket(id);
        ticket.Length.ShouldBeGreaterThan(40);
        (await bob.Http.PostAsJsonAsync("/api/game/ticket", new { characterId = id })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
