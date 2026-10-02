using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PixelRealms.Protocol.Messages;

namespace PixelRealms.Protocol;

/// <summary>
/// Registro de tipos de mensaje: nombre `t` ⇄ tipo C# (igual al nombre del record y a la clave del dispatcher GDScript).
/// Codifica sobres <c>{"t":"X","d":{…}}</c> y decodifica intenciones del cliente sin reflexión por mensaje (lookup en diccionario).
/// </summary>
public static class MessageRegistry
{
    private static readonly Dictionary<string, Func<JsonElement, IClientMessage?>> ClientDecoders = new(StringComparer.Ordinal);
    private static readonly Dictionary<Type, string> ClientNames = new();
    private static readonly Dictionary<Type, (string Name, Action<Utf8JsonWriter, IServerMessage> Write)> ServerEncoders = new();

    public const int MaxInboundBytes = 4 * 1024;

    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = false };

    static MessageRegistry()
    {
        Register<Hello>("Hello");
        Register<Ping>("Ping");
        Register<MoveInput>("MoveInput");
        Register<SelectTarget>("SelectTarget");
        Register<CastSpell>("CastSpell");
        Register<CancelCast>("CancelCast");
        Register<AutoAttack>("AutoAttack");
        Register<InventoryMove>("InventoryMove");
        Register<UseItem>("UseItem");
        Register<DestroyItem>("DestroyItem");
        Register<LootOpen>("LootOpen");
        Register<LootTake>("LootTake");
        Register<LootTakeAll>("LootTakeAll");
        Register<VendorOpen>("VendorOpen");
        Register<VendorBuy>("VendorBuy");
        Register<VendorSell>("VendorSell");
        Register<ChatSend>("ChatSend");
        Register<PartyInvite>("PartyInvite");
        Register<PartyRespond>("PartyRespond");
        Register<PartyLeave>("PartyLeave");
        Register<PartyKick>("PartyKick");
        Register<SetHotbar>("SetHotbar");
        Register<Respawn>("Respawn");
        Register<UsePortal>("UsePortal");
        Register<DuelRequest>("DuelRequest");
        Register<DuelRespond>("DuelRespond");
        Register<DuelForfeit>("DuelForfeit");
        Register<TradeRequest>("TradeRequest");
        Register<TradeRespond>("TradeRespond");
        Register<TradeOffer>("TradeOffer");
        Register<TradeConfirm>("TradeConfirm");
        Register<TradeCancel>("TradeCancel");
        Register<AdminCommand>("AdminCommand");
        Register<ChangeClass>("ChangeClass");
        Register<Logout>("Logout");
        RegisterServer<Welcome>("Welcome");
        RegisterServer<LoggedOut>("LoggedOut");
        RegisterServer<Snapshot>("Snapshot");
        RegisterServer<EntitySpawn>("EntitySpawn");
        RegisterServer<EntityDespawn>("EntityDespawn");
        RegisterServer<CastStarted>("CastStarted");
        RegisterServer<CastEnded>("CastEnded");
        RegisterServer<CombatEvents>("CombatEvents");
        RegisterServer<AuraApplied>("AuraApplied");
        RegisterServer<AuraRemoved>("AuraRemoved");
        RegisterServer<Cooldown>("Cooldown");
        RegisterServer<StatsUpdate>("StatsUpdate");
        RegisterServer<XpGain>("XpGain");
        RegisterServer<LevelUp>("LevelUp");
        RegisterServer<InventoryUpdate>("InventoryUpdate");
        RegisterServer<LootWindow>("LootWindow");
        RegisterServer<ChangeMap>("ChangeMap");
        RegisterServer<DuelUpdate>("DuelUpdate");
        RegisterServer<TradeUpdate>("TradeUpdate");
        RegisterServer<VendorWindow>("VendorWindow");
        RegisterServer<ChatMessage>("ChatMessage");
        RegisterServer<PartyUpdate>("PartyUpdate");
        RegisterServer<Died>("Died");
        RegisterServer<Error>("Error");
        RegisterServer<Pong>("Pong");
    }

    public static IReadOnlyCollection<string> ClientMessageNames => ClientDecoders.Keys;

    public static IReadOnlyCollection<string> ServerMessageNames => ServerEncoders.Values.Select(v => v.Name).ToList();

    public static string NameOf(IServerMessage msg) => ServerEncoders[msg.GetType()].Name;

    public static string NameOf(IClientMessage msg) => ClientNames[msg.GetType()];

    /// <summary>Serializa un mensaje del servidor con su sobre.</summary>
    public static byte[] Encode(IServerMessage msg)
    {
        var (name, write) = ServerEncoders[msg.GetType()];
        var buffer = new System.Buffers.ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("t", name);
            writer.WritePropertyName("d");
            write(writer, msg);
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    public static string EncodeToString(IServerMessage msg) => System.Text.Encoding.UTF8.GetString(Encode(msg));

    /// <summary>Resultado de decodificar un sobre del cliente.</summary>
    public enum DecodeStatus { Ok, InvalidJson, UnknownType, InvalidPayload, TooLarge }

    public readonly record struct DecodeResult(DecodeStatus Status, string? Type, IClientMessage? Message);

    public static DecodeResult Decode(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length > MaxInboundBytes) return new DecodeResult(DecodeStatus.TooLarge, null, null);
        try
        {
            using var doc = JsonDocument.Parse(utf8.ToArray());
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("t", out var t) || t.ValueKind != JsonValueKind.String)
                return new DecodeResult(DecodeStatus.InvalidJson, null, null);
            var name = t.GetString()!;
            if (!ClientDecoders.TryGetValue(name, out var decode)) return new DecodeResult(DecodeStatus.UnknownType, name, null);
            var d = root.TryGetProperty("d", out var dEl) ? dEl : default;
            if (d.ValueKind is not JsonValueKind.Object) d = JsonDocument.Parse("{}").RootElement;
            var msg = decode(d);
            return msg is null ? new DecodeResult(DecodeStatus.InvalidPayload, name, null) : new DecodeResult(DecodeStatus.Ok, name, msg);
        }
        catch (JsonException)
        {
            return new DecodeResult(DecodeStatus.InvalidJson, null, null);
        }
    }

    public static DecodeResult Decode(string json) => Decode(System.Text.Encoding.UTF8.GetBytes(json));

    private static void Register<T>(string name) where T : class, IClientMessage
    {
        var info = (JsonTypeInfo<T>)ProtocolJsonContext.Default.GetTypeInfo(typeof(T))!;
        ClientDecoders[name] = d =>
        {
            try { return d.Deserialize(info); }
            catch (JsonException) { return null; }
        };
        ClientNames[typeof(T)] = name;
    }

    private static void RegisterServer<T>(string name) where T : class, IServerMessage
    {
        var info = (JsonTypeInfo<T>)ProtocolJsonContext.Default.GetTypeInfo(typeof(T))!;
        ServerEncoders[typeof(T)] = (name, (writer, msg) => JsonSerializer.Serialize(writer, (T)msg, info));
    }
}
