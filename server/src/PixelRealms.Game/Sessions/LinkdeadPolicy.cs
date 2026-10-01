using PixelRealms.Content.Defs;
using PixelRealms.Game.Entities;

namespace PixelRealms.Game.Sessions;

/// <summary>
/// Regla de linkdead (HU-025 CA1, docs/architecture.md §4): un jugador sin conexión sigue en el mundo, quieto,
/// `rules.combat.linkdeadSec`; si está en combate sigue hasta salir de combate, con tope `linkdeadInCombatMaxSec`.
/// Puro: decide, no actúa.
/// </summary>
public static class LinkdeadPolicy
{
    public static bool ShouldRemove(Player player, long nowMs, IRules rules)
    {
        if (!player.IsLinkdead) return false;
        var elapsed = nowMs - player.LinkdeadSinceMs;
        if (elapsed >= (long)(rules.Combat.LinkdeadInCombatMaxSec * 1000)) return true;
        return elapsed >= (long)(rules.Combat.LinkdeadSec * 1000) && !player.IsInCombat(nowMs, rules.Combat.InCombatWindowSec);
    }

    /// <summary>Marca la pérdida de conexión: el jugador se detiene y empieza a contar.</summary>
    public static void MarkLinkdead(Player player, long nowMs)
    {
        player.ConnectionId = -1;
        player.LinkdeadSinceMs = nowMs;
        player.MoveDx = 0;
        player.MoveDy = 0;
    }

    public static void MarkReconnected(Player player, int connectionId)
    {
        player.ConnectionId = connectionId;
        player.LinkdeadSinceMs = -1;
    }
}
