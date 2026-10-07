using System.Text.Json;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Map;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Combat;

/// <summary>HU-102: los casos de shared/test-vectors/area_shapes.json pasan en AreaShape y LineOfSight.ClearDistance (el cliente
/// los pasa en area_geometry.gd: lo que marca al apuntar es lo que alcanza el servidor).</summary>
public sealed class AreaShapeVectorTests
{
    private static JsonDocument Load() => JsonDocument.Parse(File.ReadAllText(Path.Combine(TestContent.RepoRoot, "shared", "test-vectors", "area_shapes.json")));

    private static Vec2 V(JsonElement a) => new(a[0].GetSingle(), a[1].GetSingle());

    public static IEnumerable<object[]> Cases()
    {
        using var doc = Load();
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
            yield return [c.GetProperty("name").GetString()!];
    }

    public static IEnumerable<object[]> ClearCases()
    {
        using var doc = Load();
        foreach (var c in doc.RootElement.GetProperty("clear").EnumerateArray())
            yield return [c.GetProperty("name").GetString()!];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Shape_Vector_Passes(string name)
    {
        using var doc = Load();
        var b = doc.RootElement.GetProperty("body");
        var body = new TargetResolver.BodyBox(b.GetProperty("halfWidth").GetSingle(), b.GetProperty("above").GetSingle(), b.GetProperty("below").GetSingle());
        var c = doc.RootElement.GetProperty("cases").EnumerateArray().Single(x => x.GetProperty("name").GetString() == name);
        var dir = V(c.GetProperty("dir")).Normalized();
        var shape = c.GetProperty("shape").GetString() == "cone"
            ? AreaShape.Cone(V(c.GetProperty("origin")), dir, c.GetProperty("radius").GetSingle(), c.GetProperty("angleDeg").GetSingle())
            : AreaShape.Line(V(c.GetProperty("origin")), dir, c.GetProperty("length").GetSingle(), c.GetProperty("width").GetSingle());
        shape.Touches(V(c.GetProperty("feet")), body, out _).ShouldBe(c.GetProperty("hit").GetBoolean(), name);
    }

    [Theory]
    [MemberData(nameof(ClearCases))]
    public void ClearDistance_Vector_Passes(string name)
    {
        using var doc = Load();
        var c = doc.RootElement.GetProperty("clear").EnumerateArray().Single(x => x.GetProperty("name").GetString() == name);
        var grid = new CollisionGrid(30, 30);
        foreach (var w in c.GetProperty("walls").EnumerateArray()) grid.SetBlocksSight(w[0].GetInt32(), w[1].GetInt32());
        LineOfSight.ClearDistance(grid, V(c.GetProperty("from")), V(c.GetProperty("dir")).Normalized(), c.GetProperty("max").GetSingle())
            .ShouldBe(c.GetProperty("expected").GetSingle(), 1e-3, name);
    }
}
