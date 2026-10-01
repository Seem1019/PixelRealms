namespace PixelRealms.Game.Map;

/// <summary>Grilla de colisión del mapa: sólido (bloquea movimiento) y bloquea visión, por casilla. Inmutable tras construirse.</summary>
public sealed class CollisionGrid
{
    private readonly bool[] _solid;
    private readonly bool[] _blocksSight;

    public CollisionGrid(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        Width = width;
        Height = height;
        _solid = new bool[width * height];
        _blocksSight = new bool[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>Fuera del mapa cuenta como sólido (los bordes siempre bloquean).</summary>
    public bool IsSolid(int x, int y) => !InBounds(x, y) || _solid[y * Width + x];

    public bool BlocksSight(int x, int y) => !InBounds(x, y) || _blocksSight[y * Width + x];

    public void SetSolid(int x, int y, bool value = true) { if (InBounds(x, y)) _solid[y * Width + x] = value; }

    public void SetBlocksSight(int x, int y, bool value = true) { if (InBounds(x, y)) _blocksSight[y * Width + x] = value; }

    /// <summary>Casilla sólida en coordenadas continuas (tiles).</summary>
    public bool IsSolidAt(float x, float y) => IsSolid((int)MathF.Floor(x), (int)MathF.Floor(y));
}
