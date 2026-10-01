using PixelRealms.Game.Core;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Combat;

/// <summary>Desplazamientos por habilidad (ADR-016): `leap` a un punto y `dash` (Carga) hasta quedar adyacente al objetivo. No pasan por MovementStep.</summary>
public static class ForcedMovement
{
    private const float LeapSampleTiles = 0.25f;

    /// <summary>
    /// Destino del salto: `targetPos` recortado a `maxRange` y a la última casilla libre con línea de visión desde el origen
    /// (HU-087 CA1/CA5). Nunca atraviesa colisiones ni sale del mapa.
    /// </summary>
    public static Vec2 LeapDestination(Vec2 from, Vec2 targetPos, double maxRange, CollisionGrid grid)
    {
        var delta = targetPos - from;
        var dist = delta.Length;
        if (dist <= 0.0001f) return from;
        var range = (float)Math.Min(dist, maxRange);
        var dir = delta.Normalized();
        var best = from;
        var steps = Math.Max(1, (int)MathF.Ceiling(range / LeapSampleTiles));
        for (var i = 1; i <= steps; i++)
        {
            var d = Math.Min(range, i * LeapSampleTiles);
            var p = from + dir * d;
            if (!grid.InBounds((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y))) break;
            if (grid.IsSolidAt(p.X, p.Y)) break;
            if (!LineOfSight.Has(grid, from, p)) break;
            best = p;
        }
        return best;
    }

    /// <summary>Posición adyacente al objetivo (a 1 casilla, del lado del lanzador) para Carga; si está ocupada, prueba alrededor.</summary>
    public static Vec2 DashDestination(Vec2 from, Vec2 target, CollisionGrid grid)
    {
        var back = (from - target).Normalized();
        if (back.LengthSquared < 0.5f) back = new Vec2(0, 1);
        var candidate = target + back;
        if (!grid.IsSolidAt(candidate.X, candidate.Y)) return candidate;
        ReadOnlySpan<(float, float)> ring = [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, 1), (1, -1), (-1, -1)];
        foreach (var (dx, dy) in ring)
        {
            var p = new Vec2(target.X + dx, target.Y + dy);
            if (!grid.IsSolidAt(p.X, p.Y)) return p;
        }
        return from;
    }
}
