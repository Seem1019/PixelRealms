using PixelRealms.Protocol.Messages;

namespace PixelRealms.Server.Net.Handlers;

/// <summary>HU-021 CA1/CA4: guarda el último input; dx/dy fuera de {−1,0,1} se recortan y un seq no creciente se descarta (log debug).</summary>
public sealed class MoveInputHandler(ILogger<MoveInputHandler> logger) : IMessageHandler<MoveInput>
{
    public void Handle(MoveInput msg, HandlerContext ctx)
    {
        var player = ctx.Player;
        if (player is null) return; // aún no entró al mundo: se ignora en silencio
        if (msg.Seq <= player.LastInputSeq)
        {
            logger.LogDebug("MoveInput descartado: seq {Seq} ≤ {Last} ({Name})", msg.Seq, player.LastInputSeq, player.Name);
            return;
        }
        var dx = Math.Clamp(msg.Dx, -1, 1);
        var dy = Math.Clamp(msg.Dy, -1, 1);
        if (dx != msg.Dx || dy != msg.Dy) logger.LogDebug("MoveInput recortado: ({Dx}, {Dy}) de {Name}", msg.Dx, msg.Dy, player.Name);
        player.LastInputSeq = msg.Seq;
        player.MoveDx = dx;
        player.MoveDy = dy;
        player.LastInputAtMs = ctx.Tick.NowMs;
        if (player.DebugMove)
        {
            logger.LogInformation("[debug move] {Name} seq {Seq} input ({Dx},{Dy}) pos ({X:F2},{Y:F2}) tick {Tick}", player.Name, msg.Seq, dx, dy, player.Position.X, player.Position.Y, ctx.Tick.Tick);
            ctx.Send(new Protocol.Messages.ChatMessage("system", "", $"[move] seq {msg.Seq} ({dx},{dy}) pos ({player.Position.X:F2},{player.Position.Y:F2})", ctx.Tick.NowMs));
        }
    }
}
