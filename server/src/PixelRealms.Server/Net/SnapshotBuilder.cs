using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Interest;
using PixelRealms.Game.Movement;
using PixelRealms.Protocol.Messages;
using PixelRealms.Server.Players;

namespace PixelRealms.Server.Net;

/// <summary>
/// Paso 10 del tick: cada 2 ticks (10 Hz) construye un Snapshot por jugador conectado con su estado autoritativo (`self`,
/// `ackSeq`) y las entidades que ve (HU-021 CA6, HU-023). Las posiciones salen en píxeles (1 casilla = 16 px).
/// </summary>
public sealed class SnapshotBuilder(World world, PlayerRegistry players, ConnectionManager connections, InterestSystem interest, MovementSystem movement)
{
    private readonly List<EntStateDto> _ents = new(128);

    public void OnPostTick(TickContext ctx)
    {
        if (ctx.Tick % GameConstants.SnapshotEveryTicks != 0) return;
        foreach (var player in players.All)
        {
            if (player.ConnectionId < 0) continue;
            var map = world.GetInstance(player.MapInstanceId);
            if (map is null) continue;
            _ents.Clear();
            foreach (var id in interest.VisibleTo(map, player))
                if (map.Actors.TryGetValue(id, out var a)) _ents.Add(ToEntState(a));
            var speed = movement.SpeedOf(player, ctx);
            var self = new SnapshotSelfDto(Px(player.Position.X), Px(player.Position.Y), speed, player.Hp, player.MaxHp, player.Resource, player.MaxResource);
            connections.Send(player.ConnectionId, new Snapshot(ctx.Tick, player.LastInputSeq, self, _ents.ToList()));
        }
    }

    public static EntStateDto ToEntState(Actor a) => new(a.Id.Value, Px(a.Position.X), Px(a.Position.Y), a.Facing.ToWire(), HpPct(a), AnimOf(a), null);

    public static EntitySpawn ToSpawn(Actor a) => new(a.Id.Value, a.Kind switch { ActorKind.Player => "player", ActorKind.Monster => "monster", _ => "npc" },
        a is Monster m ? m.TemplateId : a is Player p ? p.ClassId : a.Name, a.Name, Px(a.Position.X), Px(a.Position.Y), a.Facing.ToWire(), a.Level,
        a is Player pl ? pl.ClassId : null, HpPct(a), a.IsDead ? 2 : 0);

    public static float Px(float tiles) => MathF.Round(tiles * GameConstants.PixelsPerTile, 2);

    private static int HpPct(Actor a) => a.MaxHp <= 0 ? 0 : (int)Math.Round(100.0 * a.Hp / a.MaxHp);

    private static string AnimOf(Actor a) => a.IsDead ? "dead" : a is Player p && (p.MoveDx != 0 || p.MoveDy != 0) ? "walk" : "idle";
}
