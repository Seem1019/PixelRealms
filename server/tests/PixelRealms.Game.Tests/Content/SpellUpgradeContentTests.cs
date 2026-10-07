using System.Text.Json.Nodes;
using PixelRealms.Content;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Content;

/// <summary>HU-104 CA1: el schema y el validador comprueban las mejoras (ids únicos, cuatro tipos de modificador que apuntan a algo
/// que el hechizo tiene, y el hechizo mejorado sigue siendo válido).</summary>
public sealed class SpellUpgradeContentTests
{
    private const string Valid = """
        [{ "id": "a_one", "name": "Uno", "description": "Uno.", "mods": [{ "stat": "range", "add": 2 }] },
         { "id": "a_two", "name": "Dos", "description": "Dos.", "mods": [{ "effect": "damage", "mult": 1.1 }] }]
        """;

    private static JsonNode? Spell(JsonNode root, string id) =>
        root["spells"]!.AsArray().First(s => s!["id"]!.GetValue<string>() == id);

    private static ContentLoader.LoadResult LoadWith(string spellId, string upgradesJson, Action<TempContent>? more = null)
    {
        using var dir = new TempContent();
        dir.Patch("spells.json", root => Spell(root, spellId)!["upgrades"] = JsonNode.Parse(upgradesJson));
        more?.Invoke(dir);
        return ContentLoader.Load(dir.Path);
    }

    private static void ShouldFailWith(ContentLoader.LoadResult result, string fragment)
    {
        result.Content.ShouldBeNull();
        result.Report.Errors.ShouldContain(e => e.Contains(fragment, StringComparison.Ordinal), string.Join("\n", result.Report.Errors));
    }

    [Fact]
    public void ValidUpgrades_Load()
    {
        var result = LoadWith("mage_fireball", Valid);
        result.Report.Errors.ShouldBeEmpty();
        result.Content!.Upgraded("mage_fireball", "a_one")!.Range.ShouldBe(result.Content.Spell("mage_fireball").Range + 2);
    }

    [Fact]
    public void ExactlyTwo() =>
        ShouldFailWith(LoadWith("mage_fireball", """[{ "id": "a_one", "name": "Uno", "description": "Uno.", "mods": [{ "stat": "range", "add": 2 }] }]"""), "upgrades");

    [Fact]
    public void OnlyClassSpells() => ShouldFailWith(LoadWith("foreman_slam", Valid), "solo los hechizos de clase tienen mejoras");

    [Fact]
    public void UpgradeIds_AreUniqueAcrossSpells() =>
        ShouldFailWith(LoadWith("mage_fireball", Valid, d => d.Patch("spells.json", root => Spell(root, "mage_frostbolt")!["upgrades"] = JsonNode.Parse(Valid))),
            "id de mejora 'a_one' repetido");

    [Theory]
    [InlineData("""{ "effect": "heal", "mult": 1.2 }""", "el hechizo no tiene efectos 'heal'")]
    [InlineData("""{ "effect": "damage", "add": 3 }""", "solo escala")]
    [InlineData("""{ "aura": "mage_chill", "stat": "durationMs", "add": 1000 }""", "no aplica el aura 'mage_chill'")]
    [InlineData("""{ "stat": "pct", "add": 0.1 }""", "no es un campo de hechizo que se pueda mejorar")]
    [InlineData("""{ "stat": "range", "effect": "damage", "mult": 2 }""", "uno solo")]
    [InlineData("""{ "stat": "range" }""", "no cambia nada")]
    [InlineData("""{ "addEffect": { "type": "apply_aura", "auraId": "nope" } }""", "auraId 'nope' no existe")]
    [InlineData("""{ "stat": "castMs", "add": -2000 }""", "minInstantSpellCooldownMs")] // queda instantánea con cooldown 0
    [InlineData("""{ "stat": "range", "add": -20 }""", "valor fuera de rango")]
    public void BadModifiers_Fail(string mod, string fragment) => ShouldFailWith(LoadWith("mage_fireball", $$"""
        [{ "id": "a_one", "name": "Uno", "description": "Uno.", "mods": [{{mod}}] },
         { "id": "a_two", "name": "Dos", "description": "Dos.", "mods": [{ "stat": "range", "add": 1 }] }]
        """), fragment);

    [Fact]
    public void AnUpgradeCannotMakeAnInstantConeUndodgeable() // ADR-027 D4: Tajo amplio pasa de 2,5 a 3,5 casillas sin casteo
        => ShouldFailWith(LoadWith("warrior_cleave", """
            [{ "id": "a_one", "name": "Uno", "description": "Uno.", "mods": [{ "stat": "aoeRadius", "add": 1 }] },
             { "id": "a_two", "name": "Dos", "description": "Dos.", "mods": [{ "stat": "aoeAngleDeg", "add": 20 }] }]
            """), "la mejora deja un área de daño sin casteo");

    [Fact]
    public void AnAddedBuff_CannotOutlastTheCooldown() // revisión de autoridad: se lanzaría con una mejora, se cambiaría y se tendrían las dos
        => ShouldFailWith(LoadWith("rogue_sprint", """
            [{ "id": "a_one", "name": "Uno", "description": "Uno.", "mods": [{ "addEffect": { "type": "apply_aura", "auraId": "priest_power_shield_speed" } }, { "stat": "cooldownMs", "mult": 0.05 }] },
             { "id": "a_two", "name": "Dos", "description": "Dos.", "mods": [{ "stat": "cooldownMs", "add": -1000 }] }]
            """), "más que la recarga");

    [Fact]
    public void UpgradeLevel_MustBeARankLevel()
    {
        using var dir = new TempContent();
        dir.PatchPointer("rules.json", "/progression/spellUpgradeLevel", "9");
        ShouldFailWith(ContentLoader.Load(dir.Path), "spellUpgradeLevel 9 no es un nivel de rango");
    }
}
