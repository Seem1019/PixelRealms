using System.Text.Json.Nodes;
using PixelRealms.Content;
using PixelRealms.Content.Defs;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Map;
using PixelRealms.Game.Progression;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Progression;

/// <summary>
/// HU-104 (ADR-027 D1): mejoras 1-de-2 por hechizo. Estos tests cargan una copia del contenido con la Fase 2 abierta (tope 10) en
/// la que Bola de fuego, Descarga de escarcha y Tajo amplio llevan mejoras de prueba en lugar de las suyas (HU-107) y Nova de
/// escarcha se queda sin mejoras; los demás hechizos llevan las del contenido.
/// </summary>
public sealed class SpellUpgradeTests
{
    /// <summary>Mejoras de prueba: cubren los cuatro tipos de modificador.</summary>
    private static void AddTestUpgrades(JsonNode spells)
    {
        foreach (var s in spells["spells"]!.AsArray())
        {
            var id = s!["id"]!.GetValue<string>();
            JsonNode? ups = id switch
            {
                "mage_fireball" => JsonNode.Parse("""
                    [{ "id": "fireball_quick", "name": "Llama rápida", "description": "Casteo 0,5 s más corto y la mitad de maná.",
                       "mods": [{ "stat": "castMs", "add": -500 }, { "stat": "cost", "mult": 0.5 }] },
                     { "id": "fireball_hot", "name": "Llama intensa", "description": "+20 % de daño.",
                       "mods": [{ "effect": "damage", "mult": 1.2 }] }]
                    """),
                "mage_frostbolt" => JsonNode.Parse("""
                    [{ "id": "frostbolt_long", "name": "Frío duradero", "description": "La ralentización dura 2 s más.",
                       "mods": [{ "aura": "mage_chill", "stat": "durationMs", "add": 2000 }] },
                     { "id": "frostbolt_root", "name": "Escarcha que atrapa", "description": "Además enraíza.",
                       "mods": [{ "addEffect": { "type": "apply_aura", "auraId": "mage_frost_nova_root" } }] }]
                    """),
                "warrior_cleave" => JsonNode.Parse("""
                    [{ "id": "cleave_wide", "name": "Tajo abierto", "description": "+30° de apertura y el doble de recarga.",
                       "mods": [{ "stat": "aoeAngleDeg", "add": 30 }, { "stat": "cooldownMs", "mult": 2 }] },
                     { "id": "cleave_long", "name": "Tajo largo", "description": "+0,5 casillas de radio.",
                       "mods": [{ "stat": "aoeRadius", "add": 0.5 }] }]
                    """),
                _ => null,
            };
            if (ups is not null) s["upgrades"] = ups;
            if (id == "mage_frost_nova") s.AsObject().Remove("upgrades"); // un hechizo sin mejoras para los casos que lo necesitan
        }
    }

    private static readonly Lazy<ContentDb> Db = new(() =>
    {
        using var tmp = new TempContent();
        tmp.Patch("spells.json", AddTestUpgrades);
        tmp.PatchPointer("rules.json", "/world/currentPhase", "2");
        return ContentLoader.LoadOrThrow(tmp.Path);
    });

    private static TestWorld Arena(string classId, int level, float monsterDistance = 4f, string monster = "boar") =>
        new WorldBuilder(Db.Value).WithMap(40, 40).WithPlayer("Ana", classId, level, (10, 10))
            .WithMonster(monster, (10 + monsterDistance, 10), wanderRadius: 0).BuildWithCombat();

    private static string? Choose(TestWorld w, string spellId, string? upgradeId, bool inCombat = false) =>
        SpellUpgradeRules.Choose(w.Player("Ana"), spellId, upgradeId, w.Content, w.Content.Rules.Progression, inCombat);

    // --- Contenido -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Upgraded_AppliesEachKindOfModifier_AndLeavesTheBaseSpellAlone() // CA1
    {
        var db = Db.Value;
        var fireball = db.Spell("mage_fireball");
        var quick = db.Upgraded("mage_fireball", "fireball_quick")!;
        quick.Id.ShouldBe("mage_fireball");
        quick.CastMs.ShouldBe(fireball.CastMs - 500);
        quick.Cost!.Amount.ShouldBe(fireball.Cost!.Amount / 2);
        quick.Upgrades.ShouldBeEmpty();
        var hot = db.Upgraded("mage_fireball", "fireball_hot")!.Effects[0];
        hot.Base.ShouldBe(fireball.Effects[0].Base * 1.2, 1e-9);
        hot.SpCoef.ShouldBe(fireball.Effects[0].SpCoef * 1.2, 1e-9);
        fireball.CastMs.ShouldBe(2000); // el del contenido no cambia

        var chill = db.Aura("mage_chill");
        var longBolt = db.Upgraded("mage_frostbolt", "frostbolt_long")!;
        longBolt.Effects.Single(e => e.AuraId == "mage_chill").AuraOverride!.DurationMs.ShouldBe(chill.DurationMs + 2000);
        longBolt.Effects.Single(e => e.AuraId == "mage_chill").AuraOverride!.Id.ShouldBe("mage_chill"); // el cliente la reconoce
        db.Upgraded("mage_frostbolt", "frostbolt_root")!.Effects.Count.ShouldBe(db.Spell("mage_frostbolt").Effects.Count + 1);
        db.Upgraded("warrior_cleave", "cleave_wide")!.AoeAngleDeg.ShouldBe(db.Spell("warrior_cleave").AoeAngleDeg + 30);
        db.Upgraded("mage_fireball", "cleave_wide").ShouldBeNull(); // la mejora de otro hechizo
    }

    // --- Elegir ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void Choose_FromLevelEight_OutOfCombat_AndChangingOrRemovingIsFree() // CA3
    {
        var w = Arena("mage", 8);
        var ana = w.Player("Ana");
        Choose(w, "mage_fireball", "fireball_quick").ShouldBeNull();
        ana.SpellUpgrades["mage_fireball"].ShouldBe("fireball_quick");
        Choose(w, "mage_fireball", "fireball_hot").ShouldBeNull();
        ana.SpellUpgrades["mage_fireball"].ShouldBe("fireball_hot");
        Choose(w, "mage_fireball", null).ShouldBeNull();
        ana.SpellUpgrades.ShouldNotContainKey("mage_fireball");
        Choose(w, "mage_fireball", "fireball_quick", inCombat: true).ShouldBe("in_combat");
        ana.SpellUpgrades.ShouldNotContainKey("mage_fireball");
    }

    [Fact]
    public void Choose_RejectsCheats() // CA7
    {
        var w = Arena("mage", 8);
        Choose(w, "warrior_cleave", "cleave_wide").ShouldBe("invalid_payload");   // hechizo de otra clase
        Choose(w, "mage_fireball", "cleave_wide").ShouldBe("invalid_payload");    // mejora de otro hechizo
        Choose(w, "mage_fireball", "fireball_turbo").ShouldBe("invalid_payload"); // id inventado
        Choose(w, "mage_frost_nova", null).ShouldBe("invalid_payload");           // hechizo sin mejoras
        Choose(w, "mage_meteor", null).ShouldBe("invalid_payload");               // aún no aprendido (nivel 13)
        w.Player("Ana").SpellUpgrades.ShouldBeEmpty();

        var low = Arena("mage", 7);
        Choose(low, "mage_fireball", "fireball_quick").ShouldBe("level_too_low");
    }

    // --- El casteo usa el hechizo mejorado ---------------------------------------------------------------------------------

    [Fact]
    public void Cast_UsesTheUpgradedCastTimeAndCost() // CA4
    {
        var w = Arena("mage", 8);
        var ana = w.Player("Ana");
        Choose(w, "mage_fireball", "fireball_quick").ShouldBeNull();
        var mana = ana.Resource;
        var ctx = w.Begin();
        w.Combat.Casts.TryBeginCast(ana, w.Content.Spell("mage_fireball"), w.Monster("boar").Id, null, w.Map, ctx).ShouldBeNull();
        ctx.Events.OfType<CastStartedEvent>().Single().DurationMs.ShouldBe(1500);
        TickRunner.RunMs(w, 1600);
        (mana - ana.Resource).ShouldBeLessThanOrEqualTo(w.Content.Spell("mage_fireball").Cost!.Amount / 2 + 1); // más la regeneración
    }

    [Fact]
    public void Cast_TheDamageUpgradeHitsHarder_WithTheSameRolls() // CA4
    {
        int Hit(string? upgrade)
        {
            var w = new WorldBuilder(Db.Value).WithRng(new FixedRng(0.5)).WithMap(40, 40).WithPlayer("Ana", "mage", 8, (10, 10))
                .WithMonster("boar", (14, 10), wanderRadius: 0).BuildWithCombat();
            if (upgrade is not null) Choose(w, "mage_fireball", upgrade).ShouldBeNull();
            w.Combat.Casts.TryBeginCast(w.Player("Ana"), w.Content.Spell("mage_fireball"), w.Monster("boar").Id, null, w.Map, w.Begin()).ShouldBeNull();
            return TickRunner.RunMs(w, 3000).OfType<CombatHitEvent>().First(e => e.SpellId == "mage_fireball").Amount;
        }
        var plain = Hit(null);
        var hot = Hit("fireball_hot");
        ((double)hot / plain).ShouldBe(1.2, 0.05);
    }

    [Fact]
    public void Cast_TheAuraUpgradeLastsLonger_AndKeepsTheAuraId() // CA4
    {
        var w = Arena("mage", 8);
        Choose(w, "mage_frostbolt", "frostbolt_long").ShouldBeNull();
        w.Combat.Casts.TryBeginCast(w.Player("Ana"), w.Content.Spell("mage_frostbolt"), w.Monster("boar").Id, null, w.Map, w.Begin()).ShouldBeNull();
        var applied = TickRunner.RunMs(w, 3000).OfType<AuraAppliedEvent>().First(e => e.Aura.AuraId == "mage_chill");
        (applied.Aura.ExpiresAtMs - w.Clock.NowMs).ShouldBeGreaterThan(w.Content.Aura("mage_chill").DurationMs);
    }

    [Fact]
    public void Cast_BelowLevelEight_IgnoresAStoredUpgrade()
    {
        var w = Arena("mage", 8);
        var ana = w.Player("Ana");
        Choose(w, "mage_fireball", "fireball_quick").ShouldBeNull();
        ana.Level = 7; // sin pasar por /level: la mejora guardada no cuenta por debajo del nivel
        var ctx = w.Begin();
        w.Combat.Casts.TryBeginCast(ana, w.Content.Spell("mage_fireball"), w.Monster("boar").Id, null, w.Map, ctx).ShouldBeNull();
        ctx.Events.OfType<CastStartedEvent>().Single().DurationMs.ShouldBe(2000);
    }

    [Fact]
    public void Cast_TellsWhichUpgradeItUses() // revisión de autoridad: los demás dibujan la forma mejorada
    {
        var w = Arena("mage", 8);
        Choose(w, "mage_fireball", "fireball_quick").ShouldBeNull();
        var ctx = w.Begin();
        w.Combat.Casts.TryBeginCast(w.Player("Ana"), w.Content.Spell("mage_fireball"), w.Monster("boar").Id, null, w.Map, ctx).ShouldBeNull();
        ctx.Events.OfType<CastStartedEvent>().Single().Spell.AppliedUpgradeId.ShouldBe("fireball_quick");
        w.Content.Spell("mage_fireball").AppliedUpgradeId.ShouldBeNull();
    }

    [Fact]
    public void LongestCooldown_CountsEveryUpgrade() // revisión de autoridad: salir y entrar no acorta una recarga alargada
    {
        var cleave = Db.Value.Spell("warrior_cleave");
        Db.Value.LongestCooldownMs(cleave).ShouldBe(cleave.CooldownMs * 2);
        Db.Value.LongestCooldownMs(Db.Value.Spell("mage_frost_nova")).ShouldBe(Db.Value.Spell("mage_frost_nova").CooldownMs);
    }

    [Fact]
    public void RenewingAnAuraFromAnotherUpgrade_TakesTheNewDefinition() // revisión de autoridad: no mezclar dos mejoras
    {
        var w = Arena("mage", 8);
        var ana = w.Player("Ana");
        var boar = w.Monster("boar");
        var chill = w.Content.Aura("mage_chill");
        w.Combat.Auras.Apply(boar, chill with { Pct = 0.3 }, ana, w.Map, w.Begin(), "mage_frostbolt");
        w.Combat.Auras.Apply(boar, chill with { Pct = 0.1, DurationMs = chill.DurationMs + 4000 }, ana, w.Map, w.Begin(), "mage_frostbolt");
        var aura = boar.Auras.All.Single(a => a.AuraId == "mage_chill");
        aura.Def.Pct.ShouldBe(0.1);
        aura.Def.DurationMs.ShouldBe(chill.DurationMs + 4000);
    }

    [Fact]
    public void Effective_AllocatesNothing() // HU-088: se llama en cada intento de casteo
    {
        var w = Arena("mage", 8);
        var ana = w.Player("Ana");
        Choose(w, "mage_fireball", "fireball_hot").ShouldBeNull();
        var fireball = w.Content.Spell("mage_fireball");
        var rules = w.Content.Rules.Progression;
        SpellUpgradeRules.Effective(ana, fireball, w.Content, rules); // calentamiento
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) SpellUpgradeRules.Effective(ana, fireball, w.Content, rules);
        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0);
    }

    // --- Subir y bajar de nivel, cambiar de clase ------------------------------------------------------------------------------

    [Fact]
    public void LevelUp_ToEight_UnlocksEveryKnownSpellWithUpgrades_ThenOnlyTheNewOnes() // CA2
    {
        var w = Arena("warrior", 7);
        var ana = w.Player("Ana");
        var ctx = w.Begin();
        w.Combat.Progression.SetLevel(ana, 8, w.Map, ctx);
        ctx.Events.OfType<LevelUpEvent>().Single().UpgradesUnlocked!.ShouldBe( // todos los que conoce tienen mejoras (HU-107)
            ["warrior_heroic_strike", "warrior_taunt", "warrior_charge", "warrior_whirlwind", "warrior_shield_block"], ignoreOrder: true);
        ctx = w.Begin();
        w.Combat.Progression.SetLevel(ana, 9, w.Map, ctx);
        ctx.Events.OfType<LevelUpEvent>().Single().UpgradesUnlocked.ShouldBe(["warrior_cleave"]); // Tajo amplio se aprende en el 9

        var mage = Arena("mage", 7);
        ctx = mage.Begin();
        mage.Combat.Progression.SetLevel(mage.Player("Ana"), 8, mage.Map, ctx);
        ctx.Events.OfType<LevelUpEvent>().Single().UpgradesUnlocked!.ShouldBe( // Nova de escarcha no: en esta copia no tiene mejoras
            ["mage_fireball", "mage_frostbolt", "mage_flame_burst", "mage_burning_field"], ignoreOrder: true);
    }

    [Fact]
    public void LevelDownBelowEight_ClearsTheUpgrades() // CA6
    {
        var w = Arena("mage", 8);
        var ana = w.Player("Ana");
        Choose(w, "mage_fireball", "fireball_quick").ShouldBeNull();
        w.Combat.Progression.SetLevel(ana, 7, w.Map, w.Begin());
        ana.SpellUpgrades.ShouldBeEmpty();
    }

    [Fact]
    public void ClassChange_ClearsTheOldClassUpgrades() // CA6
    {
        var data = new MapData("t", "T", new CollisionGrid(40, 40), [], [new NpcDef("maestro", "Maestro", null, "class_change", new Vec2(12, 10))],
            [new GraveyardDef("gy", new Vec2(2, 2))], [], [], "gy");
        var w = new WorldBuilder(Db.Value).WithMap(data).WithPlayer("Ana", "mage", 9, (10, 10)).BuildWithCombat();
        var ana = w.Player("Ana");
        var npc = new Npc(w.World.EntityIds.Next(), data.Npcs[0]) { Position = data.Npcs[0].Position };
        w.Map.Add(npc);
        Choose(w, "mage_fireball", "fireball_quick").ShouldBeNull();
        w.Combat.ClassChange.Change(ana, npc.Id, "warrior", w.Map, w.Begin()).ShouldBeNull();
        ana.SpellUpgrades.ShouldBeEmpty();
    }

    [Fact]
    public void Prune_DropsWhatTheContentNoLongerHas()
    {
        var w = Arena("mage", 8);
        var ana = w.Player("Ana");
        ana.SpellUpgrades["mage_fireball"] = "fireball_removed";
        ana.SpellUpgrades["mage_frostbolt"] = "frostbolt_long";
        SpellUpgradeRules.Prune(ana, w.Content, w.Content.Rules.Progression);
        ana.SpellUpgrades.Keys.ShouldBe(["mage_frostbolt"]);
    }
}
