using Microsoft.Extensions.DependencyInjection;
using PixelRealms.Server.Hosting;
using PixelRealms.Server.Players;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Net;

/// <summary>HU-015: volver a la selección de personaje desde el juego (`Logout` → `LoggedOut`), bloqueado en combate.</summary>
public sealed class LogoutTests
{
    private static async Task<(ApiClient Api, Guid CharacterId, TestGameClient Client, int SelfId)> Enter(TestServer server, string user, string name, string classId)
    {
        var api = await new ApiClient(server).RegisterAndLogin(user);
        var id = await api.CreateCharacterId(name, classId);
        var client = await Connect(server, api, id);
        var selfId = (await client.ExpectAsync("Welcome")).GetProperty("selfId").GetInt32();
        return (api, id, client, selfId);
    }

    private static async Task<TestGameClient> Connect(TestServer server, ApiClient api, Guid characterId)
    {
        var ticket = await api.Ticket(characterId);
        var client = await TestGameClient.ConnectAsync(server.WsUrl);
        await client.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{ticket}}"}""");
        return client;
    }

    [Fact]
    public async Task Logout_OutOfCombat_SavesLeavesAndConfirmsBeforeClosing() // CA1, CA3
    {
        await using var server = await TestServer.StartAsync();
        var (anaApi, _, ana, anaId) = await Enter(server, "ana", "Ana", "warrior");
        var (bobApi, _, bob, _) = await Enter(server, "bob", "Bob", "mage");
        using (anaApi) using (bobApi)
        {
            await bob.ExpectForIdAsync("EntitySpawn", anaId);
            var saver = server.Services.GetRequiredService<SaveService>();
            var savedBefore = saver.Saved;

            await ana.SendAsync("Logout", """{"reqId":1}""");
            await ana.ExpectAsync("LoggedOut");
            (await ana.ExpectCloseAsync()).ShouldBe("logout"); // el cierre llega después de la confirmación

            (await bob.ExpectForIdAsync("EntityDespawn", anaId)).GetProperty("reason").GetString().ShouldBe("left");
            var registry = server.Services.GetRequiredService<PlayerRegistry>();
            registry.Count.ShouldBe(1);
            registry.All.ShouldNotContain(p => p.Name == "Ana");
            server.Services.GetRequiredService<WorldSession>().LinkdeadCount.ShouldBe(0); // no queda linkdead
            await WaitUntil(() => saver.Saved > savedBefore);
            await bob.DisposeAsync();
            await ana.DisposeAsync();
        }
    }

    [Fact]
    public async Task Logout_InCombat_IsRefused_AndThePlayerStaysInTheWorld() // CA2
    {
        await using var server = await TestServer.StartAsync();
        var (anaApi, _, ana, anaId) = await Enter(server, "ana", "Ana", "warrior");
        using (anaApi)
        {
            var registry = server.Services.GetRequiredService<PlayerRegistry>();
            // Solo test: marcar combate como lo haría un golpe recibido (IsInCombat mira LastCombatAtMs).
            registry.All.First(p => p.Id.Value == anaId).LastCombatAtMs = long.MaxValue / 2;

            await ana.SendAsync("Logout", """{"reqId":9}""");
            var err = await ana.ExpectAsync("Error");
            err.GetProperty("code").GetString().ShouldBe("in_combat");
            err.GetProperty("reqId").GetInt32().ShouldBe(9);
            (await ana.ArrivesAsync("LoggedOut", _ => true, 400)).ShouldBeFalse();
            registry.Count.ShouldBe(1);
            await ana.SendAsync("Ping", """{"clientTime":1}""");
            await ana.ExpectAsync("Pong"); // la conexión sigue abierta y el jugador dentro
            await ana.DisposeAsync();
        }
    }

    [Fact]
    public async Task Logout_ThenImmediateReentry_WithTheSameCharacter_ReadsTheSavedStateOnce() // CA5
    {
        await using var server = await TestServer.StartAsync();
        var (anaApi, anaChar, ana, _) = await Enter(server, "ana", "Ana", "warrior");
        using (anaApi)
        {
            var (startX, x) = await WalkRight(ana);
            Math.Abs(x - startX).ShouldBeGreaterThan(8f, "tiene que haberse movido para que un estado viejo se note");

            await ana.SendAsync("Logout");
            await ana.ExpectAsync("LoggedOut");
            await ana.ExpectCloseAsync();

            // Sin esperar: ticket nuevo y Hello enseguida, como al pulsar Jugar en la selección.
            await using var again = await Connect(server, anaApi, anaChar);
            var welcome = await again.ExpectAsync("Welcome");
            welcome.GetProperty("self").GetProperty("x").GetSingle().ShouldBe(x, 0.01f); // estado guardado al salir, no el viejo de BD
            var registry = server.Services.GetRequiredService<PlayerRegistry>();
            registry.Count.ShouldBe(1); // ni duplicado
            server.Services.GetRequiredService<WorldSession>().LinkdeadCount.ShouldBe(0); // ni linkdead
            (await again.ArrivesAsync("Error", _ => true, 300)).ShouldBeFalse();
            await ana.DisposeAsync();
        }
    }

    [Fact]
    public async Task Logout_CancelsTheCast_TheTradeAndTheDuel_ForEveryoneInvolved() // CA3
    {
        await using var server = await TestServer.StartAsync();
        var (anaApi, _, ana, anaId) = await Enter(server, "ana", "Ana", "priest");
        var (bobApi, _, bob, bobId) = await Enter(server, "bob", "Bob", "mage");
        var (calApi, _, cal, _) = await Enter(server, "cal", "Cal", "rogue");
        using (anaApi) using (bobApi) using (calApi)
        {
            await bob.ExpectForIdAsync("EntitySpawn", anaId);
            await ana.SendAsync("TradeRequest", """{"name":"Bob"}""");
            await bob.ExpectAsync("TradeUpdate", x => x.GetProperty("state").GetString() == "requested");
            await bob.SendAsync("TradeRespond", """{"accept":true}""");
            await bob.ExpectAsync("TradeUpdate", x => x.GetProperty("state").GetString() == "open");
            await ana.SendAsync("DuelRequest", """{"name":"Cal"}""");
            await cal.ExpectAsync("DuelUpdate", x => x.GetProperty("state").GetString() == "requested");
            await cal.SendAsync("DuelRespond", """{"accept":true}""");
            await cal.ExpectAsync("DuelUpdate", x => x.GetProperty("state").GetString() == "countdown");
            await ana.SendAsync("CastSpell", """{"spellId":"priest_heal","reqId":3}""");
            await bob.ExpectAsync("CastStarted", x => x.GetProperty("casterId").GetInt32() == anaId);

            await ana.SendAsync("Logout");
            await ana.ExpectAsync("LoggedOut");
            await bob.ExpectAsync("TradeUpdate", x => x.GetProperty("state").GetString() == "cancelled");
            await cal.ExpectAsync("DuelUpdate", x => x.GetProperty("state").GetString() == "ended");
            // El casteo se cancela sin coste: los demás la ven salir (EntityDespawn quita su barra) y la cura nunca se resuelve.
            await bob.ExpectForIdAsync("EntityDespawn", anaId);
            (await bob.ArrivesAsync("CastEnded", x => x.GetProperty("casterId").GetInt32() == anaId && x.GetProperty("result").GetString() == "done", 1800)).ShouldBeFalse();
            server.Services.GetRequiredService<PlayerRegistry>().Count.ShouldBe(2);
            await ana.DisposeAsync(); await bob.DisposeAsync(); await cal.DisposeAsync();
        }
    }

    [Fact]
    public async Task Logout_ThenReentry_KeepsTheSpellCooldownRunning() // HU-015 pendiente: los cooldowns no se reiniciaban
    {
        await using var server = await TestServer.StartAsync();
        var (anaApi, anaChar, ana, _) = await Enter(server, "ana", "Ana", "warrior");
        using (anaApi)
        {
            var registry = server.Services.GetRequiredService<PlayerRegistry>();
            await server.RunOnTickAsync(tick => registry.All.Single().Combat.CooldownEndsAtMs["warrior_charge"] = tick.NowMs + 12_000); // recarga de 16 s

            await ana.SendAsync("Logout");
            await ana.ExpectAsync("LoggedOut");
            await using var again = await Connect(server, anaApi, anaChar);
            await again.ExpectAsync("Welcome");
            var cd = await again.ExpectAsync("Cooldown", x => x.GetProperty("spellId").GetString() == "warrior_charge");
            cd.GetProperty("remainingMs").GetInt32().ShouldBeInRange(8_000, 12_000);
            await ana.DisposeAsync();
        }
    }

    [Fact]
    public async Task Logout_DropsThePlayersThreatAndTagsOnMonsters() // HU-015: sin XP ni botín para un ausente
    {
        await using var server = await TestServer.StartAsync();
        var (anaApi, _, ana, _) = await Enter(server, "ana", "Ana", "warrior");
        using (anaApi)
        {
            var session = server.Services.GetRequiredService<WorldSession>();
            var registry = server.Services.GetRequiredService<PlayerRegistry>();
            var world = server.Services.GetRequiredService<PixelRealms.Game.Core.World>();
            await server.RunOnTickAsync(tick =>
            {
                var p = registry.All.Single();
                var monster = world.GetInstance(p.MapInstanceId)!.Monsters.Values.First();
                monster.Threat.Add(p.Id, 50);
                monster.TaggedBy = p.Id;

                session.Logout(p, tick).ShouldBeNull(); // en el mismo tick: el monstruo no llega a meterla en combate

                monster.Threat.Contains(p.Id).ShouldBeFalse();
                monster.TaggedBy.ShouldBeNull();
            });
            await ana.DisposeAsync();
        }
    }

    [Fact]
    public async Task Logout_WithoutBeingInTheWorld_IsConfirmedAnyway()
    {
        await using var server = await TestServer.StartAsync();
        await using var client = await TestGameClient.ConnectAsync(server.WsUrl);
        await client.SendAsync("Logout");
        await client.ExpectAsync("LoggedOut");
        (await client.ExpectCloseAsync()).ShouldBe("logout");
    }

    [Fact]
    public async Task SecondHello_ForACharacterStillInTheWorld_TakesOverTheLivePlayer_WithoutReloading() // CA5 (duplicado)
    {
        await using var server = await TestServer.StartAsync();
        var (anaApi, anaChar, ana, anaId) = await Enter(server, "ana", "Ana", "warrior");
        using (anaApi)
        {
            var registry = server.Services.GetRequiredService<PlayerRegistry>();
            var live = registry.All.Single();
            await using var again = await Connect(server, anaApi, anaChar);
            (await again.ExpectAsync("Welcome")).GetProperty("selfId").GetInt32().ShouldBe(anaId); // el mismo Player, no uno leído de BD
            (await ana.ExpectCloseAsync()).ShouldBe("replaced");
            registry.Count.ShouldBe(1);
            registry.All.Single().ShouldBeSameAs(live);
            await ana.DisposeAsync();
        }
    }

    [Fact]
    public async Task Reentry_WhileTheLogoutSaveIsStillUnwritten_UsesTheStateItLeftWith_NotTheStaleDatabase() // CA5
    {
        var gate = new HeldSaves();
        await using var server = await TestServer.StartAsync(overrideServices: s =>
        {
            s.AddSingleton<PixelRealms.Persistence.InMemory.InMemoryCharacterRepository>();
            s.AddSingleton<PixelRealms.Persistence.Repositories.ICharacterRepository>(sp =>
                new HeldCharacterRepository(sp.GetRequiredService<PixelRealms.Persistence.InMemory.InMemoryCharacterRepository>(), gate));
        });
        var anaApi = await new ApiClient(server).RegisterAndLogin("ana");
        var anaChar = await anaApi.CreateCharacterId("Ana", "warrior");
        var ana = await Connect(server, anaApi, anaChar);
        await ana.ExpectAsync("Welcome");
        using (anaApi)
        {
            var (startX, x) = await WalkRight(ana);
            Math.Abs(x - startX).ShouldBeGreaterThan(8f, "tiene que haberse movido para que la BD vieja se note");

            gate.Hold = true; // la BD no recibe el guardado de salida hasta el final del test
            await ana.SendAsync("Logout");
            await ana.ExpectAsync("LoggedOut");
            await using var again = await Connect(server, anaApi, anaChar);
            var welcome = await again.ExpectAsync("Welcome", timeoutMs: (int)HelloGate.SaveWaitTimeout.TotalMilliseconds + 3000);
            welcome.GetProperty("self").GetProperty("x").GetSingle().ShouldBe(x, 0.01f); // no la posición vieja de la BD
            gate.Release();
            await ana.DisposeAsync();
        }
    }

    [Fact]
    public async Task Hello_WhoseReadPredatesTheLogout_ArrivingAfterTheSaveIsWritten_StillGetsTheExitState() // CA5 (revisión)
    {
        var saves = new HeldSaves();
        var loads = new HeldSaves();
        await using var server = await TestServer.StartAsync(overrideServices: s =>
        {
            s.AddSingleton<PixelRealms.Persistence.InMemory.InMemoryCharacterRepository>();
            s.AddSingleton<PixelRealms.Persistence.Repositories.ICharacterRepository>(sp =>
                new HeldCharacterRepository(sp.GetRequiredService<PixelRealms.Persistence.InMemory.InMemoryCharacterRepository>(), saves, loads));
        });
        var anaApi = await new ApiClient(server).RegisterAndLogin("ana");
        var anaChar = await anaApi.CreateCharacterId("Ana", "warrior");
        var ana = await Connect(server, anaApi, anaChar);
        await ana.ExpectAsync("Welcome");
        using (anaApi)
        {
            loads.Hold = true; // el Hello de la segunda conexión lee la BD ahora y no llega al tick hasta el final
            var again = await Connect(server, anaApi, anaChar);
            await Task.Delay(300, TestContext.Current.CancellationToken);
            var (startX, x) = await WalkRight(ana);
            Math.Abs(x - startX).ShouldBeGreaterThan(8f);
            await ana.SendAsync("Logout");
            await ana.ExpectAsync("LoggedOut");
            var saver = server.Services.GetRequiredService<SaveService>();
            await WaitUntil(() => saver.Pending == 0);
            await Task.Delay(300, TestContext.Current.CancellationToken); // varios ticks y barridos con el guardado ya escrito
            loads.Release();
            var welcome = await again.ExpectAsync("Welcome");
            welcome.GetProperty("self").GetProperty("x").GetSingle().ShouldBe(x, 0.01f); // no la lectura de antes de salir
            await again.DisposeAsync();
            await ana.DisposeAsync();
        }
    }

    /// <summary>Retiene los guardados mientras `Hold`: simula una BD lenta o una cola cargada.</summary>
    private sealed class HeldSaves
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Hold { get; set; }

        public Task WaitAsync() => Hold ? _released.Task : Task.CompletedTask;

        public void Release() { Hold = false; _released.TrySetResult(); }
    }

    private sealed class HeldCharacterRepository(PixelRealms.Persistence.Repositories.ICharacterRepository inner, HeldSaves gate, HeldSaves? loads = null) : PixelRealms.Persistence.Repositories.ICharacterRepository
    {
        public Task<IReadOnlyList<PixelRealms.Persistence.Repositories.CharacterSummary>> ListByAccountAsync(Guid accountId, CancellationToken ct = default) => inner.ListByAccountAsync(accountId, ct);
        public Task<int> CountByAccountAsync(Guid accountId, CancellationToken ct = default) => inner.CountByAccountAsync(accountId, ct);
        public Task<bool> NameExistsAsync(string name, CancellationToken ct = default) => inner.NameExistsAsync(name, ct);
        public Task<PixelRealms.Persistence.Repositories.CreateCharacterResult> CreateAsync(PixelRealms.Persistence.Repositories.NewCharacter character, int maxPerAccount, CancellationToken ct = default) => inner.CreateAsync(character, maxPerAccount, ct);
        public async Task<PixelRealms.Persistence.Repositories.CharacterSaveDto?> LoadAsync(Guid id, CancellationToken ct = default)
        {
            var read = await inner.LoadAsync(id, ct); // la lectura se hace ya; lo que se retiene es su llegada al tick
            if (loads is not null) await loads.WaitAsync();
            return read;
        }
        public async Task SaveAsync(PixelRealms.Persistence.Repositories.CharacterSaveDto character, CancellationToken ct = default)
        {
            await gate.WaitAsync();
            await inner.SaveAsync(character, ct);
        }
        public Task<bool> SoftDeleteAsync(Guid accountId, Guid characterId, CancellationToken ct = default) => inner.SoftDeleteAsync(accountId, characterId, ct);
    }

    /// <summary>Camina ~0,5 s a la derecha (un MoveInput por tick, como el cliente) y devuelve la x de antes y la de después.</summary>
    private static async Task<(float Start, float End)> WalkRight(TestGameClient client)
    {
        var start = (await client.ExpectAsync("Snapshot")).GetProperty("self").GetProperty("x").GetSingle();
        for (var seq = 1; seq <= 4; seq++)
        {
            await client.SendAsync("MoveInput", $$"""{"seq":{{seq}},"dx":1,"dy":0}""");
            await Task.Delay(120);
        }
        await client.SendAsync("MoveInput", """{"seq":5,"dx":0,"dy":0}""");
        await client.ExpectAsync("Snapshot", s => s.GetProperty("ackSeq").GetInt32() == 5);
        await Task.Delay(150);
        return (start, (await client.LatestAsync("Snapshot")).GetProperty("self").GetProperty("x").GetSingle());
    }

    private static async Task WaitUntil(Func<bool> cond, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!cond())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condición no cumplida");
            await Task.Delay(25);
        }
    }
}
