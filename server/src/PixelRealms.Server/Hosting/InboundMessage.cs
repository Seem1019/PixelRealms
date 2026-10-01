using System.Threading.Channels;

namespace PixelRealms.Server.Hosting;

/// <summary>Mensaje recibido por una conexión, encolado hacia el tick (regla 2: la red nunca toca el mundo).</summary>
public readonly record struct InboundMessage(int ConnectionId, string Type, System.Text.Json.JsonElement Payload, InboundKind Kind = InboundKind.Message);

public enum InboundKind { Message, Connected, Disconnected }

/// <summary>Cola única de entrada al tick: bounded 10 000, descarta escrituras si se llena (y el productor lo loguea).</summary>
public static class InboundChannel
{
    public const int Capacity = 10_000;

    public static Channel<InboundMessage> Create() => Channel.CreateBounded<InboundMessage>(new BoundedChannelOptions(Capacity)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false,
    });
}
