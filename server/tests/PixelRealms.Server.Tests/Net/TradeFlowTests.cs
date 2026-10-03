using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PixelRealms.Persistence.Repositories;
using PixelRealms.Server.Players;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Net;

/// <summary>Prueba de juego: en el intercambio el que recibía solo veía «item ×2», sin saber qué le ofrecían.
/// `TradeUpdate` lleva la plantilla de cada objeto ofrecido, en las dos ofertas.</summary>
public sealed class TradeFlowTests
{
    private static async Task<(ApiClient Api, TestGameClient Client, JsonElement Welcome)> Enter(TestServer server, string user, string name, string classId)
    {
        var api = await new ApiClient(server).RegisterAndLogin(user);
        var id = await api.CreateCharacterId(name, classId);
        var client = await TestGameClient.ConnectAsync(server.WsUrl);
        await client.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{await api.Ticket(id)}}"}""");
        return (api, client, await client.ExpectAsync("Welcome"));
    }

    [Fact]
    public async Task TradeUpdate_TellsBothSidesWhichItemIsOffered()
    {
        await using var server = await TestServer.StartAsync();
        var (anaApi, ana, anaWelcome) = await Enter(server, "ana", "Ana", "priest");
        var (bobApi, bob, _) = await Enter(server, "bob", "Bob", "mage");
        using (anaApi) using (bobApi)
        {
            var offered = anaWelcome.GetProperty("inventory").EnumerateArray().First(i => i.ValueKind == JsonValueKind.Object);
            var itemId = offered.GetProperty("id").GetString()!;
            var templateId = offered.GetProperty("templateId").GetString()!;
            await bob.ExpectForIdAsync("EntitySpawn", anaWelcome.GetProperty("selfId").GetInt32());
            await ana.SendAsync("TradeRequest", """{"name":"Bob"}""");
            await bob.ExpectAsync("TradeUpdate", x => x.GetProperty("state").GetString() == "requested");
            await bob.SendAsync("TradeRespond", """{"accept":true}""");
            await ana.ExpectAsync("TradeUpdate", x => x.GetProperty("state").GetString() == "open");

            await ana.SendAsync("TradeOffer", $$"""{"items":[{"itemId":"{{itemId}}","qty":1}],"gold":0}""");
            var theirs = await bob.ExpectAsync("TradeUpdate", x => x.GetProperty("theirs").GetProperty("items").GetArrayLength() == 1);
            theirs.GetProperty("theirs").GetProperty("items")[0].GetProperty("templateId").GetString().ShouldBe(templateId);
            var mine = await ana.ExpectAsync("TradeUpdate", x => x.GetProperty("mine").GetProperty("items").GetArrayLength() == 1);
            mine.GetProperty("mine").GetProperty("items")[0].GetProperty("templateId").GetString().ShouldBe(templateId);
            await ana.DisposeAsync(); await bob.DisposeAsync();
        }
    }

    [Fact]
    public async Task CompletedTrade_SavesBothCharactersAtOnce() // HU-026 CA6: sin esperar al autosave de 60 s
    {
        await using var server = await TestServer.StartAsync();
        var (anaApi, ana, anaWelcome) = await Enter(server, "ana", "Ana", "priest");
        var (bobApi, bob, _) = await Enter(server, "bob", "Bob", "mage");
        using (anaApi) using (bobApi)
        {
            var offered = anaWelcome.GetProperty("inventory").EnumerateArray().First(i => i.ValueKind == JsonValueKind.Object);
            var itemId = Guid.Parse(offered.GetProperty("id").GetString()!);
            await bob.ExpectForIdAsync("EntitySpawn", anaWelcome.GetProperty("selfId").GetInt32());
            await ana.SendAsync("TradeRequest", """{"name":"Bob"}""");
            await bob.ExpectAsync("TradeUpdate", x => x.GetProperty("state").GetString() == "requested");
            await bob.SendAsync("TradeRespond", """{"accept":true}""");
            await ana.ExpectAsync("TradeUpdate", x => x.GetProperty("state").GetString() == "open");
            await ana.SendAsync("TradeOffer", $$"""{"items":[{"itemId":"{{itemId}}","qty":1}],"gold":0}""");
            var version = (await bob.ExpectAsync("TradeUpdate", x => x.GetProperty("theirs").GetProperty("items").GetArrayLength() == 1)).GetProperty("version").GetInt32();

            var saver = (PixelRealms.Server.Hosting.SaveService)server.Services.GetService(typeof(PixelRealms.Server.Hosting.SaveService))!;
            var savedBefore = saver.Saved;
            await ana.SendAsync("TradeConfirm", $$"""{"version":{{version}}}""");
            await bob.SendAsync("TradeConfirm", $$"""{"version":{{version}}}""");
            await bob.ExpectAsync("TradeUpdate", x => x.GetProperty("state").GetString() == "completed");
            for (var i = 0; i < 40 && saver.Saved < savedBefore + 2; i++) await Task.Delay(50, TestContext.Current.CancellationToken);
            saver.Saved.ShouldBeGreaterThanOrEqualTo(savedBefore + 2);

            // En la BD (InMemory) el objeto ya no está en Ana y Bob tiene uno de la misma plantilla.
            var store = (PixelRealms.Persistence.InMemory.InMemoryStore)server.Services.GetService(typeof(PixelRealms.Persistence.InMemory.InMemoryStore))!;
            var anaRow = store.Characters.Values.Single(c => c.Name == "Ana");
            var bobRow = store.Characters.Values.Single(c => c.Name == "Bob");
            anaRow.Items.ShouldNotContain(i => i.Id == itemId);
            bobRow.Items.ShouldContain(i => i.TemplateId == offered.GetProperty("templateId").GetString());
            await ana.DisposeAsync(); await bob.DisposeAsync();
        }
    }

    /// <summary>Anota qué personajes se escriben en cada llamada (uno suelto o un lote) y delega en el repositorio en memoria.</summary>
    private sealed class RecordingRepository(ICharacterRepository inner) : ICharacterRepository
    {
        public System.Collections.Concurrent.ConcurrentQueue<Guid[]> Writes { get; } = new();
        public Task<IReadOnlyList<CharacterSummary>> ListByAccountAsync(Guid accountId, CancellationToken ct = default) => inner.ListByAccountAsync(accountId, ct);
        public Task<int> CountByAccountAsync(Guid accountId, CancellationToken ct = default) => inner.CountByAccountAsync(accountId, ct);
        public Task<bool> NameExistsAsync(string name, CancellationToken ct = default) => inner.NameExistsAsync(name, ct);
        public Task<CreateCharacterResult> CreateAsync(NewCharacter character, int maxPerAccount, CancellationToken ct = default) => inner.CreateAsync(character, maxPerAccount, ct);
        public Task<CharacterSaveDto?> LoadAsync(Guid id, CancellationToken ct = default) => inner.LoadAsync(id, ct);
        public Task SaveAsync(CharacterSaveDto character, CancellationToken ct = default) { Writes.Enqueue([character.Id]); return inner.SaveAsync(character, ct); }
        public Task SaveManyAsync(IReadOnlyList<CharacterSaveDto> characters, CancellationToken ct = default) { Writes.Enqueue(characters.Select(c => c.Id).ToArray()); return inner.SaveManyAsync(characters, ct); }
        public Task<bool> SoftDeleteAsync(Guid accountId, Guid characterId, CancellationToken ct = default) => inner.SoftDeleteAsync(accountId, characterId, ct);
    }

    [Fact]
    public async Task AnotherSaveInTheTickOfACompletedTrade_StillWritesBothTogether() // revisión de autoridad: orden de los guardados
    {
        RecordingRepository? repo = null;
        await using var server = await TestServer.StartAsync(overrideServices: s =>
        {
            s.AddSingleton<PixelRealms.Persistence.InMemory.InMemoryCharacterRepository>();
            s.AddSingleton<ICharacterRepository>(sp => repo = new RecordingRepository(sp.GetRequiredService<PixelRealms.Persistence.InMemory.InMemoryCharacterRepository>()));
        });
        var (anaApi, ana, _) = await Enter(server, "ana", "Ana", "priest");
        var (bobApi, bob, _) = await Enter(server, "bob", "Bob", "mage");
        using (anaApi) using (bobApi)
        {
            var players = server.Services.GetRequiredService<PlayerRegistry>();
            var session = server.Services.GetRequiredService<WorldSession>();
            var (anaId, bobId) = (players.ByName("Ana")!.CharacterId, players.ByName("Bob")!.CharacterId);
            while (repo!.Writes.TryDequeue(out _)) { }
            // Un intercambio acaba de completarse (marca) y en el mismo tick Ana cruza un portal: su guardado se lleva a Bob.
            await server.RunOnTickAsync(t =>
            {
                var (a, b) = (players.ByName("Ana")!, players.ByName("Bob")!);
                (a.TradeSavePartner, b.TradeSavePartner) = (b, a);
                session.Save(a, t.NowMs, "change_map");
            });
            for (var i = 0; i < 40 && repo.Writes.IsEmpty; i++) await Task.Delay(50, TestContext.Current.CancellationToken);
            repo.Writes.ShouldContain(w => w.Length == 2 && w.Contains(anaId) && w.Contains(bobId));
            repo.Writes.ShouldNotContain(w => w.Length == 1 && (w[0] == anaId || w[0] == bobId));
            await server.RunOnTickAsync(_ => players.ByName("Bob")!.TradeSavePartner.ShouldBeNull());
            await ana.DisposeAsync(); await bob.DisposeAsync();
        }
    }
}
