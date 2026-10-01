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
    // Ritmos técnicos de la IA (skill combat-system §IA). TODO(balance): candidatos a `rules.ai` si se quieren ajustar sin código.
    public const int PerceptionMs = 250;
    public const int PathRecalcMs = 500;
    public const float PathRecalcMovedTiles = 2f;
    public const int WanderPauseMinMs = 2000;
    public const int WanderPauseMaxMs = 6000;
    public const float ArriveTolerance = 0.25f;
    public const float RangedAttackRangeThreshold = 2f;

    private readonly List<Monster> _monsters = new(128);

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
            brain.NextPerceptionAtMs = ctx.NowMs + PerceptionMs;
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
            if (!LineOfSight.Has(map.Data.Collision, m.Position, p.Position)) continue;
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
            var r = m.WanderRadius;
            var candidate = new Vec2(m.SpawnPosition.X + (float)((ctx.Rng.NextDouble() * 2 - 1) * r), m.SpawnPosition.Y + (float)((ctx.Rng.NextDouble() * 2 - 1) * r));
            if (map.Data.Collision.IsSolidAt(candidate.X, candidate.Y)) { brain.WanderPauseUntilMs = ctx.NowMs + WanderPauseMinMs; return; }
            brain.WanderTarget = candidate;
        }
        if (StepTowards(m, brain.WanderTarget.Value, map, ctx, speedMult: 0.5f))
        {
            brain.WanderTarget = null;
            brain.WanderPauseUntilMs = ctx.NowMs + ctx.Rng.Next(WanderPauseMinMs, WanderPauseMaxMs + 1);
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

        var switchMult = m.Template.AttackRange > RangedAttackRangeThreshold ? rules.ThreatSwitchRanged : rules.ThreatSwitchMelee;
        var targetId = m.Threat.Reevaluate(ctx.NowMs, switchMult, id => map.Find(id) is Player p && p.IsAlive && CanBeAggroed(p) && Vec2.Distance(p.Position, m.Position) <= m.Template.LeashRange * 2);
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
        var inRange = dist <= m.Template.AttackRange && LineOfSight.Has(map.Data.Collision, m.Position, target.Position);
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

    private static Actor RandomNotTop(Monster m, Actor current, MapInstance map, TickContext ctx)
    {
        var ranked = m.Threat.Ranked();
        if (ranked.Count <= 1) return current;
        var candidates = new List<Actor>(ranked.Count - 1);
        for (var i = 1; i < ranked.Count; i++) if (map.Find(ranked[i]) is { IsAlive: true } a) candidates.Add(a);
        return candidates.Count == 0 ? current : candidates[ctx.Rng.Next(0, candidates.Count)];
    }

    private static bool EnsurePath(Monster m, Vec2 targetPos, MapInstance map, TickContext ctx)
    {
        var brain = m.Brain;
        var stale = brain.PathComputedAtMs == long.MinValue || ctx.NowMs - brain.PathComputedAtMs >= PathRecalcMs
                    || Vec2.Distance(brain.PathTargetPos, targetPos) > PathRecalcMovedTiles || brain.PathIndex >= brain.Path.Count;
        if (!stale) return true;
        if (!Pathfinder.FindPath(map.Data.Collision, m.Position, targetPos, brain.Path)) return false;
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
            if (Vec2.Distance(m.Position, waypoint) <= ArriveTolerance) { brain.PathIndex++; continue; }
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
        if (Vec2.Distance(m.Position, m.SpawnPosition) <= ArriveTolerance || StepTowards(m, m.SpawnPosition, map, ctx, (float)ctx.Rules.Combat.EvadeSpeedMult, ignoreControl: true))
        {
            m.Position = m.SpawnPosition;
            m.Combat.Evading = false;
            m.Hp = m.MaxHp;
            m.Threat.Clear();
            m.TaggedBy = null;
            auras.ClearAll(m, map, ctx);
            m.LastCombatAtMs = long.MinValue;
            m.Brain.State = AiState.Idle;
            m.Brain.WanderPauseUntilMs = ctx.NowMs + WanderPauseMinMs;
        }
    }

    // ---- Movimiento -----------------------------------------------------------------------------------------------------

    /// <summary>Un tick hacia `target` en 8 direcciones con MovementStep; devuelve true si llegó.</summary>
    private bool StepTowards(Monster m, Vec2 target, MapInstance map, TickContext ctx, float speedMult, bool ignoreControl = false)
    {
        var delta = target - m.Position;
        if (delta.Length <= ArriveTolerance) return true;
        if (!ignoreControl && (m.Auras.IsStunned || m.Auras.IsRooted)) return false;
        var dx = MathF.Abs(delta.X) < 0.1f ? 0 : Math.Sign(delta.X);
        var dy = MathF.Abs(delta.Y) < 0.1f ? 0 : Math.Sign(delta.Y);
        if (dx == 0 && dy == 0) return true;
        var speed = movement.SpeedOf(m, ctx) * speedMult;
        // No pasarse del destino en este tick.
        var stepTiles = speed * ctx.DeltaMs / 1000f;
        var before = m.Position;
        MovementSystem.Move(m, dx, dy, map.Data.Collision, speed);
        if (delta.Length <= stepTiles) { m.Position = target; }
        return Vec2.Distance(m.Position, target) <= ArriveTolerance || m.Position == before;
    }

    private static void FaceTowards(Monster m, Vec2 target)
    {
        var delta = target - m.Position;
        var dx = MathF.Abs(delta.X) < 0.1f ? 0 : Math.Sign(delta.X);
        var dy = MathF.Abs(delta.Y) < 0.1f ? 0 : Math.Sign(delta.Y);
        if (DirectionExtensions.FromInput(dx, dy) is { } f) m.Facing = f;
    }
}
