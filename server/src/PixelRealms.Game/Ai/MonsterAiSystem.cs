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
/// vida completa y amenaza limpia al llegar). Cambio de objetivo por amenaza con la regla 110 %/130 % (al revés con
/// `threatTarget: lowest`, HU-117). Un monstruo inmóvil (`speed: 0`) no persigue: lanza sus hechizos aunque su objetivo quede
/// fuera de su `attackRange` y se reinicia en su sitio si se queda sin nadie o si nadie de su tabla está a su alcance y a la
/// vista durante `rules.ai.immobileOutOfReachResetMs` (HU-117).
/// </summary>
public sealed class MonsterAiSystem(CombatServices services, CastSystem casts, AuraSystem auras, MovementSystem movement) : IMapSystem
{
    // Ritmos de la IA (percepción, A*, patrulla, llegada): `rules.ai` (regla 4, ADR-008).

    private readonly List<Monster> _monsters = new(128);
    private readonly List<Actor> _candidates = new(8);

    public string Name => "monster_ai";

    /// <summary>¿El jugador puede ser objetivo de monstruos? (duelistas no, HU-036 CA5c). Por defecto, todo jugador vivo.</summary>
    public Func<Player, bool> CanBeAggroed { get; set; } = static p => !p.IsDead;

    /// <summary>
    /// ¿Puede `m` tener a `id` de objetivo (de su básico, de sus hechizos o de una invocación suya)? Un jugador vivo del mapa, que
    /// se puede atacar (no en duelo) y a ≤ 2 × `leashRange`; con `threatTarget: lowest`, además al alcance de su correa desde su
    /// sitio: si no, un señuelo lejano con poca amenaza lo sacaría de ella (revisión de autoridad, HU-117).
    /// </summary>
    public bool IsValidTarget(Monster m, EntityId id, MapInstance map) =>
        map.Find(id) is Player p && p.IsAlive && CanBeAggroed(p) && Vec2.Distance(p.Position, m.Position) <= m.Template.LeashRange * 2
        && (m.Template.ThreatTarget != ThreatTarget.Lowest || Vec2.Distance(p.Position, m.SpawnPosition) <= m.Template.LeashRange);

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
        var targetId = m.Threat.Reevaluate(ctx.NowMs, switchMult, (this, m, map), static (id, s) => s.Item1.IsValidTarget(s.m, id, s.map),
            m.Template.ThreatTarget);
        var target = targetId is { } tid ? map.Find(tid) : null;
        if (target is null)
        {
            // Reevaluate solo devuelve objetivos válidos (del mapa); si aun así faltara, se quita esa entrada y no toda la tabla.
            if (targetId is { } stale) { m.Threat.Remove(stale); return; }
            // Inmóvil: la correa nunca lo hace evadir; sin nadie a quien pegar se reinicia en su sitio (vida completa, sin auras),
            // o salir de la sala y volver dejaría al jefe a medias (HU-117).
            if (IsImmobile(m)) { BeginEvade(m, map, ctx); return; }
            m.Threat.Clear();
            m.Combat.ResetTransient();
            brain.State = AiState.Idle;
            brain.ClearPath();
            return;
        }
        // Inmóvil: si su objetivo (por amenaza) no está a tiro del básico, pega al de más amenaza que sí lo esté; si no, el tanque
        // podía apartarse a 13 casillas o tras una columna y dejarlo sin pegar a nadie (revisión de autoridad, HU-117).
        var immobile = IsImmobile(m);
        var inRange = InBasicReach(m, target, map);
        var striking = target;
        if (!inRange && immobile && BestInReach(m, null, map, ctx.Rules.Combat) is { } other) { striking = other; inRange = true; }
        m.Combat.TargetId = striking.Id;
        // Inmóvil que lleva un rato sin poder pegar con el básico a nadie: se reinicia (revisión de autoridad, HU-117). Si no,
        // quedarse entre su alcance y 2 × leashRange, o tras una columna, lo dejaba herido sin pegar ni curarse.
        if (immobile && OutOfReachTooLong(m, inRange, ctx)) { BeginEvade(m, map, ctx); return; }
        if (inRange)
        {
            brain.State = AiState.Attack;
            brain.ClearPath();
            FaceTowards(m, striking.Position);
            if (!m.Combat.IsCasting && !m.Auras.IsStunned && TryCastSpell(m, striking, map, ctx)) return;
            m.Combat.AutoAttackOn = true;
            return;
        }

        brain.State = AiState.Chase;
        m.Combat.AutoAttackOn = true; // el básico se pausa solo fuera de alcance; sigue listo al llegar
        if (immobile)
        {
            // No persigue (ni calcula caminos): sus hechizos van a quien alcancen aunque el objetivo se quede lejos o tras una
            // columna (HU-117: alejarse no es seguro).
            FaceTowards(m, target.Position);
            if (!m.Combat.IsCasting && !m.Auras.IsStunned) TryCastSpell(m, target, map, ctx);
            return;
        }
        if (m.Combat.IsCasting || m.Auras.IsStunned || m.Auras.IsRooted) return;
        if (!EnsurePath(m, target.Position, map, ctx))
        {
            // `lowest` no se deja arrastrar por quien no puede alcanzar (un sanador tras el agua): lo quita y va a por el siguiente;
            // evadir borraría a una invocación (revisión de autoridad, HU-117). Sin nadie más, evade como siempre.
            if (m.Template.ThreatTarget == ThreatTarget.Lowest && m.Threat.Count > 1) { m.Threat.Remove(target.Id); brain.ClearPath(); return; }
            BeginEvade(m, map, ctx);
            return;
        }
        FollowPath(m, map, ctx);
    }

    /// <summary>`speed: 0` en su plantilla (planta trampa, Árbol Podrido): ni patrulla ni persigue.</summary>
    private static bool IsImmobile(Monster m) => m.Template.Speed <= 0;

    /// <summary>¿Le llega su básico a `a` (a ≤ `attackRange` y a la vista)?</summary>
    private static bool InBasicReach(Monster m, Actor a, MapInstance map) =>
        Vec2.Distance(m.Position, a.Position) <= m.Template.AttackRange && LineOfSight.Has(map.Collision, m.Position, a.Position);

    /// <summary>
    /// Lleva la cuenta de <see cref="MonsterBrain.OutOfReachMs"/>: suma el tick si no puede pegar con el básico a nadie de su tabla y
    /// lo resta si pega. Solo cuenta el básico (a ≤ `attackRange` y vista desde el monstruo): desde el anillo de sus hechizos, que se
    /// esquivan, o tras una columna no se le puede tener entretenido, y asomarse un instante no descuenta lo acumulado (revisión de
    /// autoridad, HU-117). Devuelve si ha llegado al plazo de rules.ai.
    /// </summary>
    private static bool OutOfReachTooLong(Monster m, bool striking, TickContext ctx)
    {
        var brain = m.Brain;
        brain.OutOfReachMs = striking ? Math.Max(0, brain.OutOfReachMs - ctx.DeltaMs) : brain.OutOfReachMs + ctx.DeltaMs;
        return brain.OutOfReachMs >= ctx.Rules.Ai.ImmobileOutOfReachResetMs;
    }

    /// <summary>¿Le llega `spell` a `target` desde donde está? El mismo alcance y la misma vista que comprueba
    /// <see cref="CastSystem.TryBeginCast"/>, sin recorrer la instancia: un casteo condenado no se intenta (revisión de autoridad).</summary>
    private static bool Reaches(Monster m, SpellDef spell, Actor target, MapInstance map, CombatRules rules)
    {
        if (ReferenceEquals(target, m)) return true;
        var d = Vec2.Distance(m.Position, target.Position);
        if (spell.Targeting.IsGround())
        {
            if (d > spell.Range + rules.CastRangeToleranceTiles) return false;
            if (spell.Shape != Shape.Circle) return true; // cono y línea: el punto solo da la dirección (HU-102)
            return LineOfSight.Has(map.Collision, m.Position, target.Position)
                   && !map.Collision.BlocksSight((int)MathF.Floor(target.Position.X), (int)MathF.Floor(target.Position.Y));
        }
        if (spell.Targeting != Targeting.Enemy) return true;
        return d <= spell.Range && LineOfSight.Has(map.Collision, m.Position, target.Position);
    }

    /// <summary>El de más amenaza (o menos, con `threatTarget: lowest`) entre los válidos de su tabla a los que llega `spell`, o su
    /// básico si `spell` es null; null si no hay ninguno. Recorre la tabla sin asignar.</summary>
    private Actor? BestInReach(Monster m, SpellDef? spell, MapInstance map, CombatRules rules)
    {
        var lowest = m.Template.ThreatTarget == ThreatTarget.Lowest;
        Actor? best = null;
        var bestValue = lowest ? double.MaxValue : double.MinValue;
        foreach (var raw in m.Threat.Ids)
        {
            var id = new EntityId(raw);
            var v = m.Threat.Of(id);
            if (lowest ? v >= bestValue : v <= bestValue) continue;
            if (!IsValidTarget(m, id, map) || map.Find(id) is not { } a) continue;
            if (spell is null ? !InBasicReach(m, a, map) : !Reaches(m, spell, a, map, rules)) continue;
            best = a;
            bestValue = v;
        }
        return best;
    }

    private bool TryCastSpell(Monster m, Actor current, MapInstance map, TickContext ctx)
    {
        var spells = m.Template.Spells;
        if (spells.Count == 0) return false;
        var db = services.Content;
        var rules = ctx.Rules.Combat;
        for (var i = 0; i < spells.Count; i++)
        {
            var ms = spells[i];
            if (m.Combat.IsOnCooldown(ms.SpellId, ctx.NowMs)) continue;
            if (ms.HpBelowPct < 1.0 && m.MaxHp > 0 && (double)m.Hp / m.MaxHp > ms.HpBelowPct) continue;
            if (!db.TryGetSpell(ms.SpellId, out var spell) || spell is null) continue;
            var target = ms.Target switch
            {
                MonsterSpellTarget.Self => m,
                MonsterSpellTarget.RandomNotTopThreat => RandomOther(m, current, spell, map, ctx),
                _ => Reaches(m, spell, current, map, rules) ? current : null,
            };
            // Un inmóvil no puede acercarse: si a su objetivo no le llega, va al de más amenaza al que sí (revisión de autoridad).
            // En `random_not_top_threat` no hace falta: ya ha mirado a todos.
            if (target is null && ms.Target == MonsterSpellTarget.Current && IsImmobile(m)) target = BestInReach(m, spell, map, rules);
            if (target is null) continue;
            // Áreas apuntadas, y conos o líneas aunque salgan del monstruo (HU-102): hacia su objetivo, fijado al empezar.
            Vec2? targetPos = spell.Targeting.IsGround() || (spell.Targeting.IsArea() && spell.Shape != Shape.Circle) ? target.Position : null;
            if (casts.TryBeginCast(m, spell, target.Id, targetPos, map, ctx) is null) return true;
        }
        return false;
    }

    /// <summary>
    /// `random_not_top_threat`: al azar entre los válidos de su tabla (el mismo criterio que su objetivo) a los que llega el
    /// hechizo, salvo `current`, a quien está pegando; si no hay ninguno, `current` si le llega. Revisión de autoridad (HU-117):
    /// antes excluía al de más amenaza de toda la tabla, que con la regla del 130 % no siempre es su objetivo, y sorteaba sin mirar
    /// si le llegaba.
    /// </summary>
    private Actor? RandomOther(Monster m, Actor current, SpellDef spell, MapInstance map, TickContext ctx)
    {
        _candidates.Clear();
        foreach (var raw in m.Threat.Ids)
        {
            var id = new EntityId(raw);
            if (id == current.Id || !IsValidTarget(m, id, map) || map.Find(id) is not { } a) continue;
            if (Reaches(m, spell, a, map, ctx.Rules.Combat)) _candidates.Add(a);
        }
        if (_candidates.Count > 0) return _candidates[ctx.Rng.Next(0, _candidates.Count)];
        return Reaches(m, spell, current, map, ctx.Rules.Combat) ? current : null;
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
        m.Brain.OutOfReachMs = 0;
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
