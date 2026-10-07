using System.Text.Json;
using PixelRealms.Content;
using PixelRealms.Content.Defs;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Content;

/// <summary>HU-105: los casos de shared/test-vectors/spell_upgrades.json pasan en SpellUpgrades.Apply (el cliente los pasa en
/// spell_upgrades.gd: los números que muestra el libro son los que usa el servidor).</summary>
public sealed class SpellUpgradeVectorTests
{
    private static JsonDocument Load() => JsonDocument.Parse(File.ReadAllText(Path.Combine(TestContent.RepoRoot, "shared", "test-vectors", "spell_upgrades.json")));

    public static IEnumerable<object[]> Cases()
    {
        using var doc = Load();
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
            yield return [c.GetProperty("name").GetString()!];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Vector_Passes(string name)
    {
        using var doc = Load();
        var c = doc.RootElement.GetProperty("cases").EnumerateArray().Single(x => x.GetProperty("name").GetString() == name);
        var spell = ContentJson.Deserialize<SpellDef>(c.GetProperty("spell").GetRawText());
        var auras = c.GetProperty("auras").EnumerateArray().Select(a => ContentJson.Deserialize<AuraDef>(a.GetRawText())).ToDictionary(a => a.Id);
        var upgrade = spell.Upgrades.Single(u => u.Id == c.GetProperty("upgradeId").GetString());
        var s = SpellUpgrades.Apply(spell, upgrade, id => auras[id]);

        var exp = c.GetProperty("expected");
        s.CastMs.ShouldBe(exp.GetProperty("castMs").GetInt32(), name);
        s.CooldownMs.ShouldBe(exp.GetProperty("cooldownMs").GetInt32(), name);
        (s.Cost?.Amount ?? 0).ShouldBe(exp.GetProperty("cost").GetInt32(), name);
        s.MaxTargets.ShouldBe(exp.GetProperty("maxTargets").GetInt32(), name);
        foreach (var (key, actual) in new[] { ("range", s.Range), ("aoeRadius", s.AoeRadius), ("aoeAngleDeg", s.AoeAngleDeg), ("aoeLength", s.AoeLength), ("aoeWidth", s.AoeWidth) })
            actual.ShouldBe(exp.GetProperty(key).GetDouble(), 1e-6, $"{name}: {key}");

        var effects = exp.GetProperty("effects").EnumerateArray().ToList();
        s.Effects.Count.ShouldBe(effects.Count, name);
        for (var i = 0; i < effects.Count; i++)
        {
            ContentJson.EnumName(s.Effects[i].Type).ShouldBe(effects[i].GetProperty("type").GetString(), name);
            foreach (var (key, actual) in new[] { ("base", s.Effects[i].Base), ("apCoef", s.Effects[i].ApCoef), ("spCoef", s.Effects[i].SpCoef), ("weaponPct", s.Effects[i].WeaponPct) })
                if (effects[i].TryGetProperty(key, out var e)) actual.ShouldBe(e.GetDouble(), 1e-6, $"{name}: efecto {i} {key}");
        }
        foreach (var aura in exp.GetProperty("auras").EnumerateObject())
        {
            var over = s.Effects.Single(e => e.AuraId == aura.Name).AuraOverride.ShouldNotBeNull();
            over.DurationMs.ShouldBe(aura.Value.GetProperty("durationMs").GetInt32(), name);
            over.Pct.ShouldBe(aura.Value.GetProperty("pct").GetDouble(), 1e-6, name);
            over.Base.ShouldBe(aura.Value.GetProperty("base").GetDouble(), 1e-6, name);
            over.SpCoef.ShouldBe(aura.Value.GetProperty("spCoef").GetDouble(), 1e-6, name);
        }
    }
}
