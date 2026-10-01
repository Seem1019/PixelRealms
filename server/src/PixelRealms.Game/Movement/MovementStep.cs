using PixelRealms.Game.Map;

namespace PixelRealms.Game.Movement;

/// <summary>
/// Algoritmo de movimiento compartido con el cliente (shared/test-vectors/movement.json, docs/architecture.md §4). Trabaja en
/// píxeles: caja de 10×6 px en los pies (half extents 5×3), paso = speed · 16 · dt, eje X y luego eje Y deslizando por las
/// paredes, borde exacto sin colisión y redondeo a 2 decimales (half away from zero) al final de cada tick.
/// Cualquier cambio aquí obliga a cambiar `client/scripts/net/movement_step.gd` y los vectores (regla 6).
/// </summary>
public static class MovementStep
{
    public const int TileSize = 16;
    public const double DtSeconds = 0.05;
    public const double HalfX = 5;
    public const double HalfY = 3;
    public const double Diagonal = 0.7071067811865476;

    public readonly record struct Result(double X, double Y);

    public static Result Step(double x, double y, int dx, int dy, double speedTilesPerSec, CollisionGrid grid, double dt = DtSeconds)
    {
        dx = Math.Clamp(dx, -1, 1);
        dy = Math.Clamp(dy, -1, 1);
        if (dx == 0 && dy == 0) return new Result(Round2(x), Round2(y));

        var stepPx = speedTilesPerSec * TileSize * dt;
        var dirX = (double)dx;
        var dirY = (double)dy;
        if (dx != 0 && dy != 0) { dirX *= Diagonal; dirY *= Diagonal; }

        var nx = x + dirX * stepPx;
        if (dx != 0 && Collides(grid, nx, y, out var colX, out _))
            nx = dx > 0 ? colX * TileSize - HalfX : (colX + 1) * TileSize + HalfX;

        var ny = y + dirY * stepPx;
        if (dy != 0 && Collides(grid, nx, ny, out _, out var rowY))
            ny = dy > 0 ? rowY * TileSize - HalfY : (rowY + 1) * TileSize + HalfY;

        return new Result(Round2(nx), Round2(ny));
    }

    /// <summary>¿El AABB centrado en (x, y) toca un tile sólido? Devuelve la columna/fila del primer tile sólido encontrado (el más cercano al cuerpo según el sentido se resuelve arriba).</summary>
    private static bool Collides(CollisionGrid grid, double x, double y, out int solidCol, out int solidRow)
    {
        var minCol = (int)Math.Floor((x - HalfX) / TileSize);
        var maxCol = (int)Math.Ceiling((x + HalfX) / TileSize) - 1;
        var minRow = (int)Math.Floor((y - HalfY) / TileSize);
        var maxRow = (int)Math.Ceiling((y + HalfY) / TileSize) - 1;
        solidCol = 0; solidRow = 0;
        for (var row = minRow; row <= maxRow; row++)
            for (var col = minCol; col <= maxCol; col++)
                if (grid.IsSolid(col, row)) { solidCol = col; solidRow = row; return true; }
        return false;
    }

    public static double Round2(double v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
