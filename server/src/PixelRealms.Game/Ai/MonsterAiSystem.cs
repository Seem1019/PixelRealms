using PixelRealms.Content.Defs;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Map;
using PixelRealms.Game.Movement;

namespace PixelRealms.Game.Ai;

/// <summary>
/// Paso 6 del tick (HU-031 CA2, HU-036): `Idle` (patrulla en `wanderRadius` con pausas de 2–6 s) → aggro por percepción
/// (`aggroRange` con LOS; 0 = solo si le pegan) → `Chase` (A*, recalcula cada 500 ms o si el objetivo se mueve > 2 casillas)
/// → `Attack` (hechizos listos de `spells[]` o básico) → `Evade` si se aleja > `leashRange` del spawn (inmune, ×evadeSpeedMult,
/// vida completa y amenaza limpia al llegar). Cambio de objetivo por amenaza con la regla 110 %/130 %.
/// </summary>
public sealed class MonsterAiSystem(CombatServices services, CastSystem casts, AuraSystem auras, MovementSystem movement) : IMapSystem
{
    // Ritmos de la IA (percepción, A*, patrulla, llegada): `rules.ai` (regla 4, ADR-008).

    private readonly List<Monster> _monsters = new(128);
    private readonly List<EntityId> _threatIds = new(8);
    private readonly List<Actor> _candidates = new(8);

    public string Name => "monster_ai";

    /// <summary>¿El jugador puede ser objetivo de monstruos? (duelistas no, HU-036 CA5c). Por defecto, todo jugador vivo.</summary>
    public Func<Player, bool> CanBeAggroed { get; set; } = static p => !p.IsDead;

    public void Tick(MapInstance map, TickContext ctx)
    {
        _monsters.Clear();
        foreach (var m in map.Monsters.Values) if (m.IsAlive) _monsters.Add(m);
        foreach (var m in _monsters)
        {
            if (m.Combat.Evading) { Evade(m, map, ctx); continue; }
            switch (m.Brain.State)
            {
                case AiState.Idle:
                    Idle(m, map, ctx);
                    break;
                case AiState.Chase:
                case AiState.Attack:
                    Engage(m, map, ctx);
                    break;
                default:
                    break;
            }
        }
    }

    // ---- Idle ---------------------------------------------------------------------------------------------------------

    private void Idle(Monster m, MapInstance map, TickContext ctx)
    {
        var brain = m.Brain;
        if (m.Threat.Count > 0) { StartChase(m); return; }
        if (ctx.NowMs >= brain.NextPerceptionAtMs)
        {
            brain.NextPerceptionAtMs = ctx.NowMs + ctx.Rules.Ai.PerceptionMs;
            if (m.Template.AggroRange > 0 && Perceive(m, map) is { } target)
            {
                m.Threat.Add(target.Id, 0);
                StartChase(m);
                return;
            }
        }
        Wander(m, map, ctx);
    }

    private Player? Perceive(Monster m, MapInstance map)
    {
        Player? best = null;
        var bestD = float.MaxValue;
        var rangeSq = (float)(m.Template.AggroRange * m.Template.AggroRange);
        foreach (var p in map.Players.Values)
        {
            if (p.IsDead || !CanBeAggroed(p)) continue;
            var d = Vec2.DistanceSquared(p.Position, m.Position);
            if (d > rangeSq || d >= bestD) continue;
            if (!LineOfSight.Has(map.Collision, m.Position, p.Position)) continue;
            best = p; bestD = d;
        }
        return best;
    }

    private void Wander(Monster m, MapInstance map, TickContext ctx)
    {
        var brain = m.Brain;
        if (brain.WanderTarget is null)
        {
            if (ctx.NowMs < brain.WanderPauseUntilMs || m.WanderRadius <= 0) return;
            // HU-031 CA2: un punto del círculo de radio `wanderRadius` (antes un cuadrado: hasta √2·r del spawn). La raíz del
            // radio reparte los puntos uniformemente por el área.
            var angle = ctx.Rng.NextDouble() * Math.Tau;
            var dist = Math.Sqrt(ctx.Rng.NextDouble()) * m.WanderRadius;
            var candidate = new Vec2(m.SpawnPosition.X + (float)(Math.Cos(angle) * dist), m.SpawnPosition.Y + (float)(Math.Sin(angle) * dist));
            if (map.Collision.IsSolidAt(candidate.X, candidate.Y)) { brain.WanderPauseUntilMs = ctx.NowMs + ctx.Rules.Ai.WanderPauseMinMs; return; }
            brain.WanderTarget = candidate;
        }
        if (StepTowards(m, brain.WanderTarget.Value, map, ctx, speedMult: (float)ctx.Rules.Ai.WanderSpeedMult))
        {
            brain.WanderTarget = null;
            brain.WanderPauseUntilMs = ctx.NowMs + ctx.Rng.Next(ctx.Rules.Ai.WanderPauseMinMs, ctx.Rules.Ai.WanderPauseMaxMs + 1);
        }
    }

    // ---- Chase / Attack -------------------------------------------------------------------------------------------------

    private static void StartChase(Monster m)
    {
        m.Brain.State = AiState.Chase;
        m.Brain.WanderTarget = null;
        m.Brain.ClearPath();
    }

    private void Engage(Monster m, MapInstance map, TickContext ctx)
    {
        var brain = m.Brain;
        var rules = ctx.Rules.Combat;

        // Leash: demasiado lejos del spawn → evadir.
        if (Vec2.Distance(m.Position, m.SpawnPosition) > m.Template.LeashRange) { BeginEvade(m, map, ctx); return; }

        var switchMult = m.Template.AttackRange > ctx.Rules.Ai.RangedThreatThresholdTiles ? rules.ThreatSwitchRanged : rules.ThreatSwitchMelee;
        var targetId = m.Threat.Reevaluate(ctx.NowMs, switchMult, (this, m, map), static (id, s) => s.map.Find(id) is Player p && p.IsAlive && s.Item1.CanBeAggroed(p) && Vec2.Distance(p.Position, s.m.Position) <= s.m.Template.LeashRange * 2);
        var target = targetId is { } tid ? map.Find(tid) : null;
        if (target is null)
        {
            m.Threat.Clear();
            m.Combat.ResetTransient();
            brain.State = AiState.Idle;
            brain.ClearPath();
            return;
        }
        m.Combat.TargetId = target.Id;

        var dist = Vec2.Distance(m.Position, target.Position);
        var inRange = dist <= m.Template.AttackRange && LineOfSight.Has(map.Collision, m.Position, target.Position);
        if (inRange)
        {
            brain.State = AiState.Attack;
            brain.ClearPath();
            FaceTowards(m, target.Position);
            if (!m.Combat.IsCasting && !m.Auras.IsStunned && TryCastSpell(m, target, map, ctx)) return;
            m.Combat.AutoAttackOn = true;
            return;
        }

        brain.State = AiState.Chase;
        m.Combat.AutoAttackOn = true; // el básico se pausa solo fuera de alcance; sigue listo al llegar
        if (m.Combat.IsCasting || m.Auras.IsStunned || m.Auras.IsRooted) return;
        if (!EnsurePath(m, target.Position, map, ctx)) { BeginEvade(m, map, ctx); return; }
        FollowPath(m, map, ctx);
    }

    private bool TryCastSpell(Monster m, Actor current, MapInstance map, TickContext ctx)
    {
        if (m.Template.Spells.Count == 0) return false;
        var db = services.Content;
        foreach (var ms in m.Template.Spells)
        {
            if (m.Combat.IsOnCooldown(ms.SpellId, ctx.NowMs)) continue;
            if (ms.HpBelowPct < 1.0 && m.MaxHp > 0 && (double)m.Hp / m.MaxHp > ms.HpBelowPct) continue;
            if (!db.TryGetSpell(ms.SpellId, out var spell) || spell is null) continue;
            Actor target = ms.Target switch
            {
                MonsterSpellTarget.Self => m,
                MonsterSpellTarget.RandomNotTopThreat => RandomNotTop(m, current, map, ctx),
                _ => current,
            };
            Vec2? targetPos = spell.Targeting.IsGround() ? target.Position : null;
            if (casts.TryBeginCast(m, spell, target.Id, targetPos, map, ctx) is null) return true;
        }
        return false;
    }

    private Actor RandomNotTop(Monster m, Actor current, MapInstance map, TickContext ctx)
    {
        m.Threat.AllButTop(_threatIds);
        _candidates.Clear();
        foreach (var id in _threatIds) if (map.Find(id) is { IsAlive: true } a) _candidates.Add(a);
        return _candidates.Count == 0 ? current : _candidates[ctx.Rng.Next(0, _candidates.Count)];
    }

    private static bool EnsurePath(Monster m, Vec2 targetPos, MapInstance map, TickContext ctx)
    {
        var brain = m.Brain;
        var stale = brain.PathComputedAtMs == long.MinValue || ctx.NowMs - brain.PathComputedAtMs >= ctx.Rules.Ai.PathRecalcMs
                    || Vec2.Distance(brain.PathTargetPos, targetPos) > ctx.Rules.Ai.PathRecalcMovedTiles || brain.PathIndex >= brain.Path.Count;
        if (!stale) return true;
        if (!Pathfinder.FindPath(map.Collision, m.Position, targetPos, brain.Path, ctx.Rules.Ai.PathMaxNodes)) return false;
        brain.PathIndex = 0;
        brain.PathComputedAtMs = ctx.NowMs;
        brain.PathTargetPos = targetPos;
        // Último tramo: directo al objetivo (los centros de casilla se quedan cortos en el alcance melee).
        brain.Path.Add(targetPos);
        return true;
    }

    private void FollowPath(Monster m, MapInstance map, TickContext ctx)
    {
        var brain = m.Brain;
        while (brain.PathIndex < brain.Path.Count)
        {
            var waypoint = brain.Path[brain.PathIndex];
            if (Vec2.Distance(m.Position, waypoint) <= ctx.Rules.Ai.ArriveToleranceTiles) { brain.PathIndex++; continue; }
            StepTowards(m, waypoint, map, ctx, 1f);
            return;
        }
    }

    // ---- Evade ----------------------------------------------------------------------------------------------------------

    private void BeginEvade(Monster m, MapInstance map, TickContext ctx)
    {
        if (m.Combat.Cast is not null) casts.Cancel(m, map, ctx);
        m.Combat.Evading = true;
        m.Combat.ResetTransient();
        m.Threat.Clear();
        m.Brain.State = AiState.Evade;
        m.Brain.ClearPath();
    }

    private void Evade(Monster m, MapInstance map, TickContext ctx)
    {
        if (Vec2.Distance(m.Position, m.SpawnPosition) <= ctx.Rules.Ai.ArriveToleranceTiles || StepTowards(m, m.SpawnPosition, map, ctx, (float)ctx.Rules.Combat.EvadeSpeedMult, ignoreControl: true))
        {
            m.Position = m.SpawnPosition;
            m.Combat.Evading = false;
            m.Hp = m.MaxHp;
            m.Threat.Clear();
            m.TaggedBy = null;
            auras.ClearAll(m, map, ctx);
            m.LastCombatAtMs = long.MinValue;
            m.Brain.State = AiState.Idle;
            m.Brain.WanderPauseUntilMs = ctx.NowMs + ctx.Rules.Ai.WanderPauseMinMs;
        }
    }

    // ---- Movimiento -----------------------------------------------------------------------------------------------------

    /// <summary>Un tick hacia `target` en 8 direcciones con MovementStep; devuelve true si llegó.</summary>
    private bool StepTowards(Monster m, Vec2 target, MapInstance map, TickContext ctx, float speedMult, bool ignoreControl = false)
    {
        var delta = target - m.Position;
        if (delta.Length <= ctx.Rules.Ai.ArriveToleranceTiles) return true;
        if (!ignoreControl && (m.Auras.IsStunned || m.Auras.IsRooted)) return false;
        var dx = MathF.Abs(delta.X) < 0.1f ? 0 : Math.Sign(delta.X);
        var dy = MathF.Abs(delta.Y) < 0.1f ? 0 : Math.Sign(delta.Y);
        if (dx == 0 && dy == 0) return true;
        var speed = movement.SpeedOf(m, ctx) * speedMult;
        // No pasarse del destino en este tick.
        var stepTiles = speed * ctx.DeltaMs / 1000f;
        var before = m.Position;
        MovementSystem.Move(m, dx, dy, map.Collision, speed);
        if (delta.Length <= stepTiles) { m.Position = target; }
        return Vec2.Distance(m.Position, target) <= ctx.Rules.Ai.ArriveToleranceTiles || m.Position == before;
    }

    private static void FaceTowards(Monster m, Vec2 target)
    {
        var delta = target - m.Position;
        var dx = MathF.Abs(delta.X) < 0.1f ? 0 : Math.Sign(delta.X);
        var dy = MathF.Abs(delta.Y) < 0.1f ? 0 : Math.Sign(delta.Y);
        if (DirectionExtensions.FromInput(dx, dy) is { } f) m.Facing = f;
    }
}
