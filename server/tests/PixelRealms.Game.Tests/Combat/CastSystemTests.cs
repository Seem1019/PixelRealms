using PixelRealms.Content.Defs;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Items;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Combat;

/// <summary>HU-033: casteo, GCD/CD/recurso, interrupciones, revalidación al terminar, proyectiles y un test por código de error.</summary>
public sealed class CastSystemTests
{
    private static TestWorld Arena(string classId = "mage", int level = 3, float distance = 4f, string monster = "slime")
        => new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", classId, level, (10, 10)).WithMonster(monster, (10 + distance, 10), wanderRadius: 0).BuildWithCombat();

    private static string? Cast(TestWorld w, string spellId, EntityId? target = null, Vec2? pos = null)
        => w.Combat.Casts.TryBeginCast(w.Player("Ana"), w.Content.Spell(spellId), target, pos, w.Map, w.Begin());

    [Fact]
    public void Silence_BlocksEveryAbility_ButNotPotionsNorTheWeapon() // HU-035 CA3 (decisión 2026-10-03)
    {
        var w = Arena("warrior", level: 3, distance: 1f);
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        ana.Resource = ana.MaxResource;
        var silence = w.Content.Aura("warrior_charge_stun") with { Id = "test_silence", Kind = AuraKind.Silence };
        w.Combat.Auras.Apply(ana, silence, slime, w.Map, w.Begin());
        Cast(w, "warrior_heroic_strike", slime.Id).ShouldBe(CastErrors.Silenced); // también las físicas

        ana.Hp = ana.MaxHp / 2;
        var potion = ItemInstance.New("minor_healing_potion", 1);
        ana.Inventory.Bag[0] = potion;
        w.Combat.ItemUse.Use(ana, potion.Id, w.Map, w.Begin()).ShouldBeNull(); // la poción sí
        ana.Hp.ShouldBeGreaterThan(ana.MaxHp / 2);

        ana.Combat.TargetId = slime.Id; ana.Combat.AutoAttackOn = true; // y el ataque con el arma
        TickRunner.RunMs(w, 3000).OfType<CombatHitEvent>().ShouldContain(e => ReferenceEquals(e.Source, ana) && e.SpellId == null);
    }

    [Fact]
    public void InterruptLockout_BlocksAbilities_ButNotPotions() // HU-035 CA3: como el silencio
    {
        var w = Arena("mage", level: 3);
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        ana.Combat.LockoutEndsAtMs = w.Clock.NowMs + 5000;
        Cast(w, "mage_fireball", slime.Id).ShouldBe(CastErrors.LockedOut);
        ana.Hp = ana.MaxHp / 2;
        var potion = ItemInstance.New("minor_healing_potion", 1);
        ana.Inventory.Bag[0] = potion;
        w.Combat.ItemUse.Use(ana, potion.Id, w.Map, w.Begin()).ShouldBeNull();
    }

    [Fact]
    public void Bread_OnlyOutOfCombat_ADamageStopsIt_AndItHasAMinuteCooldown() // decisión 2026-10-03
    {
        var w = Arena("warrior", level: 3, distance: 8f);
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        ana.Hp = ana.MaxHp / 2;
        var bread = ItemInstance.New("bread", 3);
        ana.Inventory.Bag[0] = bread;

        ana.EnterCombat(w.Clock.NowMs);
        w.Combat.ItemUse.Use(ana, bread.Id, w.Map, w.Begin()).ShouldBe(CastErrors.InCombat); // en combate no se come
        bread.Qty.ShouldBe(3);

        ana.LastCombatAtMs = long.MinValue;
        w.Combat.ItemUse.Use(ana, bread.Id, w.Map, w.Begin()).ShouldBeNull();
        bread.Qty.ShouldBe(2);
        w.Combat.ItemUse.Use(ana, bread.Id, w.Map, w.Begin()).ShouldBe(CastErrors.OnCooldown);
        w.Content.Item("bread").UseCooldownMs.ShouldBe(60000);
        var hp = ana.Hp;
        TickRunner.RunMs(w, w.Content.Aura("bread_hot").TickMs);
        ana.Hp.ShouldBeGreaterThan(hp); // cura mientras nadie le pega

        w.Combat.Damage.Deal(slime, ana, 1, School.Physical, false, null, w.Map, w.Begin()); // un golpe corta la comida
        ana.Auras.All.ShouldNotContain(a => a.AuraId == "bread_hot");

        // También si un escudo para el golpe entero: el golpe fue real.
        ana.LastCombatAtMs = long.MinValue;
        ana.ItemCooldownEndsAtMs.Clear();
        w.Combat.ItemUse.Use(ana, bread.Id, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Auras.Apply(ana, w.Content.Aura("priest_power_shield_aura"), ana, w.Map, w.Begin()).ShouldNotBeNull();
        var hpBefore = ana.Hp;
        w.Combat.Damage.Deal(slime, ana, 1, School.Physical, false, null, w.Map, w.Begin());
        ana.Hp.ShouldBe(hpBefore);
        ana.Auras.All.ShouldNotContain(a => a.AuraId == "bread_hot");
    }

    [Fact]
    public void GroundArea_AimedInsideAWallThatBlocksSight_IsRejected()
    {
        // Un muro de una casilla en (12, 10) y el slime detrás: apuntar dentro del muro alcanzaba al otro lado.
        var w = Arena(level: 5, distance: 3.5f);
        w.Map.Data.Collision.SetBlocksSight(12, 10);
        Cast(w, "mage_flame_burst", pos: new Vec2(12.5f, 10.5f)).ShouldBe(CastErrors.NoLos);
        Cast(w, "mage_flame_burst", pos: new Vec2(11.5f, 10.5f)).ShouldBeNull(); // justo delante del muro sí
    }

    [Fact]
    public void Fireball_CastStarted_ResolvesAfter40Ticks_SpendsContentCost() // CA1 + skill §Integración de tick
    {
        var w = Arena();
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        var fireball = w.Content.Spell("mage_fireball");
        var manaBefore = ana.Resource;
        Cast(w, "mage_fireball", slime.Id).ShouldBeNull();
        var started = w.Ctx.Events.OfType<CastStartedEvent>().Single();
        started.DurationMs.ShouldBe(fireball.CastMs);
        var before = TickRunner.Run(w, 39);
        before.OfType<CastEndedEvent>().ShouldBeEmpty();
        ana.Resource.ShouldBe(manaBefore); // se descuenta al terminar
        var at40 = TickRunner.Run(w, 1);
        at40.OfType<CastEndedEvent>().Single().Result.ShouldBe(CastResults.Done);
        ana.Resource.ShouldBe(manaBefore - fireball.Cost!.Amount);
        // Proyectil: el impacto llega distancia / speed después (4 casillas a 12/s ≈ 333 ms → 7 ticks).
        at40.OfType<CombatHitEvent>().ShouldBeEmpty();
        var travelMs = (long)Math.Round(4 / fireball.Projectile!.Speed * 1000);
        var flight = TickRunner.Run(w, (int)Math.Ceiling(travelMs / (double)GameConstants.TickMs) - 1);
        flight.OfType<CombatHitEvent>().ShouldBeEmpty();
        var impact = TickRunner.Run(w, 1);
        impact.OfType<CombatHitEvent>().Single(e => e.Target == slime).Kind.ShouldBeOneOf(HitKinds.Damage, HitKinds.Miss);
    }

    [Fact]
    public void Smite_NoProjectile_HitsSameTick()
    {
        var w = Arena("priest");
        Cast(w, "priest_smite", w.Monster("slime").Id).ShouldBeNull();
        var done = TickRunner.RunMs(w, w.Content.Spell("priest_smite").CastMs);
        done.OfType<CastEndedEvent>().Single().Result.ShouldBe(CastResults.Done);
        done.OfType<CombatHitEvent>().Count(e => e.SpellId == "priest_smite").ShouldBe(1);
    }

    [Fact]
    public void MovingWhileCasting_HalvesSpeed_DoesNotInterrupt() // CA2
    {
        var w = Arena();
        var ana = w.Player("Ana");
        Cast(w, "mage_fireball", w.Monster("slime").Id).ShouldBeNull();
        var ctx = w.Begin();
        w.Combat.Movement.SpeedOf(ana, ctx).ShouldBe((float)(ana.BaseSpeed * w.Content.Rules.Combat.CastMoveSpeedMult), 1e-5f);
        ana.MoveDx = -1; ana.LastInputAtMs = long.MaxValue / 2;
        var events = TickRunner.Run(w, 10);
        events.OfType<CastEndedEvent>().ShouldBeEmpty();
        ana.Position.X.ShouldBeLessThan(10);
        // Recibir daño tampoco lo corta.
        w.Combat.Damage.Deal(w.Monster("slime"), ana, 5, School.Physical, false, null, w.Map, w.Begin());
        TickRunner.Run(w, 1).OfType<CastEndedEvent>().ShouldBeEmpty();
    }

    [Fact]
    public void Stun_Interrupts_NoCost_Lockout() // CA2b
    {
        var w = Arena();
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        var manaBefore = ana.Resource;
        Cast(w, "mage_fireball", slime.Id).ShouldBeNull();
        var ctx = w.Begin();
        w.Combat.Auras.Apply(ana, w.Content.Aura("warrior_charge_stun"), slime, w.Map, ctx);
        ctx.Events.OfType<CastEndedEvent>().Single().Result.ShouldBe(CastResults.Interrupted);
        ana.Resource.ShouldBe(manaBefore);
        ana.Combat.IsOnGcd(ctx.NowMs).ShouldBeTrue(); // el GCD sigue corriendo
        // Bloqueado interruptLockoutMs aunque pase el aturdimiento.
        w.Combat.Auras.ClearAll(ana, w.Map, ctx);
        ana.Combat.GcdEndsAtMs = long.MinValue;
        Cast(w, "mage_fireball", slime.Id).ShouldBe(CastErrors.LockedOut);
        w.Clock.Advance(w.Content.Rules.Combat.InterruptLockoutMs);
        Cast(w, "mage_fireball", slime.Id).ShouldBeNull();
    }

    [Fact]
    public void RootAndSlow_DoNotInterrupt()
    {
        var w = Arena();
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        Cast(w, "mage_fireball", slime.Id).ShouldBeNull();
        var ctx = w.Begin();
        w.Combat.Auras.Apply(ana, w.Content.Aura("mage_frost_nova_root"), slime, w.Map, ctx);
        w.Combat.Auras.Apply(ana, w.Content.Aura("mage_chill"), slime, w.Map, ctx);
        ctx.Events.OfType<CastEndedEvent>().ShouldBeEmpty();
    }

    [Fact]
    public void TargetOutOfRangeAtEnd_Failed_NoCost_NoCooldown() // CA2c
    {
        var w = Arena();
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        var frostbolt = w.Content.Spell("mage_frostbolt");
        var manaBefore = ana.Resource;
        Cast(w, "mage_frostbolt", slime.Id).ShouldBeNull();
        slime.Position = new Vec2(10 + (float)(frostbolt.Range + w.Content.Rules.Combat.CastRangeToleranceTiles) + 0.5f, 10);
        var ended = TickRunner.RunMs(w, frostbolt.CastMs).OfType<CastEndedEvent>().Single();
        ended.Result.ShouldBe(CastResults.Failed);
        ended.Reason.ShouldBe(CastErrors.OutOfRange);
        ana.Resource.ShouldBe(manaBefore);
        ana.Combat.IsOnCooldown("mage_frostbolt", w.Clock.NowMs).ShouldBeFalse();
    }

    [Fact]
    public void WithinTolerance_StillResolves()
    {
        var w = Arena();
        var slime = w.Monster("slime");
        var frostbolt = w.Content.Spell("mage_frostbolt");
        Cast(w, "mage_frostbolt", slime.Id).ShouldBeNull();
        slime.Position = new Vec2(10 + (float)frostbolt.Range + 1f, 10); // dentro de la tolerancia (1.5)
        TickRunner.RunMs(w, frostbolt.CastMs).OfType<CastEndedEvent>().Single().Result.ShouldBe(CastResults.Done);
    }

    [Fact]
    public void GroundArea_PointIsFixed_NotRevalidated() // CA2d
    {
        var w = Arena(level: 5);
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        var burst = w.Content.Spell("mage_flame_burst");
        Cast(w, "mage_flame_burst", null, slime.Position).ShouldBeNull();
        ana.Position = new Vec2(30, 30); // se aleja muchísimo
        var events = TickRunner.RunMs(w, burst.CastMs);
        events.OfType<CastEndedEvent>().Single().Result.ShouldBe(CastResults.Done);
        events.OfType<CombatHitEvent>().Count(e => e.Target == slime).ShouldBe(1);
    }

    [Fact]
    public void NewCast_CancelsCurrent_NoCost() // CA2e
    {
        var w = Arena();
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        var manaBefore = ana.Resource;
        Cast(w, "mage_fireball", slime.Id).ShouldBeNull();
        TickRunner.Run(w, 10);
        ana.Combat.GcdEndsAtMs = long.MinValue;
        Cast(w, "mage_frostbolt", slime.Id).ShouldBeNull();
        var ended = w.Ctx.Events.OfType<CastEndedEvent>().Single();
        ended.Spell.Id.ShouldBe("mage_fireball");
        ended.Result.ShouldBe(CastResults.Cancelled);
        ana.Resource.ShouldBe(manaBefore);
        ana.Combat.Cast!.Spell.Id.ShouldBe("mage_frostbolt");
    }

    [Fact]
    public void ErrorCodes_EachValidation() // CA3, CA4
    {
        var w = Arena(level: 3);
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        var rules = w.Content.Rules.Combat;

        Cast(w, "mage_fireball", slime.Id).ShouldBeNull();               // arranca el GCD
        Cast(w, "mage_frostbolt", slime.Id).ShouldBe(CastErrors.OnGcd);   // on_gcd
        w.Clock.Advance(rules.GcdMs);
        w.Combat.Casts.Cancel(ana, w.Map, w.Begin());

        Cast(w, "mage_frost_nova").ShouldBeNull();                        // instantáneo con cooldown
        w.Clock.Advance(rules.GcdMs);
        Cast(w, "mage_frost_nova").ShouldBe(CastErrors.OnCooldown);       // on_cooldown

        ana.Resource = 0;
        Cast(w, "mage_fireball", slime.Id).ShouldBe(CastErrors.NotEnoughResource); // not_enough_resource
        ana.Resource = ana.MaxResource;

        slime.Position = new Vec2(10 + (float)w.Content.Spell("mage_fireball").Range + 1, 10);
        Cast(w, "mage_fireball", slime.Id).ShouldBe(CastErrors.OutOfRange);        // out_of_range
        slime.Position = new Vec2(14, 10);

        w.Map.Data.Collision.SetBlocksSight(12, 10);
        Cast(w, "mage_fireball", slime.Id).ShouldBe(CastErrors.NoLos);             // no_los
        w.Map.Data.Collision.SetBlocksSight(12, 10, false);

        Cast(w, "mage_fireball", ana.Id).ShouldBe(CastErrors.InvalidTarget);       // invalid_target (uno mismo no es enemigo)
        Cast(w, "mage_fireball", null).ShouldBe(CastErrors.InvalidTarget);

        var ctx = w.Begin();
        w.Combat.Auras.Apply(ana, w.Content.Aura("warrior_charge_stun"), slime, w.Map, ctx);
        Cast(w, "mage_fireball", slime.Id).ShouldBe(CastErrors.Stunned);           // stunned
        w.Combat.Auras.ClearAll(ana, w.Map, w.Begin());

        // silenced: no hay aura silence en el contenido; se construye una de prueba.
        var silence = w.Content.Aura("warrior_charge_stun") with { Id = "test_silence", Kind = AuraKind.Silence };
        w.Combat.Auras.Apply(ana, silence, slime, w.Map, w.Begin());
        Cast(w, "mage_fireball", slime.Id).ShouldBe(CastErrors.Silenced);          // silenced (magic)
        w.Combat.Auras.ClearAll(ana, w.Map, w.Begin());

        var wr = Arena("rogue", level: 3);
        w.Combat.Auras.Apply(wr.Player("Ana"), wr.Content.Aura("mage_frost_nova_root"), wr.Monster("slime"), wr.Map, wr.Begin());
        Cast(wr, "rogue_shadowstep", null, new Vec2(13, 10)).ShouldBe(CastErrors.Rooted); // rooted (salto)

        var ww = Arena("warrior", level: 3, distance: 5f); // decisión 2026-10-03: la Carga tampoco sale de una raíz
        ww.Player("Ana").Resource = ww.Player("Ana").MaxResource;
        ww.Combat.Auras.Apply(ww.Player("Ana"), ww.Content.Aura("mage_frost_nova_root"), ww.Monster("slime"), ww.Map, ww.Begin());
        Cast(ww, "warrior_charge", ww.Monster("slime").Id).ShouldBe(CastErrors.Rooted); // rooted (carga)
        ww.Combat.Auras.ClearAll(ww.Player("Ana"), ww.Map, ww.Begin());
        Cast(ww, "warrior_charge", ww.Monster("slime").Id).ShouldBeNull();

        Cast(w, "mage_meteor", null, slime.Position).ShouldBe(CastErrors.NotFound); // no conocido (nivel 13)
        Cast(w, "mage_flame_burst", null, new Vec2(float.NaN, 1)).ShouldBe(CastErrors.NotFound); // no conocido a nivel 3

        var w5 = Arena(level: 5);
        Cast(w5, "mage_flame_burst", null, new Vec2(float.NaN, 1)).ShouldBe(CastErrors.InvalidPayload); // invalid_payload
        Cast(w5, "mage_flame_burst", null, new Vec2(10 + (float)(w5.Content.Spell("mage_flame_burst").Range + rules.CastRangeToleranceTiles) + 1, 10)).ShouldBe(CastErrors.OutOfRange);

        w.Combat.Death.Kill(ana, slime, w.Map, w.Begin());
        Cast(w, "mage_fireball", slime.Id).ShouldBe(CastErrors.IsDead);            // is_dead
    }

    [Fact]
    public void GroundArea_AtTheInstanceLimit_IsAreaLimit() // HU-033 CA4, HU-086 CA7b
    {
        using var tmp = new TempContent();
        tmp.Patch("rules.json", n => n["limits"]!["maxAreasPerInstance"] = 1);
        var content = PixelRealms.Content.ContentLoader.LoadOrThrow(tmp.Path);
        var w = new WorldBuilder(content).WithMap(40, 40).WithPlayer("Ana", "mage", 5, (10, 10)).WithPlayer("Bob", "mage", 5, (10, 14))
            .WithMonster("slime", (14, 12), wanderRadius: 0).BuildWithCombat();
        var burst = w.Content.Spell("mage_flame_burst"); // 1,5 s de casteo: su marca cuenta mientras dura
        w.Combat.Casts.TryBeginCast(w.Player("Ana"), burst, null, new Vec2(13, 12), w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Casts.ActiveAreas(w.Map).ShouldBe(1);
        w.Combat.Casts.TryBeginCast(w.Player("Bob"), burst, null, new Vec2(13, 12), w.Map, w.Begin()).ShouldBe(CastErrors.AreaLimit);
        TickRunner.RunMs(w, burst.CastMs + 50); // la primera termina: hay sitio otra vez
        w.Combat.Casts.ActiveAreas(w.Map).ShouldBe(0);
        w.Combat.Casts.TryBeginCast(w.Player("Bob"), burst, null, new Vec2(13, 12), w.Map, w.Begin()).ShouldBeNull();
    }

    [Fact]
    public void PlayerCast_ItemOrMonsterSpell_IsNotFound_ButTheItemStillWorksThroughUseItem()
    {
        // Cliente tramposo: CastSpell con el hechizo de la poción (cura sin recarga ni consumo) o con uno de monstruo.
        var w = Arena(level: 3);
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        ana.Hp = 10;
        Cast(w, "item_minor_heal", ana.Id).ShouldBe(CastErrors.NotFound);
        Cast(w, "item_minor_mana", ana.Id).ShouldBe(CastErrors.NotFound);
        Cast(w, "lich_shadow_bolt", slime.Id).ShouldBe(CastErrors.NotFound);
        Cast(w, "foreman_rally").ShouldBe(CastErrors.NotFound);
        ana.Hp.ShouldBe(10);

        var potion = ItemInstance.New("minor_healing_potion", 1);
        ana.Inventory.Bag[0] = potion;
        w.Combat.ItemUse.Use(ana, potion.Id, w.Map, w.Begin()).ShouldBeNull();
        ana.Hp.ShouldBeGreaterThan(10);
        ana.Inventory.Bag[0].ShouldBeNull();
    }

    [Fact]
    public void AllySpell_WithoutAllyTarget_CastsOnSelf() // CA6
    {
        var w = Arena("priest");
        var ana = w.Player("Ana");
        ana.Hp = 10;
        Cast(w, "priest_heal", w.Monster("slime").Id).ShouldBeNull(); // el slime no es aliado → sobre mí
        var events = TickRunner.RunMs(w, w.Content.Spell("priest_heal").CastMs);
        events.OfType<CombatHitEvent>().Single(e => e.Kind == HitKinds.Heal).Target.ShouldBe(ana);
        ana.Hp.ShouldBeGreaterThan(10);
    }
}
