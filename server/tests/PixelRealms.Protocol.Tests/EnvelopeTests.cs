using PixelRealms.Protocol;
using PixelRealms.Protocol.Messages;
using Shouldly;
using Xunit;

namespace PixelRealms.Protocol.Tests;

public sealed class EnvelopeTests
{
    [Fact]
    public void Pong_Encodes_ExactJson() // HU-006 CA4
    {
        MessageRegistry.EncodeToString(new Pong(123, 42)).ShouldBe("""{"t":"Pong","d":{"clientTime":123,"serverTick":42}}""");
    }

    [Fact]
    public void Error_OmitsNullOptionals()
    {
        MessageRegistry.EncodeToString(new Error("invalid_payload", null, null)).ShouldBe("""{"t":"Error","d":{"code":"invalid_payload"}}""");
        MessageRegistry.EncodeToString(new Error("on_cooldown", "Aún no", 7)).ShouldBe("""{"t":"Error","d":{"code":"on_cooldown","message":"Aún no","reqId":7}}""");
    }

    [Fact]
    public void Ping_Decodes_RoundTrip()
    {
        var r = MessageRegistry.Decode("""{"t":"Ping","d":{"clientTime":123}}""");
        r.Status.ShouldBe(MessageRegistry.DecodeStatus.Ok);
        r.Message.ShouldBeOfType<Ping>().ClientTime.ShouldBe(123);
        MessageRegistry.NameOf(r.Message!).ShouldBe("Ping");
    }

    [Fact]
    public void CastSpell_WithOptionalTargetPos_Decodes()
    {
        var r = MessageRegistry.Decode("""{"t":"CastSpell","d":{"spellId":"mage_flame_burst","targetPos":{"x":80.5,"y":96},"reqId":3}}""");
        var m = r.Message.ShouldBeOfType<CastSpell>();
        m.TargetId.ShouldBeNull();
        m.TargetPos!.Value.X.ShouldBe(80.5f);
        m.ReqId.ShouldBe(3);
    }

    [Fact]
    public void UnknownType_InvalidJson_TooLarge_AreReported() // HU-006 CA2
    {
        MessageRegistry.Decode("""{"t":"Hack","d":{}}""").Status.ShouldBe(MessageRegistry.DecodeStatus.UnknownType);
        MessageRegistry.Decode("not json").Status.ShouldBe(MessageRegistry.DecodeStatus.InvalidJson);
        MessageRegistry.Decode("""[1,2]""").Status.ShouldBe(MessageRegistry.DecodeStatus.InvalidJson);
        MessageRegistry.Decode(new byte[MessageRegistry.MaxInboundBytes + 1]).Status.ShouldBe(MessageRegistry.DecodeStatus.TooLarge);
    }

    [Fact]
    public void MissingRequiredField_IsInvalidPayload()
    {
        MessageRegistry.Decode("""{"t":"Hello","d":{"protocolVersion":1}}""").Status.ShouldBe(MessageRegistry.DecodeStatus.InvalidPayload);
        MessageRegistry.Decode("""{"t":"MoveInput","d":{"seq":"x","dx":1,"dy":0}}""").Status.ShouldBe(MessageRegistry.DecodeStatus.InvalidPayload);
    }

    [Fact]
    public void EveryClientMessage_HasAName_AndRegistryMatchesDocs()
    {
        MessageRegistry.ClientMessageNames.ShouldContain("Hello");
        MessageRegistry.ClientMessageNames.ShouldContain("MoveInput");
        MessageRegistry.ServerMessageNames.ShouldContain("CombatEvents");
        MessageRegistry.ServerMessageNames.ShouldNotContain("CombatEvent");
        MessageRegistry.ClientMessageNames.Count.ShouldBe(34);
        MessageRegistry.ServerMessageNames.Count.ShouldBe(24);
    }

    [Fact]
    public void Snapshot_Encodes_PositionsAndEnts()
    {
        var snap = new Snapshot(10, 5, new SnapshotSelfDto(80, 96, 4, 50, 60, 10, 100), [new EntStateDto(2, 16, 32, "s", 100, "idle", null)]);
        MessageRegistry.EncodeToString(snap).ShouldBe("""{"t":"Snapshot","d":{"tick":10,"ackSeq":5,"self":{"x":80,"y":96,"speed":4,"hp":50,"maxHp":60,"res":10,"maxRes":100},"ents":[{"id":2,"x":16,"y":32,"dir":"s","hpPct":100,"anim":"idle"}]}}""");
    }
}
