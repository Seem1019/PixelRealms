using PixelRealms.Game.Core;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Combat;

/// <summary>Línea de visión: Bresenham sobre la grilla de colisión, bloqueada por casillas con `blocksSight` (combat.md §Casteo).</summary>
public static class LineOfSight
{
    public static bool Has(CollisionGrid grid, Vec2 from, Vec2 to)
    {
        int x0 = (int)MathF.Floor(from.X), y0 = (int)MathF.Floor(from.Y);
        int x1 = (int)MathF.Floor(to.X), y1 = (int)MathF.Floor(to.Y);
        var dx = Math.Abs(x1 - x0);
        var dy = -Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;
        while (true)
        {
            // La casilla de origen y la de destino no bloquean (el actor puede estar pegado a un muro).
            if ((x0 != (int)MathF.Floor(from.X) || y0 != (int)MathF.Floor(from.Y)) && (x0 != x1 || y0 != y1) && grid.BlocksSight(x0, y0)) return false;
            if (x0 == x1 && y0 == y1) return true;
            var e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }
}
