using PixelRealms.Content.Defs;

namespace PixelRealms.Game.Core;

/// <summary>Evento producido por un sistema durante el tick; el servidor lo traduce a mensajes para los observadores (AOI).</summary>
public interface IGameEvent
{
    /// <summary>Instancia de mapa donde ocurrió (id interno, distinto de mapId).</summary>
    int MapInstanceId { get; }
}

/// <summary>Contexto de un tick: tiempo, eventos emitidos y servicios inyectados que los sistemas necesitan.</summary>
public sealed class TickContext(IRules rulesSource, IRng rng)
{
    public long Tick { get; set; }

    public long NowMs { get; set; }

    public int DeltaMs { get; set; } = GameConstants.TickMs;

    public List<IGameEvent> Events { get; } = new(256);

    public IRules Rules { get; } = rulesSource;

    public IRng Rng { get; } = rng;

    public void Emit(IGameEvent e) => Events.Add(e);

    public void BeginTick(long tick, long nowMs)
    {
        Tick = tick;
        NowMs = nowMs;
        Events.Clear();
    }
}
