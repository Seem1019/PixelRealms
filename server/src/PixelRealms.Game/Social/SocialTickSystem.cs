using PixelRealms.Game.Core;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Social;

/// <summary>Avanza duelos (caducidad, cuenta atrás, distancia) e intercambios (caducidad, distancia, muerte, desconexión) por instancia.</summary>
public sealed class SocialTickSystem(PvpService pvp, TradeService trades) : IMapSystem
{
    public string Name => "social";

    public void Tick(MapInstance map, TickContext ctx)
    {
        pvp.Tick(map, ctx);
        trades.Tick(map, ctx);
    }
}
