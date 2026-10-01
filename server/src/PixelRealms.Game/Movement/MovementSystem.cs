using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Movement;

/// <summary>
/// Paso 3 del tick (HU-021): aplica el último input de cada jugador con <see cref="MovementStep"/>. Si no llegó input en
/// `InputTimeoutMs` el jugador se detiene (CA5). La velocidad efectiva la da <see cref="SpeedOf"/> (base × modificadores:
/// casteo, auras; se amplía en M2).
/// </summary>
public sealed class MovementSystem : IMapSystem
{
    public const int InputTimeoutMs = 500;

    public string Name => "movement";

    /// <summary>Multiplicador de velocidad de un actor (1.0 por defecto). Lo rellenan casteo y auras (ADR-019, HU-035).</summary>
    public Func<Actor, TickContext, float> SpeedMultiplier { get; set; } = static (_, _) => 1f;

    /// <summary>¿El actor no puede moverse (muerto, aturdido, enraizado)? Lo rellena el sistema de auras (HU-035).</summary>
    public Func<Actor, bool> IsImmobilized { get; set; } = static a => a.IsDead;

    public void Tick(MapInstance map, TickContext ctx)
    {
        foreach (var player in map.Players.Values)
        {
            if (player.MoveDx == 0 && player.MoveDy == 0) continue;
            if (ctx.NowMs - player.LastInputAtMs > InputTimeoutMs) { player.MoveDx = 0; player.MoveDy = 0; continue; }
            if (IsImmobilized(player)) continue;
            Move(player, player.MoveDx, player.MoveDy, map.Data.Collision, SpeedOf(player, ctx));
        }
    }

    public float SpeedOf(Actor actor, TickContext ctx) => actor.BaseSpeed * SpeedMultiplier(actor, ctx);

    /// <summary>Mueve un actor un tick en la dirección dada (en píxeles internamente; la posición se guarda en casillas).</summary>
    public static void Move(Actor actor, int dx, int dy, CollisionGrid grid, float speedTilesPerSec)
    {
        var px = MovementStep.Step(actor.Position.X * MovementStep.TileSize, actor.Position.Y * MovementStep.TileSize, dx, dy, speedTilesPerSec, grid);
        actor.Position = new Vec2((float)(px.X / MovementStep.TileSize), (float)(px.Y / MovementStep.TileSize));
        if (DirectionExtensions.FromInput(dx, dy) is { } facing) actor.Facing = facing;
        if (actor is Player p) p.Dirty = true;
    }
}
