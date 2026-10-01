using System.Text.Json;
using PixelRealms.Game.Map;
using PixelRealms.Game.Movement;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Movement;

/// <summary>HU-021 CA3 / regla 6: todos los casos de shared/test-vectors/movement.json pasan en MovementStep.</summary>
public sealed class MovementVectorTests
{
    public static IEnumerable<object[]> Cases()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestContent.RepoRoot, "shared", "test-vectors", "movement.json")));
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
            yield return [c.GetProperty("name").GetString()!];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Vector_Passes(string name)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestContent.RepoRoot, "shared", "test-vectors", "movement.json")));
        var c = doc.RootElement.GetProperty("cases").EnumerateArray().Single(x => x.GetProperty("name").GetString() == name);
        var rows = c.GetProperty("grid").EnumerateArray().Select(r => r.GetString()!).ToList();
        var grid = new CollisionGrid(rows[0].Length, rows.Count);
        for (var y = 0; y < rows.Count; y++)
            for (var x = 0; x < rows[y].Length; x++)
                if (rows[y][x] == '#') grid.SetSolid(x, y);
        double px = c.GetProperty("start").GetProperty("x").GetDouble(), py = c.GetProperty("start").GetProperty("y").GetDouble();
        var speed = c.GetProperty("speed").GetDouble();
        foreach (var input in c.GetProperty("inputs").EnumerateArray())
        {
            var dx = input.GetProperty("dx").GetInt32(); var dy = input.GetProperty("dy").GetInt32();
            for (var t = 0; t < input.GetProperty("ticks").GetInt32(); t++)
            {
                var r = MovementStep.Step(px, py, dx, dy, speed, grid);
                px = r.X; py = r.Y;
            }
        }
        px.ShouldBe(c.GetProperty("expected").GetProperty("x").GetDouble(), 0.001, name);
        py.ShouldBe(c.GetProperty("expected").GetProperty("y").GetDouble(), 0.001, name);
    }

    [Fact]
    public void Step_ClampsInputs_AndIgnoresEdgeTouch()
    {
        var grid = new CollisionGrid(10, 6);
        grid.SetSolid(5, 2);
        var r = MovementStep.Step(75, 40, 5, 0, 4, grid); // dx 5 → 1; el borde exacto (80) no colisiona
        r.X.ShouldBe(75);
        MovementStep.Step(40, 40, 0, 0, 4, grid).ShouldBe(new MovementStep.Result(40, 40));
        MovementStep.Round2(2.345).ShouldBe(2.35);
        MovementStep.Round2(-2.345).ShouldBe(-2.35);
    }
}
