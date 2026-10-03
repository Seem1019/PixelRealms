using PixelRealms.Content.Defs;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Items;
using PixelRealms.Game.Progression;
using PixelRealms.Game.Social;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Coverage;

/// <summary>
/// Criterios de aceptación ya implementados que no tenían test (HU-033, HU-034, HU-035, HU-036, HU-039, HU-040, HU-052, HU-059,
/// HU-064) y los bordes que provocaría un tramposo o un caso raro: objetivo muerto, 0,01 casillas de más, ids inexistentes o
/// ajenos, dos mensajes en el mismo tick, desconexión a mitad de operación.
/// </summary>
public sealed class AcceptanceGapTests
{
    // ------------------------------------------------------------------------------------------------- HU-033 (casteo)

    private static TestWorld MageArena(IRng? rng = null)
    {
        var builder = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "mage", 3, (10, 10)).WithMonster("slime", (14, 10), wanderRadius: 0);
        if (rng is not null) builder.WithRng(rng);
        return builder.BuildWithCombat();
    }

    private static string? Cast(TestWorld w, string spellId, EntityId? target = null, Vec2? pos = null)
        => w.Combat.Casts.TryBeginCast(w.Player("Ana"), w.Content.Spell(spellId), target, pos, w.Map, w.Begin());

    /// <summary>No hay ningún aura `silence` en el contenido: se construye una de prueba a partir de un aturdimiento.</summary>
    private static AuraDef Silence(TestWorld w) => w.Content.Aura("warrior_charge_stun") with { Id = "test_silence", Name = "Silencio", Kind = AuraKind.Silence };

    /// <summary>Gubia es el único hechizo con `interrupt`; se aísla ese efecto (su aturdimiento también cortaría el casteo).</summary>
    private static SpellDef InterruptOnly(TestWorld w)
    {
        var gouge = w.Content.Spell("rogue_gouge");
        return gouge with { Effects = [gouge.Effects.Single(e => e.Type == EffectType.Interrupt)] };
    }

    [Fact]
    public void Cast_SilenceDuringMagicCast_InterruptsWithoutCost_AndLocksOut() // HU-033 CA2b
    {
        var w = MageArena();
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        var lockoutMs = w.Content.Rules.Combat.InterruptLockoutMs;
        var manaBefore = ana.Resource;
        Cast(w, "mage_fireball", slime.Id).ShouldBeNull();
        var ctx = w.Begin();
        w.Combat.Auras.Apply(ana, Silence(w), slime, w.Map, ctx).ShouldNotBeNull();
        var ended = ctx.Events.OfType<CastEndedEvent>().Single();
        ended.Spell.Id.ShouldBe("mage_fireball");
        ended.Result.ShouldBe(CastResults.Interrupted);
        ana.Combat.IsCasting.ShouldBeFalse();
        ana.Resource.ShouldBe(manaBefore);
        ana.Combat.IsOnGcd(ctx.NowMs).ShouldBeTrue(); // el GCD sigue corriendo
        // Sin el silencio queda el bloqueo de interruptLockoutMs: locked_out hasta el último ms, y luego vuelve a castear.
        w.Combat.Auras.ClearAll(ana, w.Map, w.Begin());
        Cast(w, "mage_fireball", slime.Id).ShouldBe(CastErrors.LockedOut);
        w.Clock.Advance(lockoutMs - 1);
        Cast(w, "mage_fireball", slime.Id).ShouldBe(CastErrors.LockedOut);
        w.Clock.Advance(1);
        Cast(w, "mage_fireball", slime.Id).ShouldBeNull();
    }

    [Fact]
    public void Cast_SilenceDuringPhysicalCast_AlsoInterrupts() // HU-033 CA2b; el silencio corta cualquier habilidad (HU-035 CA3)
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "mage", 3, (10, 10)).WithMonster("goblin_archer", (10, 15), wanderRadius: 0).BuildWithCombat();
        var ana = w.Player("Ana"); var goblin = w.Monster("goblin_archer");
        var shoot = w.Content.Spell("goblin_shoot");
        shoot.School.ShouldBe(School.Physical);
        w.Combat.Casts.TryBeginCast(goblin, shoot, ana.Id, null, w.Map, w.Begin()).ShouldBeNull();
        var ctx = w.Begin();
        w.Combat.Auras.Apply(goblin, Silence(w), ana, w.Map, ctx).ShouldNotBeNull();
        ctx.Events.OfType<CastEndedEvent>().Single().Result.ShouldBe(CastResults.Interrupted);
        goblin.Combat.Cast.ShouldBeNull();
    }

    [Fact]
    public void Cast_InterruptEffect_CutsTheCast_AndLocksOutForInterruptLockoutMs() // HU-033 CA2b
    {
        var w = MageArena(new FixedRng(0.5)); // 0,5: la tirada de impacto acierta sin crítico
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        var lockoutMs = w.Content.Rules.Combat.InterruptLockoutMs;
        var manaBefore = ana.Resource;
        Cast(w, "mage_fireball", slime.Id).ShouldBeNull();
        var ctx = w.Begin();
        w.Combat.Effects.Apply(slime, InterruptOnly(w), ana.Id, null, slime.Position, w.Map, ctx);
        var ended = ctx.Events.OfType<CastEndedEvent>().Single();
        ended.Spell.Id.ShouldBe("mage_fireball");
        ended.Result.ShouldBe(CastResults.Interrupted);
        ana.Auras.Count.ShouldBe(0); // ni aturdimiento ni silencio: lo corta el efecto solo
        ana.Resource.ShouldBe(manaBefore);
        ana.Combat.IsOnGcd(ctx.NowMs).ShouldBeTrue();
        Cast(w, "mage_fireball", slime.Id).ShouldBe(CastErrors.LockedOut);
        w.Clock.Advance(lockoutMs - 1);
        Cast(w, "mage_fireball", slime.Id).ShouldBe(CastErrors.LockedOut);
        w.Clock.Advance(1);
        Cast(w, "mage_fireball", slime.Id).ShouldBeNull();
    }

    [Fact]
    public void Cast_InterruptEffectOnSomeoneNotCasting_DoesNotLockThemOut() // HU-033 CA2b (borde: no hay casteo que cortar)
    {
        var w = MageArena(new FixedRng(0.5));
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        var ctx = w.Begin();
        w.Combat.Effects.Apply(slime, InterruptOnly(w), ana.Id, null, slime.Position, w.Map, ctx);
        ctx.Events.OfType<CastEndedEvent>().ShouldBeEmpty();
        ana.Combat.IsLockedOut(ctx.NowMs).ShouldBeFalse();
        Cast(w, "mage_fireball", slime.Id).ShouldBeNull();
    }

    [Fact]
    public void Cast_TargetOutOfSightAtEnd_FailsWithNoLos_NoCostNoCooldown() // HU-033 CA2c
    {
        var w = MageArena();
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        var frostbolt = w.Content.Spell("mage_frostbolt"); // tiene recarga: se comprueba que no empieza
        var manaBefore = ana.Resource;
        Cast(w, "mage_frostbolt", slime.Id).ShouldBeNull();
        w.Map.Data.Collision.SetBlocksSight(12, 10); // un muro entre los dos a mitad del casteo
        var events = TickRunner.RunMs(w, frostbolt.CastMs);
        var ended = events.OfType<CastEndedEvent>().Single();
        ended.Result.ShouldBe(CastResults.Failed);
        ended.Reason.ShouldBe(CastErrors.NoLos);
        ana.Resource.ShouldBe(manaBefore);
        ana.Combat.IsOnCooldown("mage_frostbolt", w.Clock.NowMs).ShouldBeFalse();
        events.OfType<CooldownEvent>().ShouldNotContain(e => e.SpellId == "mage_frostbolt");
        TickRunner.RunMs(w, 2000).OfType<CombatHitEvent>().ShouldNotContain(e => e.SpellId == "mage_frostbolt"); // ni proyectil
    }

    [Fact]
    public void Cast_TargetDiesDuringCast_FailsWithInvalidTarget_NoCostNoCooldown() // HU-033 CA2c (borde: objetivo muerto)
    {
        var w = MageArena();
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        var frostbolt = w.Content.Spell("mage_frostbolt");
        var manaBefore = ana.Resource;
        Cast(w, "mage_frostbolt", slime.Id).ShouldBeNull();
        w.Combat.Death.Kill(slime, null, w.Map, w.Begin()); // lo remata otro antes de que termine
        var ended = TickRunner.RunMs(w, frostbolt.CastMs).OfType<CastEndedEvent>().Single();
        ended.Result.ShouldBe(CastResults.Failed);
        ended.Reason.ShouldBe(CastErrors.InvalidTarget);
        ana.Resource.ShouldBe(manaBefore);
        ana.Combat.IsOnCooldown("mage_frostbolt", w.Clock.NowMs).ShouldBeFalse();
    }

    [Fact]
    public void Cast_TargetOneHundredthPastToleranceAtEnd_Fails_ButExactlyAtToleranceResolves() // HU-033 CA2c (borde: 0,01 casillas)
    {
        var frostbolt = TestContent.Load().Spell("mage_frostbolt");
        var limit = (float)(frostbolt.Range + TestContent.Load().Rules.Combat.CastRangeToleranceTiles);

        var past = MageArena();
        Cast(past, "mage_frostbolt", past.Monster("slime").Id).ShouldBeNull();
        past.Monster("slime").Position = new Vec2(10 + limit + 0.01f, 10);
        var failed = TickRunner.RunMs(past, frostbolt.CastMs).OfType<CastEndedEvent>().Single();
        failed.Result.ShouldBe(CastResults.Failed);
        failed.Reason.ShouldBe(CastErrors.OutOfRange);

        var at = MageArena();
        Cast(at, "mage_frostbolt", at.Monster("slime").Id).ShouldBeNull();
        at.Monster("slime").Position = new Vec2(10 + limit, 10);
        TickRunner.RunMs(at, frostbolt.CastMs).OfType<CastEndedEvent>().Single().Result.ShouldBe(CastResults.Done);
    }

    [Fact]
    public void Cast_DrinkingAPotionMidCast_DoesNotCancelIt_AndBothResolve() // HU-033 CA2e
    {
        var w = MageArena();
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        var fireball = w.Content.Spell("mage_fireball");
        var restore = (int)w.Content.Spell("item_minor_mana").Effects.Single(e => e.Type == EffectType.RestoreResource).Amount;
        var potion = ItemInstance.New("minor_mana_potion", 2);
        ana.Inventory.Bag[0] = potion;
        ana.Resource = 20;
        Cast(w, "mage_fireball", slime.Id).ShouldBeNull();
        var gcdEnds = ana.Combat.GcdEndsAtMs;
        const int midTicks = 10;
        TickRunner.Run(w, midTicks); // a mitad del casteo
        var manaMid = ana.Resource;
        var ctx = w.Begin();
        w.Combat.ItemUse.Use(ana, potion.Id, w.Map, ctx).ShouldBeNull();
        ctx.Events.OfType<CastEndedEvent>().ShouldNotContain(e => e.Spell.Id == "mage_fireball");
        ana.Combat.Cast.ShouldNotBeNull().Spell.Id.ShouldBe("mage_fireball");
        ana.Resource.ShouldBe(manaMid + restore);
        ana.Combat.GcdEndsAtMs.ShouldBe(gcdEnds); // el usable no toca el GCD
        potion.Qty.ShouldBe(1);
        var rest = TickRunner.RunMs(w, fireball.CastMs - midTicks * GameConstants.TickMs);
        rest.OfType<CastEndedEvent>().Single(e => e.Spell.Id == "mage_fireball").Result.ShouldBe(CastResults.Done);
    }

    // ------------------------------------------------------------------------------------------------- HU-034 (efectos)

    [Fact]
    public void ItemUse_ManaPotion_RestoresExactlyItsAmount_ClampedAtMax() // HU-034 CA1 (restore_resource)
    {
        var w = MageArena();
        var ana = w.Player("Ana");
        var amount = (int)w.Content.Spell("item_minor_mana").Effects.Single(e => e.Type == EffectType.RestoreResource).Amount;
        var potions = ItemInstance.New("minor_mana_potion", 2);
        ana.Inventory.Bag[0] = potions;
        ana.Resource = 10;
        w.Combat.ItemUse.Use(ana, potions.Id, w.Map, w.Begin()).ShouldBeNull();
        ana.Resource.ShouldBe(10 + amount);
        // Cerca del máximo no se pasa (se espera a que acabe la recarga compartida).
        w.Clock.Advance(w.Content.Item("minor_mana_potion").UseCooldownMs);
        ana.Resource = ana.MaxResource - amount / 2;
        w.Combat.ItemUse.Use(ana, potions.Id, w.Map, w.Begin()).ShouldBeNull();
        ana.Resource.ShouldBe(ana.MaxResource);
        ana.Inventory.Bag[0].ShouldBeNull();
    }

    [Fact]
    public void ItemUse_ManaPotionOnARageUser_DoesNotTouchRage() // HU-034 CA1 (borde: el recurso del efecto no es el suyo)
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "warrior", 3, (10, 10)).BuildWithCombat();
        var ana = w.Player("Ana");
        ana.Resource = 10;
        var potion = ItemInstance.New("minor_mana_potion", 1);
        ana.Inventory.Bag[0] = potion;
        w.Combat.ItemUse.Use(ana, potion.Id, w.Map, w.Begin()).ShouldBeNull();
        ana.Resource.ShouldBe(10);
    }

    [Fact]
    public void ItemUse_UnknownOrSomeoneElsesItemId_IsNotFound() // HU-034 CA1 (borde: ids inexistentes o ajenos)
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "mage", 3, (10, 10)).WithPlayer("Bob", "mage", 3, (11, 10)).BuildWithCombat();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        var bobsPotion = ItemInstance.New("minor_mana_potion", 1);
        bob.Inventory.Bag[0] = bobsPotion;
        ana.Resource = 10;
        w.Combat.ItemUse.Use(ana, Guid.NewGuid(), w.Map, w.Begin()).ShouldBe("not_found");
        w.Combat.ItemUse.Use(ana, bobsPotion.Id, w.Map, w.Begin()).ShouldBe("not_found");
        ana.Resource.ShouldBe(10);
        bobsPotion.Qty.ShouldBe(1);
    }

    /// <summary>
    /// Oración desesperada (nivel 13, tiradas a 0,5: sin crítico, variance 1) de Ana sobre Bob con 1000 de vida máxima y
    /// `hpFromThreshold(umbral en puntos de vida)` de vida actual. Devuelve la cura y las esperadas sin y con `bonusMult`.
    /// </summary>
    private static (CombatHitEvent Heal, int Plain, int WithBonus) DesperatePrayer(Func<int, int> hpFromThreshold)
    {
        const int maxHp = 1000;
        var w = new WorldBuilder().WithMap(40, 40).WithRng(new FixedRng(0.5)).WithPlayer("Ana", "priest", 13, (10, 10))
            .WithPlayer("Bob", "warrior", 13, (12, 10)).BuildWithCombat();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        var spell = w.Content.Spell("priest_desperate_prayer");
        var heal = spell.Effects.Single(e => e.Type == EffectType.Heal);
        heal.BonusBelowHpPct.ShouldBeGreaterThan(0);
        bob.MaxHp = maxHp;
        bob.Hp = hpFromThreshold((int)Math.Round(maxHp * heal.BonusBelowHpPct));
        var ctx = w.Begin();
        w.Combat.Casts.TryBeginCast(ana, spell, bob.Id, null, w.Map, ctx).ShouldBeNull();
        var hit = ctx.Events.OfType<CombatHitEvent>().Single(e => e.Kind == HitKinds.Heal && e.Target == bob);
        var rules = w.Content.Rules;
        var raw = heal.Base * SpellRanks.BaseMultiplier(rules.Progression, ana.Level) + heal.SpCoef * w.Combat.Services.StatsOf(ana).SpellPower;
        var variance = CombatCalculator.RollVariance(new FixedRng(0.5), rules.Combat);
        return (hit, CombatCalculator.Heal(raw, false, variance, rules.Combat), CombatCalculator.Heal(raw * heal.BonusMult, false, variance, rules.Combat));
    }

    [Fact]
    public void Effects_DesperatePrayerBelowTheThreshold_HealsWithBonusMult_ExactNumbers() // HU-034 CA1 (bonusBelowHpPct)
    {
        var below = DesperatePrayer(threshold => threshold - 1);
        below.Heal.Crit.ShouldBeFalse();
        below.Heal.Amount.ShouldBe(below.WithBonus);
        var healthy = DesperatePrayer(threshold => threshold + 200);
        healthy.Heal.Amount.ShouldBe(healthy.Plain);
        below.Heal.Amount.ShouldBeGreaterThan(healthy.Heal.Amount);
    }

    [Fact]
    public void Effects_DesperatePrayerAtExactlyTheThreshold_HasNoBonus() // HU-034 CA1 (borde: "menos del 40 %", no "hasta")
    {
        var at = DesperatePrayer(threshold => threshold);
        at.Heal.Amount.ShouldBe(at.Plain);
    }

    // ------------------------------------------------------------------------------------------------- HU-035 (auras)

    [Fact]
    public void Auras_SprintImmuneKinds_BlockRootAndSlow_AfterTheGenericImmunityEnded() // HU-035 CA2b
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Bob", "rogue", 5, (10, 10)).WithMonster("slime", (12, 10), wanderRadius: 0).BuildWithCombat();
        var bob = w.Player("Bob"); var slime = w.Monster("slime");
        var root = w.Content.Aura("mage_frost_nova_root");
        var sprint = w.Content.Aura("rogue_sprint_aura");
        w.Combat.Auras.Apply(bob, root, slime, w.Map, w.Begin()).ShouldNotBeNull();
        w.Combat.Casts.TryBeginCast(bob, w.Content.Spell("rogue_sprint"), null, null, w.Map, w.Begin()).ShouldBeNull();
        bob.Auras.IsRooted.ShouldBeFalse(); // la raíz desaparece al instante
        // Quitar la raíz concede además la inmunidad genérica tras control: se deja pasar para aislar `immuneKinds`.
        TickRunner.RunMs(w, (int)(w.Content.Rules.Combat.HardControlImmunitySec * 1000) + GameConstants.TickMs);
        bob.Combat.IsHardControlImmune(w.Clock.NowMs).ShouldBeFalse();
        bob.Auras.Find(sprint.Id, bob.Id).ShouldNotBeNull();
        var ctx = w.Begin();
        w.Combat.Auras.Apply(bob, root, slime, w.Map, ctx).ShouldBeNull();
        w.Combat.Auras.Apply(bob, w.Content.Aura("mage_chill"), slime, w.Map, ctx).ShouldBeNull();
        ctx.Events.OfType<CombatHitEvent>().Count(e => e.Target == bob && e.Kind == HitKinds.Immune).ShouldBe(2);
        bob.Auras.IsRooted.ShouldBeFalse();
        bob.Auras.MaxSlow().ShouldBe(0);
        // Al terminar Carrera (6 s) la Nova vuelve a enraizarlo.
        TickRunner.RunMs(w, sprint.DurationMs);
        bob.Auras.Find(sprint.Id, bob.Id).ShouldBeNull();
        w.Combat.Auras.Apply(bob, root, slime, w.Map, w.Begin()).ShouldNotBeNull();
    }

    /// <summary>Ana con `maxDebuffsPerEntity` perjuicios (debuff_0 es el que antes caduca) y un beneficio propio.</summary>
    private static TestWorld FullOfDebuffs()
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "priest", 7, (10, 10)).WithMonster("slime", (12, 10), wanderRadius: 0).Build();
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        var poison = w.Content.Aura("rogue_poison");
        w.Combat.Auras.Apply(ana, w.Content.Aura("priest_renew_hot"), ana, w.Map, w.Begin()).ShouldNotBeNull();
        for (var i = 0; i < w.Content.Rules.Limits.MaxDebuffsPerEntity; i++)
        {
            w.Clock.Advance(100);
            w.Combat.Auras.Apply(ana, poison with { Id = $"debuff_{i}" }, slime, w.Map, w.Begin()).ShouldNotBeNull();
        }
        return w;
    }

    [Fact]
    public void Auras_SeventeenthDebuff_EvictsTheOneWithLeastTimeLeft_BuffsUntouched() // HU-035 CA7
    {
        var w = FullOfDebuffs();
        var ana = w.Player("Ana");
        var cap = w.Content.Rules.Limits.MaxDebuffsPerEntity;
        ana.Auras.All.Count(a => a.Def.IsDebuff).ShouldBe(cap);
        w.Clock.Advance(100);
        var ctx = w.Begin();
        w.Combat.Auras.Apply(ana, w.Content.Aura("rogue_poison") with { Id = "debuff_new" }, w.Monster("slime"), w.Map, ctx).ShouldNotBeNull(); // la nueva siempre entra
        ctx.Events.OfType<AuraRemovedEvent>().Single().AuraId.ShouldBe("debuff_0");
        ana.Auras.All.Count(a => a.Def.IsDebuff).ShouldBe(cap);
        ana.Auras.All.ShouldContain(a => a.AuraId == "debuff_new");
        ana.Auras.All.ShouldContain(a => a.AuraId == "priest_renew_hot"); // el grupo de beneficios es aparte
    }

    [Fact]
    public void Auras_RenewingADebuffWhileTheGroupIsFull_EvictsNobody() // HU-035 CA7 (borde: renovar no es una aura nueva)
    {
        var w = FullOfDebuffs();
        var ana = w.Player("Ana");
        var ctx = w.Begin();
        w.Combat.Auras.Apply(ana, w.Content.Aura("rogue_poison") with { Id = "debuff_0" }, w.Monster("slime"), w.Map, ctx).ShouldNotBeNull();
        ctx.Events.OfType<AuraRemovedEvent>().ShouldBeEmpty();
        ana.Auras.All.Count(a => a.Def.IsDebuff).ShouldBe(w.Content.Rules.Limits.MaxDebuffsPerEntity);
    }

    [Fact]
    public void Auras_MaxStacks1SameCaster_RenewsFullDurationWithoutStacksOrRhythmReset_AnotherCasterCoexists() // HU-035 CA10
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "priest", 7, (10, 10)).WithPlayer("Cid", "priest", 7, (11, 10)).BuildWithCombat();
        var ana = w.Player("Ana"); var cid = w.Player("Cid");
        var renew = w.Content.Aura("priest_renew_hot");
        renew.MaxStacks.ShouldBe(1);
        ana.Hp = 1;
        var first = w.Combat.Auras.Apply(ana, renew, ana, w.Map, w.Begin()).ShouldNotBeNull();
        TickRunner.RunMs(w, 2000);
        var nextTick = first.NextTickAtMs;
        w.Combat.Auras.Apply(ana, renew, ana, w.Map, w.Begin()).ShouldBeSameAs(first);
        first.Stacks.ShouldBe(1);
        first.ExpiresAtMs.ShouldBe(w.Clock.NowMs + renew.DurationMs); // duración completa
        first.NextTickAtMs.ShouldBe(nextTick);                         // sin reiniciar el ritmo
        var other = w.Combat.Auras.Apply(ana, renew, cid, w.Map, w.Begin()).ShouldNotBeNull();
        other.ShouldNotBeSameAs(first);
        ana.Auras.All.Count(a => a.AuraId == renew.Id).ShouldBe(2);
        // La renovada cura a los 3 s de su primera aplicación; la de Cid aún no.
        var ticks = TickRunner.RunMs(w, (int)(nextTick - w.Clock.NowMs)).OfType<CombatHitEvent>().Where(e => e.SpellId == renew.Id).ToList();
        ticks.ShouldHaveSingleItem().Source.ShouldBe(ana);
    }

    [Fact]
    public void Auras_PoisonFromTwoRogues_TwoInstancesOfUpTo3Stacks_CountTwiceTowardTheCap() // HU-035 CA10
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Rox", "rogue", 13, (10, 10)).WithPlayer("Ziv", "rogue", 13, (11, 10))
            .WithMonster("slime", (12, 10), wanderRadius: 0).Build();
        var rox = w.Player("Rox"); var ziv = w.Player("Ziv"); var slime = w.Monster("slime");
        var poison = w.Content.Aura("rogue_poison");
        for (var i = 0; i < poison.MaxStacks + 1; i++) w.Combat.Auras.Apply(slime, poison, rox, w.Map, w.Begin());
        w.Clock.Advance(100);
        for (var i = 0; i < poison.MaxStacks + 1; i++) w.Combat.Auras.Apply(slime, poison, ziv, w.Map, w.Begin());
        slime.Auras.Count.ShouldBe(2);
        slime.Auras.Find(poison.Id, rox.Id).ShouldNotBeNull().Stacks.ShouldBe(poison.MaxStacks);
        slime.Auras.Find(poison.Id, ziv.Id).ShouldNotBeNull().Stacks.ShouldBe(poison.MaxStacks);
        // 2 venenos + (tope − 2) perjuicios llenan el grupo: el siguiente saca al veneno de Rox (el que antes caduca).
        var cap = w.Content.Rules.Limits.MaxDebuffsPerEntity;
        for (var i = 0; i < cap - 2; i++)
        {
            w.Clock.Advance(100);
            w.Combat.Auras.Apply(slime, poison with { Id = $"debuff_{i}" }, rox, w.Map, w.Begin());
        }
        w.Clock.Advance(100);
        var ctx = w.Begin();
        w.Combat.Auras.Apply(slime, poison with { Id = "debuff_new" }, rox, w.Map, ctx).ShouldNotBeNull();
        var evicted = ctx.Events.OfType<AuraRemovedEvent>().Single();
        evicted.AuraId.ShouldBe(poison.Id);
        evicted.CasterId.ShouldBe(rox.Id);
    }

    [Fact]
    public void Auras_InterruptDuringPostControlImmunity_StillCutsTheCast() // HU-035 CA12
    {
        var w = MageArena(new FixedRng(0.5));
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        var stun = w.Content.Aura("warrior_charge_stun");
        w.Combat.Auras.Apply(ana, stun, slime, w.Map, w.Begin()).ShouldNotBeNull();
        TickRunner.RunMs(w, stun.DurationMs); // termina el aturdimiento: empieza la inmunidad a controles fuertes
        ana.Auras.IsStunned.ShouldBeFalse();
        ana.Combat.IsHardControlImmune(w.Clock.NowMs).ShouldBeTrue();
        Cast(w, "mage_fireball", slime.Id).ShouldBeNull();
        var ctx = w.Begin();
        w.Combat.Effects.Apply(slime, w.Content.Spell("rogue_gouge"), ana.Id, null, slime.Position, w.Map, ctx); // daño + interrupt + aturdimiento
        ctx.Events.OfType<CastEndedEvent>().Single().Result.ShouldBe(CastResults.Interrupted);
        ctx.Events.OfType<CombatHitEvent>().ShouldContain(e => e.Target == ana && e.Kind == HitKinds.Immune); // el aturdimiento no entra
        ana.Auras.IsStunned.ShouldBeFalse();
        Cast(w, "mage_fireball", slime.Id).ShouldBe(CastErrors.LockedOut);
    }

    // ------------------------------------------------------------------------------------------------- HU-036 (IA)

    [Fact]
    public void MonsterAi_NormalMonsterAfterAStun_IsImmuneToHardControlForImmunitySec() // HU-036 CA5d
    {
        var w = new WorldBuilder().WithMap(60, 60).WithPlayer("Ana", "warrior", 5, (5, 5)).WithMonster("wolf", (40, 40), wanderRadius: 0).BuildWithCombat();
        var ana = w.Player("Ana"); var wolf = w.Monster("wolf");
        wolf.IsBoss.ShouldBeFalse();
        var stun = w.Content.Aura("warrior_charge_stun");
        var root = w.Content.Aura("mage_frost_nova_root");
        w.Combat.Auras.Apply(wolf, stun, ana, w.Map, w.Begin()).ShouldNotBeNull();
        TickRunner.RunMs(w, stun.DurationMs);
        wolf.Auras.IsStunned.ShouldBeFalse();
        var ctx = w.Begin();
        w.Combat.Auras.Apply(wolf, w.Content.Aura("rogue_gouge_stun"), ana, w.Map, ctx).ShouldBeNull();
        w.Combat.Auras.Apply(wolf, root, ana, w.Map, ctx).ShouldBeNull();
        w.Combat.Auras.Apply(wolf, Silence(w), ana, w.Map, ctx).ShouldBeNull();
        ctx.Events.OfType<CombatHitEvent>().Count(e => e.Target == wolf && e.Kind == HitKinds.Immune).ShouldBe(3);
        w.Combat.Auras.Apply(wolf, w.Content.Aura("mage_chill"), ana, w.Map, ctx).ShouldNotBeNull(); // una ralentización sí entra
        // Al cumplirse hardControlImmunitySec justos vuelve a poder enraizarse.
        w.Clock.Advance((long)(w.Content.Rules.Combat.HardControlImmunitySec * 1000));
        w.Combat.Auras.Apply(wolf, root, ana, w.Map, w.Begin()).ShouldNotBeNull();
    }

    [Fact]
    public void MonsterAi_Boss_ShowsImmuneToStunRootAndSlow() // HU-036 CA5d
    {
        var w = new WorldBuilder().WithMap(60, 60).WithPlayer("Ana", "warrior", 5, (5, 5)).WithMonster("foreman_grask", (40, 40), wanderRadius: 0).Build();
        var ana = w.Player("Ana"); var boss = w.Monster("foreman_grask");
        var ctx = w.Begin();
        foreach (var auraId in new[] { "rogue_gouge_stun", "mage_frost_nova_root", "mage_chill" })
            w.Combat.Auras.Apply(boss, w.Content.Aura(auraId), ana, w.Map, ctx).ShouldBeNull(auraId);
        ctx.Events.OfType<CombatHitEvent>().Count(e => e.Target == boss && e.Kind == HitKinds.Immune).ShouldBe(3);
        boss.Auras.Count.ShouldBe(0);
    }

    // ------------------------------------------------------------------------------------------------- HU-039 (ira)

    private static TestWorld ChargeArena(double roll)
        => new WorldBuilder().WithMap(40, 40).WithRng(new FixedRng(roll)).WithPlayer("Ana", "warrior", 3, (10, 10))
            .WithMonster("wolf", (17, 10), wanderRadius: 0).Build();

    [Fact]
    public void Rage_ChargeThatHits_GrantsRagePerHitDealt_WithoutDealingDamage() // HU-039 CA2
    {
        var w = ChargeArena(0.5); // acierta sin crítico
        var ana = w.Player("Ana"); var wolf = w.Monster("wolf");
        ana.Resource.ShouldBe(0); // la ira empieza en 0
        var wolfHp = wolf.Hp;
        var ctx = w.Begin();
        w.Combat.Casts.TryBeginCast(ana, w.Content.Spell("warrior_charge"), wolf.Id, null, w.Map, ctx).ShouldBeNull(); // Carga no cuesta ira
        ctx.Events.OfType<CombatHitEvent>().ShouldNotContain(e => e.Source == ana && e.Kind == HitKinds.Damage);
        wolf.Hp.ShouldBe(wolfHp);
        wolf.Auras.IsStunned.ShouldBeTrue();
        ana.Resource.ShouldBe((int)w.Content.Rules.Combat.RagePerHitDealt);
    }

    [Fact]
    public void Rage_ChargeThatMisses_GrantsNoRage() // HU-039 CA2 (borde: solo "al impactar")
    {
        var w = ChargeArena(0.0); // 0: por debajo de la probabilidad de fallo
        var ana = w.Player("Ana"); var wolf = w.Monster("wolf");
        var ctx = w.Begin();
        w.Combat.Casts.TryBeginCast(ana, w.Content.Spell("warrior_charge"), wolf.Id, null, w.Map, ctx).ShouldBeNull();
        ctx.Events.OfType<CombatHitEvent>().ShouldContain(e => e.Source == ana && e.Kind == HitKinds.Miss);
        wolf.Auras.IsStunned.ShouldBeFalse();
        ana.Resource.ShouldBe(0);
    }

    // ------------------------------------------------------------------------------------------------- HU-040 (tag y botín)

    private static TestWorld TagArena()
        => new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "warrior", 3, (10, 10)).WithPlayer("Bob", "warrior", 3, (11, 10))
            .WithMonster("slime", (12, 10), wanderRadius: 0).Build();

    /// <summary>Ana le pega primero y Bob lo remata; progresión y botín leen el ActorDied del lote, como en el tick.</summary>
    private static TickContext AnaTagsBobKills(TestWorld w)
    {
        var ana = w.Player("Ana"); var bob = w.Player("Bob"); var slime = w.Monster("slime");
        var ctx = w.Begin();
        w.Combat.Damage.Deal(ana, slime, 1, School.Physical, false, null, w.Map, ctx);
        w.Combat.Damage.Deal(bob, slime, slime.Hp, School.Physical, false, null, w.Map, ctx);
        slime.IsDead.ShouldBeTrue();
        slime.TaggedBy.ShouldBe(ana.Id);
        w.Combat.Progression.Tick(w.Map, ctx);
        w.Combat.Loot.Tick(w.Map, ctx);
        return ctx;
    }

    [Fact]
    public void Loot_MonsterTaggedByAnotherPlayer_KillerGetsNoXpNorLoot() // HU-040 CA3
    {
        var w = TagArena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob"); var slime = w.Monster("slime");
        var ctx = AnaTagsBobKills(w);
        ctx.Events.OfType<XpGainedEvent>().ShouldContain(e => e.Player == ana);
        ctx.Events.OfType<XpGainedEvent>().ShouldNotContain(e => e.Player == bob);
        bob.Xp.ShouldBe(0);
        var bag = w.Combat.Loot.Get(w.Map, slime.Id).ShouldNotBeNull();
        bag.Eligible.ShouldHaveSingleItem().ShouldBe(ana.CharacterId);
        ctx.Events.OfType<LootAvailableEvent>().ShouldAllBe(e => !e.Winners.Contains(bob));
        w.Combat.Loot.Open(bob, slime.Id, w.Map, ctx).Error.ShouldBe("not_owner");
        w.Combat.Loot.TakeAll(bob, slime.Id, w.Map, ctx).ShouldBe("not_owner");
        w.Combat.Loot.Take(bob, slime.Id, 0, w.Map, ctx).ShouldBe("not_owner");
        // Tampoco cuando pasa la exclusividad: no es elegible, no "llega tarde".
        w.Clock.Advance((long)(w.Content.Rules.Loot.ExclusiveSec * 1000));
        w.Combat.Loot.Take(bob, slime.Id, 0, w.Map, w.Begin()).ShouldBe("not_owner");
        w.Combat.Loot.Open(ana, slime.Id, w.Map, w.Begin()).Error.ShouldBeNull();
        w.Combat.Loot.Open(ana, new EntityId(999_999), w.Map, w.Begin()).Error.ShouldBe("not_found"); // id inexistente
    }

    [Fact]
    public void Loot_MonsterTaggedByMyPartyMember_IsAlsoMine() // HU-040 CA3 (contraste: solo excluye a quien está fuera del grupo)
    {
        var w = TagArena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob"); var slime = w.Monster("slime");
        var group = w.Content.Rules.Group;
        w.Combat.Parties.Invite(ana, bob, 0, group).ShouldBeNull();
        w.Combat.Parties.Respond(bob, true, id => w.Map.Players.Values.FirstOrDefault(p => p.CharacterId == id), 0, group).Error.ShouldBeNull();
        var ctx = AnaTagsBobKills(w);
        w.Combat.Loot.Get(w.Map, slime.Id).ShouldNotBeNull().Eligible.ShouldContain(bob.CharacterId);
        w.Combat.Loot.Open(bob, slime.Id, w.Map, ctx).Error.ShouldBeNull();
    }

    // ------------------------------------------------------------------------------------------------- HU-052 (cambio de arma)

    [Fact]
    public void AutoAttack_WeaponSwappedMidCombat_NextSwingUsesTheNewSpeedDamageSchoolAndRange() // HU-052 CA4
    {
        // Solo el sistema de básico (sin IA): el gólem ni se mueve ni responde. Tiradas a 0,5: impacta sin crítico.
        var w = new WorldBuilder().WithMap(40, 40).WithRng(new FixedRng(0.5)).WithPlayer("Ana", "warrior", 5, (10.5f, 10.5f))
            .WithMonster("rubble_golem", (11.5f, 10.5f), wanderRadius: 0).Build();
        w.Simulation.AddSystem(w.Combat.AutoAttack);
        var ana = w.Player("Ana"); var golem = w.Monster("rubble_golem");
        ana.Equipment.MainHand.ShouldNotBeNull().TemplateId.ShouldBe("worn_sword");
        ana.Inventory.Bag[0] = ItemInstance.New("worn_dagger");
        ana.Inventory.Bag[1] = ItemInstance.New("apprentice_staff");
        ana.Combat.TargetId = golem.Id;
        ana.Combat.AutoAttackOn = true;

        var (swordTicks, swordHit) = NextBasicHit(w, ana);
        swordTicks.ShouldBe(SwingTicks(w, ana, "worn_sword"));
        swordHit.School.ShouldBe(School.Physical);
        swordHit.Amount.ShouldBe(ExpectedBasic(w, ana, golem, "worn_sword"));

        Equip(w, ana, 0); // justo tras un golpe: el siguiente ya es de daga
        var (daggerTicks, daggerHit) = NextBasicHit(w, ana);
        daggerTicks.ShouldBe(SwingTicks(w, ana, "worn_dagger"));
        daggerTicks.ShouldBeLessThan(swordTicks);
        daggerHit.Amount.ShouldBe(ExpectedBasic(w, ana, golem, "worn_dagger"));
        daggerHit.Amount.ShouldNotBe(swordHit.Amount);

        // Bastón (`scaling: int`): escuela magic y alcance 5; a 4 casillas la daga ya no llegaba.
        golem.Position = new Vec2(14.5f, 10.5f);
        Equip(w, ana, 1);
        var (staffTicks, staffHit) = NextBasicHit(w, ana);
        staffTicks.ShouldBe(SwingTicks(w, ana, "apprentice_staff"));
        staffHit.School.ShouldBe(School.Magic);
        staffHit.Amount.ShouldBe(ExpectedBasic(w, ana, golem, "apprentice_staff"));
    }

    private static void Equip(TestWorld w, Player p, int bagIndex)
    {
        InventoryOps.Equip(p, bagIndex, w.Content).Ok.ShouldBeTrue();
        w.Combat.Services.Recalculate(p); // como el handler de InventoryMove
    }

    /// <summary>Ticks hasta el siguiente golpe básico de `p` y ese golpe.</summary>
    private static (int Ticks, CombatHitEvent Hit) NextBasicHit(TestWorld w, Player p, int maxTicks = 400)
    {
        for (var i = 1; i <= maxTicks; i++)
        {
            var hit = TickRunner.Run(w, 1).OfType<CombatHitEvent>().FirstOrDefault(e => e.Source == p && e.SpellId is null && e.Kind == HitKinds.Damage);
            if (hit is not null) return (i, hit);
        }
        throw new ShouldAssertException($"{p.Name} no golpeó en {maxTicks} ticks");
    }

    private static int SwingTicks(TestWorld w, Player p, string weaponId)
        => (int)Math.Ceiling(w.Content.Item(weaponId).SpeedMs / w.Combat.Services.StatsOf(p).Haste / GameConstants.TickMs);

    /// <summary>Daño del básico con el arma dada y todas las tiradas a 0,5 (combat.md §Ataque básico).</summary>
    private static int ExpectedBasic(TestWorld w, Player p, Monster target, string weaponId)
    {
        var c = w.Content.Rules.Combat;
        var weapon = w.Content.Item(weaponId);
        var stats = w.Combat.Services.StatsOf(p);
        var roll = CombatCalculator.RollWeapon(new FixedRng(0.5), weapon.DamageMin, weapon.DamageMax);
        var variance = CombatCalculator.RollVariance(new FixedRng(0.5), c);
        var affinity = w.Content.Rules.Affinity.MultiplierFor(p.ClassId, weapon.AffinityType);
        var magic = weapon.Scaling == Stat.Int;
        var raw = CombatCalculator.BasicAttackRaw(roll, affinity, magic ? stats.SpellPower : stats.AttackPower, weapon.SpeedMs / stats.Haste, c);
        return magic
            ? CombatCalculator.MagicDamage(raw, false, variance, 1.0, c)
            : CombatCalculator.PhysicalDamage(raw, CombatCalculator.Mitigation(w.Combat.Services.StatsOf(target).Armor, p.Level, c), false, variance, 1.0, c);
    }

    // ------------------------------------------------------------------------------------------------- HU-059 (intercambio)

    private static TestWorld TradeArena()
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "warrior", 5, (10, 10)).WithPlayer("Bob", "mage", 5, (11, 10)).BuildWithCombat();
        foreach (var p in w.Map.Players.Values) p.ConnectionId = p.Id.Value; // conectados: si no, se cancela por desconexión
        return w;
    }

    /// <summary>Intercambio abierto con la espada de Ana en la oferta (bloqueada).</summary>
    private static (Player Ana, Player Bob, ItemInstance Sword) OpenTrade(TestWorld w)
    {
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        var sword = ItemInstance.New("iron_sword");
        ana.Inventory.Bag[0] = sword;
        var ctx = w.Begin();
        w.Combat.Trades.Request(ana, bob, w.Map, ctx).ShouldBeNull();
        w.Combat.Trades.Respond(bob, true, w.Map, ctx).ShouldBeNull();
        w.Combat.Trades.Offer(ana, [(sword.Id, 1)], 0, w.Map, ctx).ShouldBeNull();
        w.Combat.Trades.IsLocked(ana, sword.Id).ShouldBeTrue();
        return (ana, bob, sword);
    }

    [Fact]
    public void Trade_PartnerDies_CancelsWithDied_AndUnlocksTheOffer() // HU-059 CA4
    {
        var w = TradeArena();
        var (ana, bob, sword) = OpenTrade(w);
        w.Combat.Death.Kill(bob, null, w.Map, w.Begin());
        var cancelled = TickRunner.Run(w, 1).OfType<TradeChangedEvent>().Single();
        cancelled.State.ShouldBe("cancelled");
        cancelled.Reason.ShouldBe("died");
        w.Combat.Trades.TradeOf(ana).ShouldBeNull();
        w.Combat.Trades.TradeOf(bob).ShouldBeNull();
        w.Combat.Trades.IsLocked(ana, sword.Id).ShouldBeFalse(); // vuelve a estar disponible
        ana.Inventory.Bag[0].ShouldBeSameAs(sword);
        bob.Inventory.Bag.ShouldNotContain(i => i != null && i.TemplateId == "iron_sword");
    }

    [Fact]
    public void Trade_TradeCancel_CancelsForBoth_AndAConfirmInTheSameTickIsNotFound() // HU-059 CA4 (+ dos mensajes en el mismo tick)
    {
        var w = TradeArena();
        var (ana, bob, sword) = OpenTrade(w);
        var trade = w.Combat.Trades.TradeOf(ana).ShouldNotBeNull();
        var ctx = w.Begin();
        w.Combat.Trades.CancelBy(bob, "cancelled", w.Map, ctx).ShouldBeNull();                 // TradeCancel de Bob…
        w.Combat.Trades.Confirm(ana, trade.Version, w.Map, ctx).ShouldBe("not_found");          // …y el Confirm de Ana en el mismo tick
        var ev = ctx.Events.OfType<TradeChangedEvent>().Single();
        ev.State.ShouldBe("cancelled");
        ev.Trade.ShouldBeSameAs(trade);
        trade.State.ShouldBe(TradeState.Cancelled);
        w.Combat.Trades.TradeOf(ana).ShouldBeNull();
        w.Combat.Trades.IsLocked(ana, sword.Id).ShouldBeFalse();
        ana.Inventory.Bag[0].ShouldBeSameAs(sword);
        w.Combat.Trades.CancelBy(bob, "cancelled", w.Map, w.Begin()).ShouldBe("not_found"); // cancelar dos veces
    }

    [Fact]
    public void Trade_PartnerDisconnectsMidTrade_Cancels() // HU-059 CA4 (desconexión a mitad de operación)
    {
        var w = TradeArena();
        var (ana, bob, sword) = OpenTrade(w);
        bob.ConnectionId = -1; // linkdead
        var ev = TickRunner.Run(w, 1).OfType<TradeChangedEvent>().Single();
        ev.State.ShouldBe("cancelled");
        ev.Reason.ShouldBe("disconnected");
        w.Combat.Trades.IsLocked(ana, sword.Id).ShouldBeFalse();
    }

    [Fact]
    public void Trade_UnansweredRequest_ExpiresAfterTradeRequestExpireSec() // HU-059 CA1, CA4
    {
        var w = TradeArena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        var expireMs = (int)(w.Content.Rules.Social.TradeRequestExpireSec * 1000);
        w.Combat.Trades.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        TickRunner.RunMs(w, expireMs).OfType<TradeChangedEvent>().ShouldBeEmpty(); // a los 30 s justos sigue pendiente
        w.Combat.Trades.TradeOf(bob).ShouldNotBeNull().State.ShouldBe(TradeState.Requested);
        var ev = TickRunner.Run(w, 1).OfType<TradeChangedEvent>().Single();
        ev.State.ShouldBe("cancelled");
        ev.Reason.ShouldBe("expired");
        w.Combat.Trades.Respond(bob, true, w.Map, w.Begin()).ShouldBe("not_found"); // aceptar tarde no abre nada
    }

    // ------------------------------------------------------------------------------------------------- HU-064 (duelos)

    private static TestWorld DuelArena()
        => new WorldBuilder().WithMap(60, 60).WithPlayer("Ana", "warrior", 5, (10, 10)).WithPlayer("Bob", "mage", 5, (11, 10)).BuildWithCombat();

    private static void Challenge(TestWorld w)
    {
        w.Combat.Pvp.Request(w.Player("Ana"), w.Player("Bob"), w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Pvp.Respond(w.Player("Bob"), true, w.Map, w.Begin()).ShouldBeNull();
    }

    private static void StartDuel(TestWorld w)
    {
        Challenge(w);
        TickRunner.RunMs(w, (int)(w.Content.Rules.Pvp.Rulesets["duel"].CountdownSec * 1000) + GameConstants.TickMs);
        w.Combat.Pvp.InActiveDuel(w.Player("Ana")).ShouldBeTrue();
    }

    /// <summary>Bob cruza a otra instancia del mapa (en el juego, un portal a la Mina).</summary>
    private static void MoveBobToAnotherInstance(TestWorld w)
    {
        var bob = w.Player("Bob");
        var other = w.World.CreateInstance(w.Map.MapId);
        w.Map.Remove(bob.Id);
        other.Add(bob);
    }

    [Fact]
    public void Duel_DuelistChangesInstance_EndsWithLeftMap_AndTheLeaverLoses() // HU-064 CA4
    {
        var w = DuelArena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        StartDuel(w);
        MoveBobToAnotherInstance(w);
        var ended = TickRunner.Run(w, 1).OfType<DuelChangedEvent>().Single(e => e.State == "ended");
        ended.Reason.ShouldBe("left_map");
        ended.Duel.Winner.ShouldBe(ana);
        w.Combat.Pvp.DuelOf(ana).ShouldBeNull();
        w.Combat.Pvp.CanAttack(ana, bob, w.Content.Rules).ShouldBeNull();
    }

    [Fact]
    public void Duel_DuelistDisconnects_AbandonEndsIt_AndTheLeaverLoses() // HU-064 CA4
    {
        var w = DuelArena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        StartDuel(w);
        var ctx = w.Begin();
        w.Combat.Pvp.Abandon(bob, "disconnected", w.Map, ctx);
        var ended = ctx.Events.OfType<DuelChangedEvent>().Single();
        ended.State.ShouldBe("ended");
        ended.Reason.ShouldBe("disconnected");
        ended.Duel.Winner.ShouldBe(ana);
        w.Combat.Pvp.DuelOf(bob).ShouldBeNull();
        w.Combat.Services.IsEnemy(ana, bob).ShouldBeFalse();
    }

    [Fact]
    public void Duel_LeavingDuringTheCountdown_WithdrawsIt_AndNobodyWins() // HU-064 CA4 (borde: antes de empezar no hay perdedor)
    {
        var w = DuelArena();
        Challenge(w);
        var ctx = w.Begin();
        w.Combat.Pvp.Abandon(w.Player("Bob"), "disconnected", w.Map, ctx);
        var withdrawn = ctx.Events.OfType<DuelChangedEvent>().Single();
        withdrawn.State.ShouldBe("declined");
        withdrawn.Duel.Winner.ShouldBeNull();

        var w2 = DuelArena();
        Challenge(w2);
        MoveBobToAnotherInstance(w2);
        var left = TickRunner.Run(w2, 1).OfType<DuelChangedEvent>().Single();
        left.State.ShouldBe("declined");
        left.Reason.ShouldBe("left_map");
        left.Duel.Winner.ShouldBeNull();
    }

    [Fact]
    public void Duel_AbandonWithoutADuel_DoesNothing() // HU-064 CA4 (borde: se desconecta quien no está en duelo)
    {
        var w = DuelArena();
        var ctx = w.Begin();
        w.Combat.Pvp.Abandon(w.Player("Bob"), "disconnected", w.Map, ctx);
        ctx.Events.OfType<DuelChangedEvent>().ShouldBeEmpty();
    }
}
