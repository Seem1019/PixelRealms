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

    /// <summary>
    /// HU-102: cuánto avanza un rayo desde `from` en la dirección unitaria `dir` antes de entrar en la primera casilla que bloquea la
    /// vista (la de origen no cuenta), como mucho `maxDistance`. Recorre todas las casillas que cruza (Amanatides-Woo): una línea no
    /// atraviesa muros.
    /// </summary>
    public static float ClearDistance(CollisionGrid grid, Vec2 from, Vec2 dir, float maxDistance)
    {
        var x = (int)MathF.Floor(from.X);
        var y = (int)MathF.Floor(from.Y);
        var stepX = Math.Sign(dir.X);
        var stepY = Math.Sign(dir.Y);
        if (stepX == 0 && stepY == 0) return maxDistance;
        var deltaX = stepX != 0 ? 1f / MathF.Abs(dir.X) : float.PositiveInfinity;
        var deltaY = stepY != 0 ? 1f / MathF.Abs(dir.Y) : float.PositiveInfinity;
        var nextX = stepX > 0 ? (x + 1 - from.X) * deltaX : stepX < 0 ? (from.X - x) * deltaX : float.PositiveInfinity;
        var nextY = stepY > 0 ? (y + 1 - from.Y) * deltaY : stepY < 0 ? (from.Y - y) * deltaY : float.PositiveInfinity;
        while (true)
        {
            float t;
            if (nextX < nextY) { t = nextX; x += stepX; nextX += deltaX; }
            else { t = nextY; y += stepY; nextY += deltaY; }
            if (t >= maxDistance) return maxDistance;
            if (grid.BlocksSight(x, y)) return t; // fuera del mapa también bloquea: el bucle siempre termina
        }
    }
}
