using PixelRealms.Game.Core;

namespace PixelRealms.Game.Tests.Helpers;

/// <summary>Avanza N ticks de una simulación de prueba y devuelve todos los eventos emitidos.</summary>
public static class TickRunner
{
    public static List<IGameEvent> Run(Simulation sim, int ticks)
    {
        var all = new List<IGameEvent>();
        for (var i = 0; i < ticks; i++)
        {
            sim.RunTick();
            all.AddRange(sim.Context.Events);
        }
        return all;
    }

    public static List<IGameEvent> Run(TestWorld w, int ticks) => Run(w.Simulation, ticks);

    /// <summary>Avanza el tiempo dado en ms (redondeando hacia arriba a ticks completos).</summary>
    public static List<IGameEvent> RunMs(TestWorld w, int ms) => Run(w.Simulation, (int)Math.Ceiling(ms / (double)GameConstants.TickMs));
}
