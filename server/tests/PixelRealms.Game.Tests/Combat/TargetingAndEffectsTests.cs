using PixelRealms.Content.Defs;
using PixelRealms.Content.Validation;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Progression;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Combat;

/// <summary>HU-034: targeting con posiciones concretas, motor de efectos, balance CA3b y los hechizos disponibles sobre un maniquí.</summary>
public sealed class TargetingAndEffectsTests
{
    private static TestWorld Arena(string classId = "mage", int level = 5)
        => new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", classId, level, (10, 10))
            .WithPlayer("Bob", "priest", level, (11, 10))
            .WithMonster("slime", (12, 10), wanderRadius: 0)   // a 2
            .WithMonster("boar", (13, 10), wanderRadius: 0)    // a 3
            .WithMonster("wolf", (10, 16), wanderRadius: 0)    // a 6
            .BuildWithCombat();

    private static List<Actor> Targets(TestWorld w, string spellId, EntityId? target = null, Vec2? pos = null)
    {
        var ana = w.Player("Ana");
        return w.Combat.Targets.Resolve(ana, w.Content.Spell(spellId), target, pos, ana.Position, w.Map, w.Begin());
    }

    [Fact]
    public void SelfAoeEnemies_RadiusAndMaxTargets_NearestFirst() // CA2
    {
        var w = Arena();
        var nova = w.Content.Spell("mage_frost_nova"); // radio 3 alrededor del lanzador
        var t = Targets(w, "mage_frost_nova");
        t.Select(a => a.Name).ToArray().ShouldBe(new[] { w.Monster("slime").Name, w.Monster("boar").Name });
        t.ShouldNotContain(w.Player("Bob")); // sin fuego amigo
        t.Count.ShouldBe(2);
        // maxTargets 1 → solo el más cercano
        var one = nova with { MaxTargets = 1 };
        var ana = w.Player("Ana");
        w.Combat.Targets.Resolve(ana, one, null, null, ana.Position, w.Map, w.Begin()).Single().ShouldBe(w.Monster("slime"));
        // Fuera de radio (lobo a 6)
        t.ShouldNotContain(w.Monster("wolf"));
    }

    [Fact]
    public void GroundArea_HitsWhenTheCircleTouchesTheDrawnBody_NotOnlyTheFeet()
    {
        // Estallido de llamas: radio 2,5 en (10, 10). El cuadro del cuerpo sube 0,75 casillas desde los pies y mide 0,75 de ancho.
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "mage", 5, (4, 4))
            .WithMonster("slime", (10, 12.6f), wanderRadius: 0)   // pies a 2,6: fuera; la parte de arriba del cuadro, dentro
            .WithMonster("boar", (12.8f, 10), wanderRadius: 0)    // pies a 2,8: fuera; el lado izquierdo del cuadro, dentro
            .WithMonster("wolf", (10, 7.2f), wanderRadius: 0)     // pies a 2,8 por arriba: el cuadro queda más arriba, fuera
            .BuildWithCombat();
        var t = Targets(w, "mage_flame_burst", pos: new Vec2(10, 10));
        t.ShouldContain(w.Monster("slime"));
        t.ShouldContain(w.Monster("boar"));
        t.ShouldNotContain(w.Monster("wolf"));
    }

    [Fact]
    public void GroundArea_TouchingTheTopOfABodyBehindAWall_DoesNotHit()
    {
        // El jabalí está justo al sur de un muro: la parte de arriba de su cuadro entra en la casilla del muro y el círculo la
        // toca, pero la línea de visión se mide del centro a los pies, y el muro la corta.
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "mage", 5, (4, 4))
            .WithMonster("boar", (10.5f, 12.1f), wanderRadius: 0)
            .BuildWithCombat();
        Targets(w, "mage_flame_burst", pos: new Vec2(10.5f, 9.0f)).ShouldContain(w.Monster("boar")); // sin muro: el cuadro entra (pies fuera)
        w.Map.Data.Collision.SetBlocksSight(10, 11);
        Targets(w, "mage_flame_burst", pos: new Vec2(10.5f, 9.0f)).ShouldNotContain(w.Monster("boar"));
    }

    [Fact]
    public void SelfAoeAllies_IncludesCaster_And_GroundAll_SplitsByRelation()
    {
        var w = Arena("priest");
        Targets(w, "priest_hymn").Select(a => a.Name).OrderBy(n => n).ToArray().ShouldBe(new[] { "Ana", "Bob" });
        var all = Targets(w, "priest_holy_pulse", null, new Vec2(11, 10)); // radio 3 en (11,10): Ana, Bob, slime, boar
        all.Count.ShouldBe(4);
        all.ShouldNotContain(w.Monster("wolf"));
    }

    [Fact]
    public void GroundArea_WallBlocksLosFromCenter()
    {
        var w = Arena(level: 5);
        w.Map.Data.Collision.SetBlocksSight(12, 10); // entre el centro (11,10) y el jabalí (13,10); el slime está en la propia casilla del muro
        var t = Targets(w, "mage_flame_burst", null, new Vec2(11.5f, 10.5f));
        t.ShouldNotContain(w.Monster("boar"));
    }

    [Fact]
    public void GroundAoeAll_PositiveToAllies_NegativeToEnemies() // HU-085 CA1
    {
        var w = Arena("priest", 5);
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        ana.Hp = 20; bob.Hp = 20;
        var slime = w.Monster("slime");
        var ctx = w.Begin();
        w.Combat.Effects.Apply(ana, w.Content.Spell("priest_holy_pulse"), null, new Vec2(11, 10), ana.Position, w.Map, ctx);
        var heals = ctx.Events.OfType<CombatHitEvent>().Where(e => e.Kind == HitKinds.Heal).Select(e => e.Target).ToList();
        heals.ShouldContain(ana); heals.ShouldContain(bob); heals.ShouldNotContain(slime);
        var dmg = ctx.Events.OfType<CombatHitEvent>().Where(e => e.Kind is HitKinds.Damage or HitKinds.Miss).Select(e => e.Target).ToList();
        dmg.ShouldNotContain(ana); dmg.ShouldNotContain(bob);
        ctx.Events.OfType<AuraAppliedEvent>().ShouldAllBe(e => e.Target is Monster); // la ralentización solo a enemigos
    }

    [Fact]
    public void HolyPulse_DamageMuchLowerThanHeal_SameSpellPower() // HU-085 CA2
    {
        var db = TestContent.Load();
        var pulse = db.Spell("priest_holy_pulse");
        pulse.LevelReq.ShouldBe(5);
        var heal = pulse.Effects.First(e => e.Type == EffectType.Heal);
        var dmg = pulse.Effects.First(e => e.Type == EffectType.Damage);
        const double sp = 30;
        CombatCalculator.MagicRaw(dmg, sp).ShouldBeLessThan(CombatCalculator.MagicRaw(heal, sp) * 0.5);
        pulse.Effects.ShouldContain(e => e.Type == EffectType.ApplyAura && db.Aura(e.AuraId!).Kind == AuraKind.Slow);
    }

    [Fact]
    public void Dash_PlacesCasterAdjacent_RequiresMinRange() // CA1 (dash)
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "warrior", 3, (10, 10)).WithMonster("wolf", (17, 10), wanderRadius: 0).BuildWithCombat();
        var ana = w.Player("Ana"); var wolf = w.Monster("wolf");
        var charge = w.Content.Spell("warrior_charge");
        var ctx = w.Begin();
        w.Combat.Casts.TryBeginCast(ana, charge, wolf.Id, null, w.Map, ctx).ShouldBeNull();
        Vec2.Distance(ana.Position, wolf.Position).ShouldBe(1f, 0.01f);
        ctx.Events.OfType<ForcedMoveEvent>().Single().Actor.ShouldBe(ana);
        // Demasiado cerca → out_of_range (minRange)
        ana.Combat.CooldownEndsAtMs.Clear(); ana.Combat.GcdEndsAtMs = long.MinValue; ana.Combat.AbilityLockEndsAtMs = long.MinValue;
        w.Combat.Casts.TryBeginCast(ana, charge, wolf.Id, null, w.Map, w.Begin()).ShouldBe(CastErrors.OutOfRange);
    }

    [Fact]
    public void Leap_ClampsToFreeTile_WithLos_EffectsAtLanding() // HU-087 CA1/CA3/CA5
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "rogue", 3, (10.5f, 10.5f)).WithMonster("slime", (14.5f, 10.5f), wanderRadius: 0).BuildWithCombat();
        var ana = w.Player("Ana");
        for (var y = 0; y < 40; y++) { w.Map.Data.Collision.SetSolid(16, y); w.Map.Data.Collision.SetBlocksSight(16, y); }
        var ctx = w.Begin();
        // Paso sombrío (alcance 5): apunta tras el muro → se queda antes del muro.
        w.Combat.Casts.TryBeginCast(ana, w.Content.Spell("rogue_shadowstep"), null, new Vec2(18.5f, 10.5f), w.Map, ctx).ShouldBeNull();
        ana.Position.X.ShouldBeLessThan(16);
        ana.Position.X.ShouldBeGreaterThan(14);
        // El aura de velocidad propia (applyTo self) y la ralentización al slime en el punto de llegada.
        ctx.Events.OfType<AuraAppliedEvent>().ShouldContain(e => e.Target == ana);
        ctx.Events.OfType<AuraAppliedEvent>().ShouldContain(e => e.Target == w.Monster("slime"));
        // Fuera del mapa: nunca sale.
        var w2 = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "rogue", 3, (2.5f, 2.5f)).BuildWithCombat();
        var a2 = w2.Player("Ana");
        w2.Combat.Casts.TryBeginCast(a2, w2.Content.Spell("rogue_shadowstep"), null, new Vec2(0.5f, 0.5f), w2.Map, w2.Begin()).ShouldBeNull();
        w2.Map.Data.Collision.IsSolidAt(a2.Position.X, a2.Position.Y).ShouldBeFalse();
    }

    [Fact]
    public void Leap_CancelsOwnCast_WithoutCost() // HU-087 CA6
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "mage", 9, (10.5f, 10.5f)).WithMonster("slime", (14.5f, 10.5f), wanderRadius: 0).BuildWithCombat();
        var ana = w.Player("Ana");
        var mana = ana.Resource;
        w.Combat.Casts.TryBeginCast(ana, w.Content.Spell("mage_fireball"), w.Monster("slime").Id, null, w.Map, w.Begin()).ShouldBeNull();
        ana.Combat.GcdEndsAtMs = long.MinValue;
        var ctx = w.Begin();
        w.Combat.Casts.TryBeginCast(ana, w.Content.Spell("mage_blink"), null, new Vec2(12.5f, 10.5f), w.Map, ctx).ShouldBeNull();
        ctx.Events.OfType<CastEndedEvent>().First(e => e.Spell.Id == "mage_fireball").Result.ShouldBe(CastResults.Cancelled);
        ana.Resource.ShouldBe(mana - w.Content.Spell("mage_blink").Cost!.Amount); // solo el coste del Parpadeo
    }

    [Theory]
    [InlineData("warrior", 1)] [InlineData("warrior", 5)] [InlineData("warrior", 15)]
    [InlineData("rogue", 1)] [InlineData("rogue", 5)] [InlineData("rogue", 15)]
    [InlineData("mage", 1)] [InlineData("mage", 5)] [InlineData("mage", 15)]
    [InlineData("priest", 1)] [InlineData("priest", 5)] [InlineData("priest", 15)]
    public void DerivedStats_AllFormulas_AllClasses(string classId, int level) // CA3
    {
        var db = TestContent.Load();
        var cls = db.Class(classId);
        var rules = db.Rules; var c = rules.Combat; var z = rules.ClassScaling[classId];
        var gear = new[] { db.Item("iron_sword"), db.Item("foreman_breastplate") }; // espada + placas: fuera de rol para Mago/Sacerdote
        var d = StatCalculator.Derive(cls, level, rules, gear);
        double Aff(ItemTemplate i) => rules.Affinity.MultiplierFor(classId, i.AffinityType);
        var p = new PrimaryStats(
            cls.BaseStats.Str + cls.StatsPerLevel.Str * (level - 1) + gear.Sum(i => (i.Stats?.Str ?? 0) * Aff(i)),
            cls.BaseStats.Agi + cls.StatsPerLevel.Agi * (level - 1) + gear.Sum(i => (i.Stats?.Agi ?? 0) * Aff(i)),
            cls.BaseStats.Int + cls.StatsPerLevel.Int * (level - 1) + gear.Sum(i => (i.Stats?.Int ?? 0) * Aff(i)),
            cls.BaseStats.Spi + cls.StatsPerLevel.Spi * (level - 1) + gear.Sum(i => (i.Stats?.Spi ?? 0) * Aff(i)),
            cls.BaseStats.Sta + cls.StatsPerLevel.Sta * (level - 1) + gear.Sum(i => (i.Stats?.Sta ?? 0) * Aff(i)));
        d.Primary.Str.ShouldBe(p.Str, 1e-9); d.Primary.Agi.ShouldBe(p.Agi, 1e-9); d.Primary.Int.ShouldBe(p.Int, 1e-9);
        d.Primary.Spi.ShouldBe(p.Spi, 1e-9); d.Primary.Sta.ShouldBe(p.Sta, 1e-9);
        d.MaxHp.ShouldBe((int)Math.Round(cls.BaseHp + p.Sta * z.HpPerSta));
        d.MaxMana.ShouldBe(z.ManaPerInt > 0 ? (int)Math.Round(cls.BaseMana + p.Int * z.ManaPerInt) : 0);
        d.AttackPower.ShouldBe(z.Ap.Str * p.Str + z.Ap.Agi * p.Agi + z.Ap.Int * p.Int, 1e-9);
        d.SpellPower.ShouldBe(z.Sp.Str * p.Str + z.Sp.Agi * p.Agi + z.Sp.Int * p.Int + gear.Sum(i => i.SpellPower * Aff(i)), 1e-9);
        d.CritChancePhysical.ShouldBe(Math.Min(c.CritCap, c.CritBase + p.Agi * c.CritPerAgi), 1e-9);
        d.CritChanceMagic.ShouldBe(Math.Min(c.CritCap, c.CritBase + p.Int * c.CritPerInt), 1e-9);
        d.DodgeChance.ShouldBe(Math.Min(c.DodgeCap, c.DodgeBase + p.Agi * c.DodgePerAgi), 1e-9);
        d.Armor.ShouldBe(gear.Sum(i => i.Armor * Aff(i)) * z.ArmorMult + p.Agi * c.ArmorPerAgi, 1e-9);
        d.Haste.ShouldBe(z.Haste);
        d.ManaRegenPer5s.ShouldBe(p.Spi * c.ManaRegenPerSpiPer5s + p.Int * c.ManaRegenPerIntPer5s, 1e-9);
        d.HpRegenPerSec.ShouldBe(p.Spi * c.HpRegenPerSpi + p.Sta * c.HpRegenPerSta, 1e-9);
        d.WeaponAffinity.ShouldBe(Aff(gear[0]));
    }

    [Fact]
    public void OffRoleBalance_IronSwordAndMailShirt_WithinRulesMargins() // CA3b (escenario exacto; márgenes de rules.balanceTargets)
    {
        var db = TestContent.Load();
        var rules = db.Rules; var c = rules.Combat;
        var goblin = db.Monster("goblin_archer");
        var gear = new[] { db.Item("iron_sword"), db.Item("recruit_mail_shirt") };
        const int level = 5;
        var dps = new Dictionary<string, double>();
        var ttk = new Dictionary<string, double>();
        foreach (var classId in new[] { "warrior", "rogue", "mage", "priest" })
        {
            var cls = db.Class(classId);
            var d = StatCalculator.Derive(cls, level, rules, gear);
            var sword = gear[0];
            var swingSec = sword.SpeedMs / d.Haste / 1000.0;
            var raw = CombatCalculator.BasicAttackRaw((sword.DamageMin + sword.DamageMax) / 2, d.WeaponAffinity, d.AttackPower, swingSec * 1000, c);
            var hit = raw * (1 - CombatCalculator.Mitigation(goblin.Armor, level, c));
            dps[classId] = hit * (1 - c.PhysicalMissBase) * (1 + d.CritChancePhysical * (c.CritMultiplier - 1)) / swingSec;
            var goblinHit = (goblin.DamageMin + goblin.DamageMax) / 2.0 * (1 - CombatCalculator.Mitigation(d.Armor, goblin.Level, c))
                            * (1 - c.PhysicalMissBase) * (1 - d.DodgeChance) * (1 + c.CritBase * (c.CritMultiplier - 1));
            ttk[classId] = d.MaxHp / (goblinHit / (goblin.AttackSpeedMs / 1000.0));
        }
        var dmgRange = rules.BalanceTargets.OffRoleDamagePct;
        var survRange = rules.BalanceTargets.OffRoleSurvivalPct;
        (dps["priest"] / dps["rogue"]).ShouldBeInRange(dmgRange[0], dmgRange[1]);
        (ttk["mage"] / ttk["warrior"]).ShouldBeInRange(survRange[0], survRange[1]);
    }

    public static IEnumerable<object[]> AvailableClassSpells()
    {
        var db = TestContent.Load();
        foreach (var cls in db.Classes)
            foreach (var s in db.ClassSpells(cls.Id))
                yield return [cls.Id, s.Id];
    }

    [Theory]
    [MemberData(nameof(AvailableClassSpells))]
    public void EveryClassSpell_OnDummy_NoExceptions_AtLeastOneEvent_OrUnavailable(string classId, string spellId) // CA4 (ampliado por HU-086/087)
    {
        var db = TestContent.Load();
        var spell = db.Spell(spellId);
        if (EngineCapabilities.UnavailableReason(spell) is not null)
        {
            // No disponibles (cono/línea): no se aprenden.
            db.KnownSpells(classId, 15).ShouldNotContain(spell);
            return;
        }
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", classId, 15, (10.5f, 10.5f))
            .WithMonster("rubble_golem", (11.5f, 10.5f), wanderRadius: 0).BuildWithCombat(); // a 1 casilla: alcance melee
        var ana = w.Player("Ana"); var dummy = w.Monster("rubble_golem");
        ana.Resource = ana.MaxResource; // ira llena para probar los del Guerrero
        if (spell.Effects.Any(e => e.Type == EffectType.Dash)) dummy.Position = new Vec2(15.5f, 10.5f); // Carga exige distancia mínima
        var ctx = w.Begin();
        Vec2? pos = spell.Targeting.IsGround() || spell.Effects.Any(e => e.Type == EffectType.Leap) ? dummy.Position : null;
        var err = w.Combat.Casts.TryBeginCast(ana, spell, dummy.Id, pos, w.Map, ctx);
        err.ShouldBeNull($"{spellId}: {err}");
        var events = new List<IGameEvent>(ctx.Events);
        if (!spell.IsInstant) events.AddRange(TickRunner.RunMs(w, spell.CastMs + 50));
        if (spell.Projectile is not null) events.AddRange(TickRunner.RunMs(w, 1000));
        events.ShouldNotBeEmpty();
        events.Any(e => e is CombatHitEvent or AuraAppliedEvent or ForcedMoveEvent or CastEndedEvent or CooldownEvent)
      .ShouldBeTrue("Se esperaba al menos un evento de combate/aura/movimiento/cast/cooldown");
    }
}
