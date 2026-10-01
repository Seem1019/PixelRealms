namespace PixelRealms.Game.Core;

/// <summary>Posición o vector en casillas (tiles, float). Una casilla = 16 px en el protocolo.</summary>
public readonly record struct Vec2(float X, float Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);

    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);

    public static Vec2 operator *(Vec2 a, float k) => new(a.X * k, a.Y * k);

    public float LengthSquared => X * X + Y * Y;

    public float Length => MathF.Sqrt(LengthSquared);

    public Vec2 Normalized() { var l = Length; return l > 0 ? new Vec2(X / l, Y / l) : Zero; }

    public static float DistanceSquared(Vec2 a, Vec2 b) => (a - b).LengthSquared;

    public static float Distance(Vec2 a, Vec2 b) => MathF.Sqrt(DistanceSquared(a, b));

    public float Dot(Vec2 o) => X * o.X + Y * o.Y;

    public override string ToString() => $"({X:0.##}, {Y:0.##})";
}
