using System.Text.Json;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Net;

public sealed class MovementAndAoiTests
{
    private static async Task<TestGameClient> Enter(TestServer server, string user, string name, string classId)
    {
        using var api = await new ApiClient(server).RegisterAndLogin(user);
        var id = await api.CreateCharacterId(name, classId);
        var ticket = await api.Ticket(id);
        var client = await TestGameClient.ConnectAsync(server.WsUrl);
        await client.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{ticket}}"}""");
        await client.ExpectAsync("Welcome");
        return client;
    }

    [Fact]
    public async Task MoveInput_MovesPlayer_SnapshotCarriesAckSeq() // HU-021 CA1/CA2/CA6
    {
        await using var server = await TestServer.StartAsync();
        await using var ana = await Enter(server, "ana", "Ana", "warrior");
        var first = await ana.ExpectAsync("Snapshot");
        var x0 = first.GetProperty("self").GetProperty("x").GetSingle();
        first.GetProperty("ackSeq").GetInt32().ShouldBe(0);
        for (var seq = 1; seq <= 4; seq++)
        {
            await ana.SendAsync("MoveInput", $$"""{"seq":{{seq}},"dx":1,"dy":0}""");
            await Task.Delay(120);
        }
        await ana.SendAsync("MoveInput", """{"seq":5,"dx":0,"dy":0}""");
        await Task.Delay(150);
        var snap = await ana.LatestAsync("Snapshot");
        snap.GetProperty("ackSeq").GetInt32().ShouldBe(5);
        var x1 = snap.GetProperty("self").GetProperty("x").GetSingle();
        (x1 - x0).ShouldBeGreaterThan(16f);   // ~0.5 s a 4 casillas/s ≈ 32 px
        (x1 - x0).ShouldBeLessThan(48f);
        snap.GetProperty("self").GetProperty("speed").GetSingle().ShouldBe(4f);
    }

    [Fact]
    public async Task InvalidInputs_AreIgnored() // HU-021 CA4
    {
        await using var server = await TestServer.StartAsync();
        await using var ana = await Enter(server, "ana", "Ana", "warrior");
        await ana.SendAsync("MoveInput", """{"seq":10,"dx":5,"dy":0}"""); // se recorta a 1
        await Task.Delay(100);
        await ana.SendAsync("MoveInput", """{"seq":3,"dx":0,"dy":0}""");  // seq decreciente: ignorado → sigue moviéndose
        await Task.Delay(200);
        var snap = await ana.LatestAsync("Snapshot");
        snap.GetProperty("ackSeq").GetInt32().ShouldBe(10);
    }

    [Fact]
    public async Task TwoPlayers_SeeEachOther_ViaEntitySpawn_AndEnts() // HU-023 CA1/CA2
    {
        await using var server = await TestServer.StartAsync();
        await using var ana = await Enter(server, "ana", "Ana", "warrior");
        await using var bob = await Enter(server, "bob", "Bob", "mage");
        var spawnForAna = await ana.ExpectAsync("EntitySpawn", m => m.GetProperty("kind").GetString() == "player");
        spawnForAna.GetProperty("name").GetString().ShouldBe("Bob");
        spawnForAna.GetProperty("kind").GetString().ShouldBe("player");
        spawnForAna.GetProperty("classId").GetString().ShouldBe("mage");
        spawnForAna.GetProperty("level").GetInt32().ShouldBe(1);
        var bobId = spawnForAna.GetProperty("id").GetInt32();
        (await bob.ExpectAsync("EntitySpawn", m => m.GetProperty("kind").GetString() == "player")).GetProperty("name").GetString().ShouldBe("Ana");
        var snap = await ana.LatestAsync("Snapshot");
        var bobState = snap.GetProperty("ents").EnumerateArray().Single(e => e.GetProperty("id").GetInt32() == bobId);
        bobState.GetProperty("anim").GetString().ShouldBe("idle");

        await bob.DisposeAsync();
        var despawn = await ana.ExpectForIdAsync("EntityDespawn", bobId, 3000);
        despawn.GetProperty("reason").GetString().ShouldBe("left");
    }
}
