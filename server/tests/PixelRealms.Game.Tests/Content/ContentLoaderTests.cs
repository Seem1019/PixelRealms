using System.Text.Json;
using PixelRealms.Content;
using PixelRealms.Content.Defs;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Content;

public sealed class ContentLoaderTests
{
    [Fact]
    public void RepoContent_Loads_WithoutErrors() // HU-003 CA1
    {
        var result = ContentLoader.Load(TestContent.ContentDir);
        result.Report.Errors.ShouldBeEmpty();
        var db = result.ContentOrThrow;
        db.Classes.Count.ShouldBe(4);
        db.Spells.Count.ShouldBe(57); // + los 16 hechizos de los monstruos del Tier 2 (HU-109)
        db.Auras.Count.ShouldBe(37);
        db.Items.Count.ShouldBe(37);
        db.Monsters.Count.ShouldBe(24); // + los élites de las ramas de la pradera (HU-080) y los 12 del Tier 2 (HU-109)
        db.LootTables.Count.ShouldBe(24);
        db.Vendors.Count.ShouldBe(1);
        db.Rules.Hash.Length.ShouldBe(16);
    }

    [Fact]
    public void ContentDb_Lookups_ThrowClearMessage() // HU-003 CA6
    {
        var db = TestContent.Load();
        db.Spell("mage_fireball").Name.ShouldBe("Bola de fuego");
        db.Item("iron_sword").Stats!.Str.ShouldBe(2);
        db.Monster("foreman_grask").Boss.ShouldBeTrue();
        db.Aura("priest_renew_hot").TickMs.ShouldBe(3000);
        db.Class("mage").Resource.ShouldBe(Resource.Mana);
        db.LootTable("lt_foreman").Groups.Count.ShouldBe(1);
        db.Vendor("robledal_general_goods").Items.ShouldContain("initiate_mace");
        var ex = Should.Throw<KeyNotFoundException>(() => db.Spell("nope"));
        ex.Message.ShouldContain("'nope'");
        ex.Message.ShouldContain("spells.json");
    }

    [Fact]
    public void Enums_Deserialize_FromSnakeCase()
    {
        var db = TestContent.Load();
        db.Spell("priest_holy_pulse").Targeting.ShouldBe(Targeting.GroundAoeAll);
        db.Spell("priest_path_of_light").Shape.ShouldBe(Shape.Line);
        db.Class("rogue").Role.ShouldBe(ClassRole.MeleeDps);
        db.Item("wooden_shield").Slot.ShouldBe(EquipSlot.OffHand);
        db.Monster("foreman_grask").Spells[1].Target.ShouldBe(MonsterSpellTarget.RandomNotTopThreat);
        db.Rules.Loot.OwnerMode.ShouldBe(LootOwnerMode.RandomPerItem);
        db.Spell("rogue_shadowstep").Effects[1].ApplyTo.ShouldBe(ApplyTo.Self);
    }

    [Fact]
    public void Defaults_Apply_WhenFieldMissing()
    {
        var db = TestContent.Load();
        db.Spell("mage_fireball").TriggersGcd.ShouldBeTrue();
        db.Spell("warrior_taunt").TriggersGcd.ShouldBeFalse();
        db.Spell("mage_fireball").MaxTargets.ShouldBe(10);
        db.Spell("mage_flame_burst").Shape.ShouldBe(Shape.Circle);
        db.Item("bread").MaxStack.ShouldBe(20);
        db.Item("worn_sword").MaxStack.ShouldBe(1);
        db.Monster("slime").Boss.ShouldBeFalse();
        db.LootTable("lt_slime").MaxItems.ShouldBe(4);
        db.Rules.CurrentLevelCap.ShouldBe(6);
    }

    [Fact]
    public void ConeAndLineSpells_AreAvailable() // ADR-023, HU-003 CA4d; HU-102 CA6
    {
        // Hasta HU-102 estos cuatro se cargaban como no disponibles (cono y línea sin implementar); ya no queda ninguno.
        var result = ContentLoader.Load(TestContent.ContentDir);
        var db = result.ContentOrThrow;
        db.UnavailableSpells.ShouldBeEmpty();
        foreach (var id in new[] { "warrior_cleave", "rogue_throwing_blades", "mage_cone_of_cold", "priest_path_of_light" })
            db.IsSpellAvailable(id).ShouldBeTrue(id);
        result.Report.Warnings.ShouldNotContain(w => w.Contains("ADR-023", StringComparison.Ordinal));
        db.KnownSpells("warrior", 15).Select(s => s.Id).ShouldContain("warrior_cleave");
        db.KnownSpells("mage", 4).Select(s => s.Id).ToArray().ShouldBe(new[] { "mage_fireball", "mage_frostbolt", "mage_frost_nova" });
    }

    [Fact]
    public void InstantDamageCones_WarnOnlyBeyondMeleeRange() // ADR-027 D4
    {
        var warnings = ContentLoader.Load(TestContent.ContentDir).Report.Warnings;
        warnings.ShouldNotContain(w => w.Contains("warrior_cleave", StringComparison.Ordinal));        // radio 2,5
        warnings.ShouldContain(w => w.Contains("rogue_throwing_blades", StringComparison.Ordinal)
            && w.Contains("ADR-015", StringComparison.Ordinal));                                       // radio 4, sin casteo (HU-106)
    }

    [Fact]
    public void InstantDamageArea_ThatTheActivePhaseReaches_IsAnError() // revisión de autoridad de HU-102
    {
        using var dir = new TempContent();
        dir.PatchPointer("rules.json", "/world/currentPhase", "2"); // tope 10: las Cuchillas (nivel 9) ya se alcanzan
        var result = ContentLoader.Load(dir.Path);
        result.Content.ShouldBeNull();
        result.Report.Errors.ShouldContain(e => e.Contains("rogue_throwing_blades", StringComparison.Ordinal) && e.Contains("ya se alcanza", StringComparison.Ordinal));
        result.Report.Errors.ShouldNotContain(e => e.Contains("mage_cone_of_cold", StringComparison.Ordinal)); // nivel 11: aún aviso
    }

    [Fact]
    public void Rules_ReadAllSections()
    {
        var r = TestContent.Load().Rules;
        r.Combat.GcdMs.ShouldBe(1000);
        r.Progression.SpellRankLevels.ShouldBe(new[] { 4, 8, 12 });
        r.Progression.SpellRankBonusPct.ShouldBe(0.15);
        r.ClassScaling["warrior"].HpPerSta.ShouldBe(12);
        r.Affinity.Of("mage", "plate").ShouldBe(Affinity.Baja);
        r.Affinity.Of("priest", "mace").ShouldBe(Affinity.Alta);
        r.Affinity.Of("rogue", null).ShouldBe(Affinity.Alta);
        r.Affinity.MultiplierFor("mage", "sword").ShouldBe(0.7);
        ((IRules)r).ClassAdvantage["mage"]["warrior"].ShouldBe(1.0);
        r.Pvp.Rulesets["duel"].EndAtHpPct.ShouldBe(0.01);
        r.Weapons.Types["wand"].RangeTiles.ShouldBe(7);
        r.Limits.MaxBuffsPerEntity.ShouldBe(16);
        r.Combat.BossImmuneToAuraKinds.ShouldBe(new[] { AuraKind.Stun, AuraKind.Root, AuraKind.Slow });
        r.World.StartMapId.ShouldBe("meadow");
    }

    [Fact]
    public void UnknownField_FailsWithPathAndFile() // HU-003 CA2
    {
        using var dir = new TempContent();
        dir.Patch("items.json", j => j["items"]![3]!["dmgMin"] = 1);
        var result = ContentLoader.Load(dir.Path);
        result.Content.ShouldBeNull();
        result.Report.Errors.ShouldContain(e => e.StartsWith("items.json/items/3/dmgMin", StringComparison.Ordinal) && e.Contains("no permitida", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingItemReference_FailsWithExactMessage() // HU-003 CA3
    {
        using var dir = new TempContent();
        dir.Patch("loot_tables.json", j => j["lootTables"]![3]!["entries"]![1]!["itemId"] = "wolf_fangg");
        var result = ContentLoader.Load(dir.Path);
        result.Report.Errors.ShouldContain(e => e.Contains("lt_wolf: itemId 'wolf_fangg' no existe en items.json", StringComparison.Ordinal));
    }

    [Fact]
    public void ClassSpell_WrongResource_Fails() // HU-003 CA4
    {
        using var dir = new TempContent();
        dir.Patch("spells.json", j => j["spells"]![0]!["cost"]!["resource"] = "mana");
        ContentLoader.Load(dir.Path).Report.Errors.ShouldContain(e => e.Contains("cost.resource 'mana' no coincide", StringComparison.Ordinal));
    }

    [Theory] // HU-003 CA4b/4d: una regla cruzada por caso
    [InlineData("rules.json", "/group/bonusBySize", "[1.0, 1.3]", "maxMembers")]
    [InlineData("rules.json", "/progression/levelCapByPhase", "[6, 10, 14]", "maxLevel")]
    [InlineData("rules.json", "/affinity/weaponScaling/sword", "\"agi\"", "scaling 'str' debe ser 'agi'")]
    [InlineData("monsters.json", "/monsters/2/level", "0", "level")]
    [InlineData("monsters.json", "/monsters/0/boss", "true", "type 'boss' y boss:true")]
    [InlineData("loot_tables.json", "/lootTables/7/groups/0/rolls", "5", "rolls 5 > 3")]
    [InlineData("classes.json", "/classes/2/startingItems/0/itemId", "\"worn_sword\"", "afinidad baja, debe ser alta")]
    [InlineData("spells.json", "/spells/3/cooldownMs", "500", "minInstantSpellCooldownMs")]
    [InlineData("spells.json", "/spells/2/effects/1/auraId", "\"nope\"", "auraId 'nope' no existe")]
    [InlineData("rules.json", "/ai/wanderPauseMinMs", "7000", "mayor que wanderPauseMaxMs")]
    [InlineData("rules.json", "/loadout/usableSlots", "5", "más que las 8 teclas")]
    public void CrossReferenceRules_Fail(string file, string pointer, string valueJson, string expectedFragment)
    {
        using var dir = new TempContent();
        dir.PatchPointer(file, pointer, valueJson);
        var result = ContentLoader.Load(dir.Path);
        result.Content.ShouldBeNull();
        result.Report.Errors.ShouldContain(e => e.Contains(expectedFragment, StringComparison.Ordinal), string.Join("\n", result.Report.Errors));
    }

    [Fact]
    public void TooManySpellsPerClass_Fails() // HU-003 CA4d
    {
        using var dir = new TempContent();
        dir.Patch("spells.json", j =>
        {
            var arr = j["spells"]!.AsArray();
            var copy = JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonNode>(arr[0]!.ToJsonString())!;
            copy["id"] = "warrior_extra";
            copy["levelReq"] = 15;
            arr.Add(copy);
        });
        ContentLoader.Load(dir.Path).Report.Errors.ShouldContain(e => e.Contains("maxSpellsPerClass", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadRules_Only_ReturnsSameHash()
    {
        var (rules, report) = ContentLoader.LoadRules(TestContent.ContentDir);
        report.Errors.ShouldBeEmpty();
        rules!.Hash.ShouldBe(TestContent.Load().Rules.Hash);
    }
}
