using PixelRealms.Content.Defs;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Social;

public enum DuelState { Requested, Countdown, Active, Ended }

/// <summary>Duelo entre dos jugadores de una instancia (ADR-011, HU-064).</summary>
public sealed class DuelSession(Player a, Player b, string ruleset, long requestedAtMs)
{
    public Player A { get; } = a;
    public Player B { get; } = b;
    public string Ruleset { get; } = ruleset;
    public DuelState State { get; set; } = DuelState.Requested;
    public long RequestedAtMs { get; } = requestedAtMs;
    public long StartsAtMs { get; set; }
    public Player? Winner { get; set; }
    /// <summary>Punto medio al activarse: quien se aleja más de él pierde por distancia.</summary>
    public Vec2 Center { get; set; }

    /// <summary>Vida y recurso de cada uno al activarse: al terminar vuelven a esto, no al máximo (el duelo no cura ni hiere).</summary>
    public (int Hp, int Resource) StartA { get; set; }

    public (int Hp, int Resource) StartB { get; set; }

    public bool Involves(Player p) => ReferenceEquals(p, A) || ReferenceEquals(p, B);
    public Player Opponent(Player p) => ReferenceEquals(p, A) ? B : A;
}

/// <summary>Cambio de estado de un duelo: el servidor envía DuelUpdate a ambos (y anuncia el final en `say`).</summary>
public sealed record DuelChangedEvent(int MapInstanceId, DuelSession Duel, string State, string? Reason) : IGameEvent;

/// <summary>
/// Única puerta del PvP (skill combat-system): `CanAttack(a, b)` devuelve el ruleset aplicable o null. En el MVP solo `duel`:
/// solicitud (caduca `requestExpireSec`), cuenta atrás `countdownSec`, activo; termina al bajar a `endAtHpPct` (nadie muere), al
/// alejarse > `maxDistanceTiles`, desconectarse, cambiar de mapa o rendirse. Los duelistas no generan aggro ni amenaza.
/// </summary>
public sealed class PvpService(AuraSystem auras)
{
    private readonly Dictionary<int, List<DuelSession>> _duels = new();

    public DuelSession? DuelOf(Player p)
    {
        if (!_duels.TryGetValue(p.MapInstanceId, out var list)) return null;
        foreach (var d in list) if (d.State != DuelState.Ended && d.Involves(p)) return d;
        return null;
    }

    public bool InActiveDuel(Player p) => DuelOf(p) is { State: DuelState.Active };

    /// <summary>¿El jugador tiene un intercambio pedido o abierto? No se reta a duelo en pleno intercambio (lo rellena CombatModule).</summary>
    public Func<Player, bool> InTrade { get; set; } = static _ => false;

    /// <summary>Ruleset aplicable si `a` puede atacar a `b`, o null (HU-064 CA7).</summary>
    public PvpRuleset? CanAttack(Player a, Player b, IRules rules)
    {
        if (ReferenceEquals(a, b)) return null;
        var duel = DuelOf(a);
        if (duel is null || duel.State != DuelState.Active || !ReferenceEquals(duel.Opponent(a), b)) return null;
        if (!rules.Pvp.EnabledRulesets.Contains(duel.Ruleset)) return null;
        return rules.Pvp.Rulesets.GetValueOrDefault(duel.Ruleset);
    }

    public string? Request(Player from, Player to, MapInstance map, TickContext ctx)
    {
        var rules = ctx.Rules.Pvp;
        var ruleset = rules.DefaultRuleset;
        if (!rules.EnabledRulesets.Contains(ruleset) || !rules.Rulesets.TryGetValue(ruleset, out var rs)) return "pvp_not_allowed";
        if (ReferenceEquals(from, to) || from.MapInstanceId != to.MapInstanceId) return "invalid_target";
        if (from.IsDead || to.IsDead) return "is_dead";
        // El final del duelo restaura vida y recurso: retar en pleno combate sería una cura completa (y los monstruos sueltan a
        // los duelistas). Solo se reta fuera de combate.
        if (InCombat(from, ctx) || InCombat(to, ctx)) return "in_combat";
        if (DuelOf(from) is not null || DuelOf(to) is not null) return "duel_busy";
        if (InTrade(from) || InTrade(to)) return "trade_busy";
        if (!rs.AllowedInSafeZones && (map.Data.IsSafeZone(from.Position) || map.Data.IsSafeZone(to.Position))) return "pvp_not_allowed";
        if (Vec2.Distance(from.Position, to.Position) > rs.MaxDistanceTiles) return "out_of_range";
        var duel = new DuelSession(from, to, ruleset, ctx.NowMs);
        if (!_duels.TryGetValue(map.Id, out var list)) _duels[map.Id] = list = new List<DuelSession>();
        list.Add(duel);
        ctx.Emit(new DuelChangedEvent(map.Id, duel, "requested", null));
        return null;
    }

    public string? Respond(Player target, bool accept, MapInstance map, TickContext ctx)
    {
        var duel = DuelOf(target);
        if (duel is null || duel.State != DuelState.Requested || !ReferenceEquals(duel.B, target)) return "not_found";
        var rs = ctx.Rules.Pvp.Rulesets[duel.Ruleset];
        if (!accept)
        {
            duel.State = DuelState.Ended;
            ctx.Emit(new DuelChangedEvent(map.Id, duel, "declined", null));
            Remove(map, duel);
            return null;
        }
        // Entre el reto y la respuesta pueden pasar 30 s: muerto, en otro mapa, comerciando, en combate o lejos ya no vale (al
        // terminar el duelo se restauraría a un muerto o a alguien a mitad de una pelea, o el duelo seguiría en otro mapa).
        var invalid = duel.A.IsDead || duel.B.IsDead ? "is_dead"
            : duel.A.MapInstanceId != duel.B.MapInstanceId ? "invalid_target"
            : InTrade(duel.A) || InTrade(duel.B) ? "trade_busy"
            : InCombat(duel.A, ctx) || InCombat(duel.B, ctx) ? "in_combat"
            : Vec2.Distance(duel.A.Position, duel.B.Position) > rs.MaxDistanceTiles ? "out_of_range"
            : !rs.AllowedInSafeZones && (map.Data.IsSafeZone(duel.A.Position) || map.Data.IsSafeZone(duel.B.Position)) ? "pvp_not_allowed"
            : null;
        if (invalid is not null)
        {
            Cancel(duel, invalid, map, ctx);
            return invalid;
        }
        duel.State = DuelState.Countdown;
        duel.StartsAtMs = ctx.NowMs + (long)(rs.CountdownSec * 1000);
        ctx.Emit(new DuelChangedEvent(map.Id, duel, "countdown", null));
        return null;
    }

    public string? Forfeit(Player p, MapInstance map, TickContext ctx)
    {
        var duel = DuelOf(p);
        if (duel is null) return "not_found";
        // Antes de que empiece no hay nada que ganar ni que restaurar: rendirse retira el reto (retar y rendirse curaba al 100 %).
        if (duel.State != DuelState.Active) { Cancel(duel, "forfeit", map, ctx); return null; }
        End(duel, duel.Opponent(p), "forfeit", map, ctx);
        return null;
    }

    /// <summary>Desconexión, portal, muerte fuera del duelo: pierde. Si aún no había empezado, el reto se retira sin restaurar.</summary>
    public void Abandon(Player p, string reason, MapInstance map, TickContext ctx)
    {
        var duel = DuelOf(p);
        if (duel is null) return;
        if (duel.State != DuelState.Active) { Cancel(duel, reason, map, ctx); return; }
        End(duel, duel.Opponent(p), reason, map, ctx);
    }

    /// <summary>Recorte de daño en duelo (regla de oro 7): nunca por debajo de `endAtHpPct`; al tocar el umbral termina.</summary>
    public int ClampDamage(Actor source, Actor target, int dmg, MapInstance map, TickContext ctx)
    {
        if (target is not Player tp || source is not Player sp) return dmg;
        var duel = DuelOf(tp);
        if (duel is null || duel.State != DuelState.Active) return dmg;
        // Un DoT cuyo lanzador ya salió del mapa llega con la víctima como origen (AuraSystem): cuenta como daño del rival, no
        // como una victoria de la víctima sobre sí misma.
        if (ReferenceEquals(sp, tp)) sp = duel.Opponent(tp);
        if (!duel.Involves(sp)) return dmg;
        var rs = ctx.Rules.Pvp.Rulesets[duel.Ruleset];
        var floor = Math.Max(1, (int)Math.Ceiling(tp.MaxHp * rs.EndAtHpPct));
        var allowed = Math.Max(0, tp.Hp - floor);
        var applied = Math.Min(dmg, allowed);
        if (tp.Hp - applied <= floor) End(duel, sp, "hp", map, ctx, applyAfterDamage: applied);
        return applied;
    }

    private void End(DuelSession duel, Player winner, string reason, MapInstance map, TickContext ctx, int applyAfterDamage = 0)
    {
        if (duel.State == DuelState.Ended) return;
        duel.State = DuelState.Ended;
        duel.Winner = winner;
        var rs = ctx.Rules.Pvp.Rulesets[duel.Ruleset];
        foreach (var p in new[] { duel.A, duel.B })
        {
            if (p.IsDead) continue; // un muerto no resucita por terminar el duelo: pasa por Respawn como siempre
            // Nadie sigue atacando al ex-rival, y lo que le puso (un DoT, un aturdimiento) se va: sin el recorte del duelo lo mataría.
            var opponentId = duel.Opponent(p).Id;
            for (var i = p.Auras.All.Count - 1; i >= 0; i--)
                if (i < p.Auras.All.Count && p.Auras.All[i].CasterId == opponentId) auras.Remove(p, p.Auras.All[i], map, ctx);
            p.Combat.ResetTransient();
            p.Dirty = true;
            if (rs.RestoreOnEnd)
            {
                // Vuelve a la vida y el recurso con que empezó (nunca al máximo: sería una posada gratis). El daño que termina el
                // duelo todavía no se ha restado: se compensa para que quede exactamente en `start`.
                var start = ReferenceEquals(p, duel.A) ? duel.StartA : duel.StartB;
                p.Hp = Math.Clamp(start.Hp, 1, p.MaxHp) + (ReferenceEquals(p, duel.Opponent(winner)) ? applyAfterDamage : 0);
                p.Resource = Math.Clamp(start.Resource, 0, p.MaxResource);
            }
            else if (!ReferenceEquals(p, winner) && reason == "hp" && rs.LoserRegenMult > 1)
            {
                // HU-064 CA3: cada uno se queda como acabó; quien perdió por vida (al `endAtHpPct`) recupera algo más rápido lo que el
                // duelo le quitó, no más: rendirse o alejarse no da nada y la recuperación no sirve para curar una pelea anterior.
                var start = ReferenceEquals(p, duel.A) ? duel.StartA : duel.StartB;
                p.Combat.Recovery = new PostDuelRecovery(ctx.NowMs, rs.LoserRegenMult, Math.Min(start.Hp, p.MaxHp));
            }
        }
        ctx.Emit(new DuelChangedEvent(map.Id, duel, "ended", reason));
        Remove(map, duel);
    }

    /// <summary>Retira un reto o una cuenta atrás: nadie gana y nadie se restaura.</summary>
    private void Cancel(DuelSession duel, string reason, MapInstance map, TickContext ctx)
    {
        duel.State = DuelState.Ended;
        ctx.Emit(new DuelChangedEvent(map.Id, duel, "declined", reason));
        Remove(map, duel);
    }

    private static bool InCombat(Player p, TickContext ctx) => p.IsInCombat(ctx.NowMs, ctx.Rules.Combat.InCombatWindowSec);

    private void Remove(MapInstance map, DuelSession duel)
    {
        if (_duels.TryGetValue(map.Id, out var list)) list.Remove(duel);
    }

    /// <summary>Caducidad de solicitudes, arranque tras la cuenta atrás y distancia máxima.</summary>
    public void Tick(MapInstance map, TickContext ctx)
    {
        if (!_duels.TryGetValue(map.Id, out var list) || list.Count == 0) return;
        foreach (var duel in list.ToList())
        {
            var rs = ctx.Rules.Pvp.Rulesets[duel.Ruleset];
            switch (duel.State)
            {
                case DuelState.Requested or DuelState.Countdown when duel.A.IsDead || duel.B.IsDead || duel.A.MapInstanceId != map.Id || duel.B.MapInstanceId != map.Id:
                    Cancel(duel, duel.A.IsDead || duel.B.IsDead ? "died" : "left_map", map, ctx);
                    break;
                case DuelState.Requested when ctx.NowMs - duel.RequestedAtMs > rs.RequestExpireSec * 1000:
                    Cancel(duel, "expired", map, ctx);
                    break;
                case DuelState.Countdown when InCombat(duel.A, ctx) || InCombat(duel.B, ctx):
                    Cancel(duel, "in_combat", map, ctx); // un monstruo los ataca durante la cuenta atrás
                    break;
                case DuelState.Countdown when ctx.NowMs >= duel.StartsAtMs:
                    duel.State = DuelState.Active;
                    duel.Center = (duel.A.Position + duel.B.Position) * 0.5f;
                    duel.StartA = (duel.A.Hp, duel.A.Resource);
                    duel.StartB = (duel.B.Hp, duel.B.Resource);
                    ctx.Emit(new DuelChangedEvent(map.Id, duel, "active", null));
                    break;
                case DuelState.Active:
                    if (duel.A.MapInstanceId != map.Id) { End(duel, duel.B, "left_map", map, ctx); break; }
                    if (duel.B.MapInstanceId != map.Id) { End(duel, duel.A, "left_map", map, ctx); break; }
                    if (duel.A.IsDead) { End(duel, duel.B, "died", map, ctx); break; }
                    if (duel.B.IsDead) { End(duel, duel.A, "died", map, ctx); break; }
                    // Lejos del rival o del punto de inicio: sin el segundo, los dos podían viajar juntos ignorados por los monstruos.
                    if (Vec2.Distance(duel.A.Position, duel.B.Position) > rs.MaxDistanceTiles
                        || Vec2.Distance(duel.A.Position, duel.Center) > rs.MaxDistanceTiles || Vec2.Distance(duel.B.Position, duel.Center) > rs.MaxDistanceTiles)
                    {
                        // Pierde quien se alejó: el más lejos del punto medio inicial.
                        var loser = Vec2.Distance(duel.A.Position, duel.Center) >= Vec2.Distance(duel.B.Position, duel.Center) ? duel.A : duel.B;
                        End(duel, duel.Opponent(loser), "distance", map, ctx);
                    }
                    break;
                default:
                    break;
            }
        }
    }
}
