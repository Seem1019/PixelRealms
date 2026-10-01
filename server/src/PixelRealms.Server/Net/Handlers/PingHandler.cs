using PixelRealms.Protocol.Messages;

namespace PixelRealms.Server.Net.Handlers;

/// <summary>HU-006 CA1: Ping → Pong{clientTime, serverTick}.</summary>
public sealed class PingHandler : IMessageHandler<Ping>
{
    public void Handle(Ping msg, HandlerContext ctx) => ctx.Send(new Pong(msg.ClientTime, ctx.Tick.Tick));
}
