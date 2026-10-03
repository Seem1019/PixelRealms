using System.Diagnostics;
using System.Globalization;
using PixelRealms.Content;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Map;

namespace PixelRealms.Tools.LoadBot;

/// <summary>
/// HU-089 CA4 sin BenchmarkDotNet (no disponible): Stopwatch sobre 1 M de pruebas de forma y una consulta de área con 100
/// candidatos, con la prueba real de TargetResolver (distancia al cuadrado al cuadro del cuerpo, sin raíces: ADR-018). Falla
/// (código de salida 1) si supera los umbrales de la HU.
/// </summary>
public static class MicroBench
{
    public const double ShapeTestsLimitMs = 5, AreaQueryLimitUs = 20;

    public static bool Run(LoadOptions o)
    {
        var ci = CultureInfo.InvariantCulture;
        var combat = TargetResolver.BodyBox.From(ContentLoader.LoadOrThrow(LoadOptions.FindContentDir(o.ContentDir)).Rules.Combat);
        var rng = new SeededRng(1);
        var points = new Vec2[1_000_000];
        for (var i = 0; i < points.Length; i++) points[i] = new Vec2((float)(rng.NextDouble() * 60), (float)(rng.NextDouble() * 60));
        var center = new Vec2(30, 30); const float radius = 3f;
        const float radiusSq = radius * radius;
        // Calentamiento.
        var hits = 0; for (var i = 0; i < 100_000; i++) if (TargetResolver.DistanceSquaredToBody(center, points[i], combat) <= radiusSq) hits++;
        var sw = Stopwatch.StartNew();
        hits = 0;
        for (var i = 0; i < points.Length; i++) if (TargetResolver.DistanceSquaredToBody(center, points[i], combat) <= radiusSq) hits++;
        sw.Stop();
        var shapeMs = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"1 000 000 pruebas de forma (círculo r={radius} contra el cuadro del cuerpo): {shapeMs.ToString("F2", ci)} ms (< {ShapeTestsLimitMs}) · dentro {hits}");

        // Consulta de área con 100 candidatos: forma + línea de visión por candidato (lo que hace TargetResolver por área).
        var grid = new CollisionGrid(60, 60);
        for (var i = 0; i < 60; i++) { grid.SetSolid(i, 0); grid.SetSolid(0, i); }
        var candidates = new Vec2[100];
        for (var i = 0; i < 100; i++) candidates[i] = new Vec2(25 + (float)(rng.NextDouble() * 10), 25 + (float)(rng.NextDouble() * 10));
        for (var w = 0; w < 1000; w++) Query(grid, center, radiusSq, candidates, combat);
        const int reps = 10_000;
        sw.Restart();
        var total = 0;
        for (var r = 0; r < reps; r++) total += Query(grid, center, radiusSq, candidates, combat);
        sw.Stop();
        var queryUs = sw.Elapsed.TotalMilliseconds * 1000 / reps;
        Console.WriteLine($"consulta de área con 100 candidatos (forma + LOS): {queryUs.ToString("F2", ci)} µs (< {AreaQueryLimitUs}) · media {total / reps} alcanzados");
        var passed = shapeMs < ShapeTestsLimitMs && queryUs < AreaQueryLimitUs;
        Console.WriteLine(passed ? "RESULTADO: OK" : "RESULTADO: FALLA (ver umbrales)");
        return passed;
    }

    private static int Query(CollisionGrid grid, Vec2 center, float radiusSq, Vec2[] candidates, TargetResolver.BodyBox combat)
    {
        var n = 0;
        foreach (var c in candidates)
            if (TargetResolver.DistanceSquaredToBody(center, c, combat) <= radiusSq && LineOfSight.Has(grid, center, c)) n++;
        return n;
    }
}
