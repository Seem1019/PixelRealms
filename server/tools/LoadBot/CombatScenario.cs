using System.Diagnostics;
using System.Globalization;
using PixelRealms.Content;
using PixelRealms.Content.Defs;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Interest;
using PixelRealms.Game.Items;
using PixelRealms.Game.Map;
using PixelRealms.Game.Movement;
using PixelRealms.Game.Progression;

namespace PixelRealms.Tools.LoadBot;

/// <summary>
/// Escenario "Mina llena" (HU-089 CA1) en proceso: una instancia de `size`×`size`, `bots` jugadores de nivel máximo que lanzan un
/// hechizo por GCD (la mitad de área) y `monsters` monstruos con IA real. Corre los ticks sin dormir (el coste por tick es lo
/// que se mide) y evalúa los umbrales de CA2. Sin red: la salida por cliente (p95 KB/s) la mide HU-072 en el servidor real.
/// </summary>
public static class CombatScenario
{
    // Umbrales de HU-089 CA2, HU-088 CA5 (combate ≤ 4 ms p99, ADR-018) y HU-036 CA6 (IA < 3 ms): técnicos, no de balance.
    public const double TickP99LimitMs = 15, TickMaxLimitMs = 50, CombatP99LimitMs = 4, AiP99LimitMs = 3, AllocLimitBytesPerSec = 2 * 1024 * 1024, MemoryGrowthLimitPct = 10;

    private const int TicksPerSecond = 1000 / GameConstants.TickMs;

    public sealed record Result(int Ticks, double TickP50, double TickP99, double TickMax, double CombatP50, double CombatP99, double AllocPerSec, int Gen2,
        long MemStart, long MemEnd, int AurasAvg, int AurasMax, int AreasAvg, int AreasMax, int Casts, int Kills, int BotDeaths, double WallSec, bool MemoryChecked, double AiP99)
    {
        /// <summary>El +10 % de memoria se evalúa en la prueba de resistencia (≥ 30 min simulados); en corridas cortas solo se informa.</summary>
        public bool Passed => TickP99 <= TickP99LimitMs && TickMax <= TickMaxLimitMs && CombatP99 <= CombatP99LimitMs && AiP99 < AiP99LimitMs && AllocPerSec <= AllocLimitBytesPerSec && Gen2 == 0
                              && (!MemoryChecked || (MemEnd - MemStart) <= MemStart * MemoryGrowthLimitPct / 100.0);

        public string Report()
        {
            var ci = CultureInfo.InvariantCulture;
            string F(double v) => v.ToString("F2", ci);
            var lines = new[]
            {
                $"ticks {Ticks} en {F(WallSec)} s de reloj ({F(Ticks / 20.0)} s simulados)",
                $"tick p50 {F(TickP50)} ms · p99 {F(TickP99)} ms (≤ {TickP99LimitMs}) · máx {F(TickMax)} ms (≤ {TickMaxLimitMs})",
                $"combate p50 {F(CombatP50)} ms · p99 {F(CombatP99)} ms (≤ {CombatP99LimitMs}) por instancia · IA de monstruos p99 {F(AiP99)} ms (< {AiP99LimitMs})",
                $"asignación {F(AllocPerSec / 1024 / 1024)} MB/s simulado (≤ 2) · Gen2 {Gen2} (= 0) · memoria {F(MemStart / 1024.0 / 1024)} → {F(MemEnd / 1024.0 / 1024)} MB ({(MemoryChecked ? $"≤ +{MemoryGrowthLimitPct} %" : "solo informativo: < 30 min")})",
                $"auras media {AurasAvg} máx {AurasMax} (objetivo ~200) · áreas marcadas + proyectiles media {AreasAvg} máx {AreasMax} (objetivo 40)",
                $"casteos {Casts} · monstruos muertos {Kills} · muertes de bots {BotDeaths}",
                Passed ? "RESULTADO: OK" : "RESULTADO: FALLA (ver umbrales)",
            };
            return string.Join('\n', lines);
        }
    }

    public static Result Run(LoadOptions o, TextWriter log)
    {
        var content = ContentLoader.LoadOrThrow(LoadOptions.FindContentDir(o.ContentDir));
        var rng = new SeededRng(o.Seed);
        var world = new World();
        var grid = new CollisionGrid(o.Size, o.Size);
        for (var i = 0; i < o.Size; i++) { grid.SetSolid(i, 0); grid.SetSolid(i, o.Size - 1); grid.SetSolid(0, i); grid.SetSolid(o.Size - 1, i); }
        var map = new MapData("load", "Mina llena", grid, [], [], [new GraveyardDef("gy", new Vec2(o.Size / 2f, o.Size / 2f))], [], [], "gy");
        world.RegisterMap(map);
        var inst = world.CreateInstance("load");
        var sim = new Simulation(world, content.Rules, rng, new TickClock()) { CombatTimings = new Dictionary<int, TickStats>(), SystemTimings = new Dictionary<string, TickStats>(), SystemAllocs = new Dictionary<string, long>() };
        var combat = CombatModule.Create(() => content, world, new MovementSystem(), new InterestSystem());
        combat.Register(sim);

        var classes = new[] { "warrior", "rogue", "mage", "priest" };
        var level = content.Rules.CurrentLevelCap;
        var bots = new List<Player>(o.Bots);
        for (var i = 0; i < o.Bots; i++)
        {
            var classId = classes[i % classes.Length];
            // Grupos de 5 repartidos por el mapa, cada uno en su rincón de monstruos.
            var group = i / 5; var groups = Math.Max(1, (o.Bots + 4) / 5);
            var angle = 2 * Math.PI * group / groups;
            var cx = o.Size / 2f + (float)(Math.Cos(angle) * o.Size * 0.3); var cy = o.Size / 2f + (float)(Math.Sin(angle) * o.Size * 0.3);
            var p = MakePlayer(world, content, $"Bot{i}", classId, level, new Vec2(cx + (i % 5) * 0.8f, cy));
            inst.Add(p); bots.Add(p);
        }
        var monsterIds = new[] { "kobold_miner", "kobold_miner", "kobold_miner", "wolf", "goblin_archer", "rubble_golem" };
        for (var i = 0; i < o.Monsters; i++)
        {
            var t = content.Monster(monsterIds[i % monsterIds.Length]);
            var pos = new Vec2(2 + (float)(rng.NextDouble() * (o.Size - 4)), 2 + (float)(rng.NextDouble() * (o.Size - 4)));
            var m = new Monster(world.EntityIds.Next(), t, pos, 3) { Level = t.Level, Position = pos, Hp = t.Hp, MaxHp = t.Hp, BaseSpeed = (float)t.Speed };
            m.Brain.Spawn = new SpawnDef($"s{i}", t.Id, 1, 3, pos, Vec2.Zero);
            inst.Add(m);
        }
        var spellsByClass = classes.ToDictionary(c => c, c => content.KnownSpells(c, level).ToList());

        var ticks = o.DurationSec * TicksPerSecond;
        var tickStats = new TickStats(ticks);
        AuraDef? dotAura; try { dotAura = content.Aura("foreman_whip_bleed"); } catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException) { dotAura = null; }
        var casts = 0; var kills = 0; var botDeaths = 0; long aurasSum = 0, areasSum = 0; var aurasMax = 0; var areasMax = 0;
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var memStart = GC.GetTotalMemory(true);
        var allocStart = GC.GetTotalAllocatedBytes(true);
        var gen2Start = GC.CollectionCount(2);
        var wall = Stopwatch.StartNew();
        var ctx = sim.Context;
        var monsters = inst.Monsters.Values.ToList();
        // Calentamiento (JIT) de 2 s simulados fuera de la medición.
        for (var w = 0; w < 2 * TicksPerSecond; w++) sim.RunTick();
        sim.CombatTimings[inst.Id].Reset();
        foreach (var st in sim.SystemTimings!.Values) st.Reset();
        sim.SystemAllocs!.Clear();
        memStart = GC.GetTotalMemory(true); allocStart = GC.GetTotalAllocatedBytes(true); gen2Start = GC.CollectionCount(2); // la recolección forzada queda fuera del conteo
        for (var t = 0; t < ticks; t++)
        {
            // ~200 auras vivas (CA1): los kits del Tier 1 no las generan solos, así que se completa con sangrados sobre monstruos.
            if (dotAura is not null && t % 2 == 0)
            {
                var live = 0; foreach (var a in inst.Actors.Values) live += a.Auras.Count;
                for (var k = 0; live < 200 && k < 8; k++, live++)
                {
                    var m = monsters[rng.Next(0, monsters.Count)];
                    if (m.IsAlive) combat.Auras.Apply(m, dotAura, bots[k % bots.Count], inst, ctx);
                }
            }
            // Bots: un hechizo por GCD, la mitad de área; básico encendido; muertos reaparecen en cuanto se puede.
            var now = sim.Clock.NowMs + GameConstants.TickMs;
            foreach (var bot in bots)
            {
                if (bot.IsDead)
                {
                    // Sin retardo de reaparición en las reglas (Died{respawnInMs: 0}): reaparece 2 s después para no distorsionar la carga.
                    if (bot.Combat.DiedAtMs + 2000 <= now) combat.Death.Respawn(bot, inst, ctx);
                    continue;
                }
                if (bot.Combat.TargetId is null || inst.Find(bot.Combat.TargetId.Value) is not Monster { IsAlive: true })
                {
                    bot.Combat.TargetId = Nearest(bot, monsters)?.Id;
                    bot.Combat.AutoAttackOn = bot.Combat.TargetId is not null;
                }
                if (bot.Combat.TargetId is null || bot.Combat.IsOnGcd(now) || bot.Combat.Cast is not null) continue;
                var target = (Monster)inst.Find(bot.Combat.TargetId.Value)!;
                var spells = spellsByClass[bot.ClassId];
                if (spells.Count == 0) continue;
                var wantArea = (t + bot.Id.Value) % 2 == 0;
                SpellDef? spell = null;
                foreach (var s in spells) if (s.Targeting.IsArea() == wantArea && !bot.Combat.IsOnCooldown(s.Id, now) && s.Targeting != Targeting.Ally && s.Targeting != Targeting.SelfAoeAllies) { spell = s; break; }
                spell ??= spells.FirstOrDefault(s => !bot.Combat.IsOnCooldown(s.Id, now) && s.Targeting != Targeting.Ally && s.Targeting != Targeting.SelfAoeAllies);
                if (spell is null) continue;
                var err = combat.Casts.TryBeginCast(bot, spell, spell.Targeting.IsGround() ? null : target.Id, spell.Targeting.IsGround() ? target.Position : null, inst, ctx);
                if (err is null) casts++;
                else if (err is CastErrors.OutOfRange or CastErrors.NoLos)
                {
                    var d = target.Position - bot.Position;
                    bot.MoveDx = Math.Sign(d.X); bot.MoveDy = Math.Sign(d.Y);
                }
                if (err is null) { bot.MoveDx = 0; bot.MoveDy = 0; }
            }
            var t0 = Stopwatch.GetTimestamp();
            sim.RunTick();
            tickStats.Record(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
            foreach (var e in ctx.Events)
            {
                if (e is ActorDiedEvent died) { if (died.Victim is Monster) kills++; else botDeaths++; }
            }
            if (t % 10 == 0)
            {
                var auras = 0; foreach (var a in inst.Actors.Values) auras += a.Auras.Count;
                var areas = combat.Casts.ActiveAreas(inst) + combat.Casts.PendingImpacts(inst); // áreas marcadas + proyectiles en vuelo
                aurasSum += auras; areasSum += areas; aurasMax = Math.Max(aurasMax, auras); areasMax = Math.Max(areasMax, areas);
            }
            if (t % (TicksPerSecond * 30) == 0 && t > 0)
            {
                var (p50, p99) = tickStats.Percentiles();
                log.WriteLine($"  t={t / TicksPerSecond}s tick p50 {p50:F2} p99 {p99:F2} máx {tickStats.MaxMs:F2} ms · casteos {casts} · kills {kills}");
            }
        }
        wall.Stop();
        log.WriteLine("  por sistema (p50 / p99 / máx ms): " + string.Join(" · ", sim.SystemTimings!.Select(kv => { var (a, b) = kv.Value.Percentiles(); return $"{kv.Key} {a:F2}/{b:F2}/{kv.Value.MaxMs:F1}"; })));
        log.WriteLine("  asignación por sistema (KB/s simulado): " + string.Join(" · ", sim.SystemAllocs!.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value / 1024.0 / o.DurationSec:F1}")));
        var alloc = GC.GetTotalAllocatedBytes(true) - allocStart;
        var gen2 = GC.CollectionCount(2) - gen2Start;
        var memEnd = GC.GetTotalMemory(true);
        var (tp50, tp99) = tickStats.Percentiles();
        var (cp50, cp99) = sim.CombatTimings![inst.Id].Percentiles();
        var aiP99 = sim.SystemTimings!.TryGetValue("monster_ai", out var ai) ? ai.Percentiles().P99 : 0;
        var samples = Math.Max(1, (ticks + 9) / 10);
        return new Result(ticks, tp50, tp99, tickStats.MaxMs, cp50, cp99, alloc / Math.Max(1.0, o.DurationSec), gen2, memStart, memEnd,
            (int)(aurasSum / samples), aurasMax, (int)(areasSum / samples), areasMax, casts, kills, botDeaths, wall.Elapsed.TotalSeconds, o.DurationSec >= 1800, aiP99);
    }

    private static Monster? Nearest(Player bot, List<Monster> monsters)
    {
        Monster? best = null; var bestD = float.MaxValue;
        foreach (var m in monsters)
        {
            if (m.IsDead) continue;
            var d = Vec2.Distance(m.Position, bot.Position);
            if (d < bestD) { bestD = d; best = m; }
        }
        return best;
    }

    private static Player MakePlayer(World world, ContentDb content, string name, string classId, int level, Vec2 at)
    {
        var cls = content.Class(classId);
        var p = new Player(world.EntityIds.Next(), name, classId) { CharacterId = Guid.NewGuid(), AccountId = Guid.NewGuid(), Level = level, Position = at };
        foreach (var start in cls.StartingItems.Where(i => i.Equip))
        {
            var tpl = content.Item(start.ItemId);
            if (tpl.Slot is { } slot) p.Equipment.Slots[(int)slot] = ItemInstance.New(start.ItemId);
        }
        var equipped = p.Equipment.Slots.Where(s => s is not null).Select(s => content.Item(s!.TemplateId)).ToList();
        var d = StatCalculator.Derive(cls, level, content.Rules, equipped);
        p.MaxHp = d.MaxHp; p.Hp = p.MaxHp;
        p.MaxResource = StatCalculator.MaxResource(cls, d, content.Rules);
        p.Resource = cls.Resource == Resource.Rage ? 0 : p.MaxResource;
        p.BaseSpeed = (float)content.Rules.Movement.BaseSpeedTilesPerSec;
        p.KnownSpells.AddRange(content.KnownSpells(classId, level).Select(sp => sp.Id));
        return p;
    }
}
