using System.Diagnostics;
using System.Globalization;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Map;

namespace PixelRealms.Tools.LoadBot;

/// <summary>HU-089 CA4 sin BenchmarkDotNet (no disponible): Stopwatch sobre 1 M de pruebas de forma y una consulta de área con 100 candidatos.</summary>
public static class MicroBench
{
    public static void Run()
    {
        var ci = CultureInfo.InvariantCulture;
        var rng = new SeededRng(1);
        var points = new Vec2[1_000_000];
        for (var i = 0; i < points.Length; i++) points[i] = new Vec2((float)(rng.NextDouble() * 60), (float)(rng.NextDouble() * 60));
        var center = new Vec2(30, 30); const float radius = 3f;
        // Calentamiento.
        var hits = 0; for (var i = 0; i < 100_000; i++) if (Vec2.Distance(points[i], center) <= radius) hits++;
        var sw = Stopwatch.StartNew();
        hits = 0;
        for (var i = 0; i < points.Length; i++) if (Vec2.Distance(points[i], center) <= radius) hits++;
        sw.Stop();
        Console.WriteLine($"1 000 000 pruebas de forma (círculo r={radius}): {sw.Elapsed.TotalMilliseconds.ToString("F2", ci)} ms (objetivo < 5) · dentro {hits}");

        // Consulta de área con 100 candidatos: línea de visión + distancia por candidato (lo que hace TargetResolver por área).
        var grid = new CollisionGrid(60, 60);
        for (var i = 0; i < 60; i++) { grid.SetSolid(i, 0); grid.SetSolid(0, i); }
        var candidates = new Vec2[100];
        for (var i = 0; i < 100; i++) candidates[i] = new Vec2(25 + (float)(rng.NextDouble() * 10), 25 + (float)(rng.NextDouble() * 10));
        for (var w = 0; w < 1000; w++) Query(grid, center, radius, candidates);
        const int reps = 10_000;
        sw.Restart();
        var total = 0;
        for (var r = 0; r < reps; r++) total += Query(grid, center, radius, candidates);
        sw.Stop();
        Console.WriteLine($"consulta de área con 100 candidatos (distancia + LOS): {(sw.Elapsed.TotalMilliseconds * 1000 / reps).ToString("F2", ci)} µs (objetivo < 20) · media {total / reps} alcanzados");
    }

    private static int Query(CollisionGrid grid, Vec2 center, float radius, Vec2[] candidates)
    {
        var n = 0;
        foreach (var c in candidates)
            if (Vec2.Distance(c, center) <= radius && LineOfSight.Has(grid, center, c)) n++;
        return n;
    }
}
