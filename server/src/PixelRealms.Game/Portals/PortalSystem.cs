using PixelRealms.Content.Defs;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Portals;

/// <summary>Un jugador cruzó un portal: el servidor lo mueve de instancia y envía ChangeMap (HU-027 CA1).</summary>
public sealed record PortalUsed(int MapInstanceId, Player Player, PortalDef Portal) : IGameEvent;

/// <summary>El portal se rechazó (en combate, muerto, nivel insuficiente, paso cerrado): el servidor envía Error (HU-027 CA3/CA4, HU-112).</summary>
public sealed record PortalRejected(int MapInstanceId, Player Player, PortalDef Portal, string ErrorCode) : IGameEvent;

/// <summary>Reglas puras de uso de un portal (HU-027).</summary>
public static class PortalPolicy
{
    public const string InCombat = "in_combat";
    public const string IsDead = "is_dead";
    public const string LevelTooLow = "level_too_low";
    public const string OutOfRange = "out_of_range";
    public const string PortalLocked = "portal_locked";

    /// <summary>
    /// ¿Está abierto el paso? (HU-112) Cerrado si la fase activa no llega a su `minPhase` o si su mapa de destino no está cargado
    /// (la salida a un tier que aún no existe, HU-115), aunque la fase ya lo permita.
    /// </summary>
    public static bool IsOpen(PortalDef portal, IRules rules, bool targetLoaded) =>
        targetLoaded && (portal.MinPhase is not { } phase || rules.World.CurrentPhase >= phase);

    /// <summary>
    /// Código de error o null si puede cruzar. El paso cerrado va primero: con el derrumbe delante, avisar de que estás en combate
    /// o de que te falta nivel daría a entender que luego se podría cruzar.
    /// </summary>
    public static string? Check(Player player, PortalDef portal, long nowMs, IRules rules, bool targetLoaded)
    {
        if (!IsOpen(portal, rules, targetLoaded)) return PortalLocked;
        if (player.IsDead) return IsDead;
        if (player.IsInCombat(nowMs, rules.Combat.InCombatWindowSec)) return InCombat;
        if (portal.MinLevel is { } min && player.Level < min) return LevelTooLow;
        return null;
    }

    /// <summary>Distancia (casillas) de un punto al rectángulo del portal; 0 si está dentro.</summary>
    public static float DistanceTo(PortalDef portal, Vec2 p)
    {
        var dx = MathF.Max(0f, MathF.Max(portal.Position.X - p.X, p.X - (portal.Position.X + portal.Size.X)));
        var dy = MathF.Max(0f, MathF.Max(portal.Position.Y - p.Y, p.Y - (portal.Position.Y + portal.Size.Y)));
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>
/// Paso del tick posterior al movimiento: detecta jugadores sobre un portal (o con `UsePortal` pendiente a ≤ 1 casilla) y emite
/// <see cref="PortalUsed"/> o <see cref="PortalRejected"/>. Un rechazo no se repite hasta que el jugador sale del portal
/// (evita spam de errores cada tick). El cambio de instancia lo hace el servidor con el evento.
/// </summary>
/// <param name="isMapLoaded">¿Hay instancia del mapa de destino? Sin ella el portal está cerrado (HU-112); null = todos cargados.</param>
public sealed class PortalSystem(Func<string, bool>? isMapLoaded = null) : IMapSystem
{
    public string Name => "portals";

    public void Tick(MapInstance map, TickContext ctx)
    {
        if (map.Data.Portals.Count == 0) return;
        foreach (var player in map.Players.Values)
        {
            // En pleno salto (HU-087) no se cruza: el vuelo lleva coordenadas de este mapa. Se evalúa al aterrizar.
            if (player.Combat.Flight is not null) continue;
            PortalDef? portal = null;
            if (player.RequestedPortalId is { } requested)
            {
                player.RequestedPortalId = null;
                PortalDef? p = null;
                foreach (var x in map.Data.Portals) if (x.PortalId == requested) { p = x; break; }
                if (p is null) { ctx.Emit(new PortalRejected(map.Id, player, new PortalDef(requested, "", 0, 0, null, Vec2.Zero, Vec2.Zero), "not_found")); continue; }
                if (PortalPolicy.DistanceTo(p, player.Position) > ctx.Rules.Movement.PortalUseRangeTiles) { ctx.Emit(new PortalRejected(map.Id, player, p, PortalPolicy.OutOfRange)); continue; }
                portal = p;
            }
            else
            {
                foreach (var x in map.Data.Portals) if (x.Contains(player.Position)) { portal = x; break; }
                if (portal is null) { player.RejectedPortalId = null; continue; }
                if (player.RejectedPortalId == portal.PortalId) continue;
            }

            var error = PortalPolicy.Check(player, portal, ctx.NowMs, ctx.Rules, isMapLoaded?.Invoke(portal.TargetMapId) ?? true);
            if (error is null) ctx.Emit(new PortalUsed(map.Id, player, portal));
            else { player.RejectedPortalId = portal.PortalId; ctx.Emit(new PortalRejected(map.Id, player, portal, error)); }
        }
    }
}
