using PixelRealms.Content.Defs;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;

namespace PixelRealms.Game.Combat;

/// <summary>
/// Forma de un área ya colocada (ADR-016, HU-102): círculo en un centro, cono desde un vértice o línea desde un origen, y la
/// prueba de si toca el cuadro del cuerpo de un candidato (el mismo criterio que el círculo de HU-086). Se construye una vez por
/// área (ahí están la raíz y la trigonometría); la prueba por candidato solo suma, multiplica y compara (ADR-018).
/// </summary>
public readonly struct AreaShape
{
    private readonly Vec2 _n1;
    private readonly Vec2 _n2;

    private AreaShape(Shape kind, Vec2 origin, Vec2 dir, float radius, float length, float halfWidth, Vec2 n1, Vec2 n2)
    {
        Kind = kind;
        Origin = origin;
        Dir = dir;
        Radius = radius;
        Length = length;
        HalfWidth = halfWidth;
        _n1 = n1;
        _n2 = n2;
    }

    public Shape Kind { get; }

    /// <summary>Centro del círculo, vértice del cono u origen de la línea.</summary>
    public Vec2 Origin { get; }

    /// <summary>Dirección unitaria del cono y de la línea.</summary>
    public Vec2 Dir { get; }

    /// <summary>Radio del círculo y del cono.</summary>
    public float Radius { get; }

    /// <summary>Largo de la línea (ya recortado en la primera pared).</summary>
    public float Length { get; }

    public float HalfWidth { get; }

    public static AreaShape Circle(Vec2 center, float radius) => new(Shape.Circle, center, Vec2.Zero, radius, 0, 0, Vec2.Zero, Vec2.Zero);

    /// <summary>Cono de apertura `angleDeg` (≤ 180: la cuña es la intersección de dos semiplanos) desde `vertex` hacia `dir` (unitaria).</summary>
    public static AreaShape Cone(Vec2 vertex, Vec2 dir, float radius, float angleDeg)
    {
        var half = angleDeg * MathF.PI / 360f;
        var (sin, cos) = MathF.SinCos(half);
        // Bordes de la cuña (dir girada ±half) y la normal de cada uno que mira hacia dentro (hacia dir).
        var e1 = new Vec2(dir.X * cos - dir.Y * sin, dir.X * sin + dir.Y * cos);
        var e2 = new Vec2(dir.X * cos + dir.Y * sin, -dir.X * sin + dir.Y * cos);
        return new AreaShape(Shape.Cone, vertex, dir, radius, 0, 0, InwardNormal(e1, dir), InwardNormal(e2, dir));
    }

    /// <summary>Rectángulo de `length` × `width` que sale de `origin` hacia `dir` (unitaria).</summary>
    public static AreaShape Line(Vec2 origin, Vec2 dir, float length, float width) =>
        new(Shape.Line, origin, dir, 0, length, width / 2f, Vec2.Zero, Vec2.Zero);

    private static Vec2 InwardNormal(Vec2 edge, Vec2 dir)
    {
        var n = new Vec2(edge.Y, -edge.X);
        return n.X * dir.X + n.Y * dir.Y >= 0 ? n : new Vec2(-n.X, -n.Y);
    }

    /// <summary>
    /// ¿Toca el área el cuadro del cuerpo de quien tiene los pies en `feet`? `distSq` es la distancia al cuadrado del centro, el
    /// vértice o el origen al cuadro: ordena "más cercanos primero" igual en las tres formas.
    /// </summary>
    public bool Touches(Vec2 feet, TargetResolver.BodyBox body, out float distSq)
    {
        distSq = TargetResolver.DistanceSquaredToBody(Origin, feet, body);
        return Kind switch
        {
            Shape.Cone => distSq <= Radius * Radius && (distSq == 0 || ConeTouches(feet, body)),
            Shape.Line => LineTouches(feet, body),
            _ => distSq <= Radius * Radius,
        };
    }

    /// <summary>El cuadro recortado por los dos semiplanos de la cuña queda, en algún punto, a ≤ radio del vértice.</summary>
    private bool ConeTouches(Vec2 feet, TargetResolver.BodyBox body)
    {
        Span<Vec2> a = stackalloc Vec2[8];
        Span<Vec2> b = stackalloc Vec2[8];
        a[0] = new Vec2(feet.X - body.HalfWidth, feet.Y - body.Above);
        a[1] = new Vec2(feet.X + body.HalfWidth, feet.Y - body.Above);
        a[2] = new Vec2(feet.X + body.HalfWidth, feet.Y + body.Below);
        a[3] = new Vec2(feet.X - body.HalfWidth, feet.Y + body.Below);
        var n = Clip(a, 4, b, _n1);
        n = Clip(b, n, a, _n2);
        if (n == 0) return false;
        // El vértice está fuera del cuadro (distSq > 0), así que el punto más cercano del polígono está en un lado.
        var rSq = Radius * Radius;
        for (var i = 0; i < n; i++)
            if (SegmentDistanceSquared(Origin, a[i], a[(i + 1) % n]) <= rSq) return true;
        return false;
    }

    /// <summary>Sutherland-Hodgman contra el semiplano normal·(p − vértice) ≥ 0; devuelve cuántos puntos deja en `dst`.</summary>
    private int Clip(Span<Vec2> src, int count, Span<Vec2> dst, Vec2 normal)
    {
        var m = 0;
        for (var i = 0; i < count; i++)
        {
            var p = src[i];
            var q = src[(i + 1) % count];
            var dp = normal.X * (p.X - Origin.X) + normal.Y * (p.Y - Origin.Y);
            var dq = normal.X * (q.X - Origin.X) + normal.Y * (q.Y - Origin.Y);
            if (dp >= 0) dst[m++] = p;
            if ((dp >= 0) != (dq >= 0))
            {
                var t = dp / (dp - dq);
                dst[m++] = new Vec2(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t);
            }
        }
        return m;
    }

    private static float SegmentDistanceSquared(Vec2 p, Vec2 a, Vec2 b)
    {
        var abx = b.X - a.X;
        var aby = b.Y - a.Y;
        var lenSq = abx * abx + aby * aby;
        var t = lenSq > 0 ? Math.Clamp(((p.X - a.X) * abx + (p.Y - a.Y) * aby) / lenSq, 0f, 1f) : 0f;
        var dx = a.X + abx * t - p.X;
        var dy = a.Y + aby * t - p.Y;
        return dx * dx + dy * dy;
    }

    /// <summary>Ejes separadores entre el cuadro (alineado) y el rectángulo de la línea (girado): X, Y, la dirección y su normal.</summary>
    private bool LineTouches(Vec2 feet, TargetResolver.BodyBox body)
    {
        var bx = body.HalfWidth;
        var by = (body.Above + body.Below) / 2f;
        var boxX = feet.X;
        var boxY = feet.Y + (body.Below - body.Above) / 2f;
        var half = Length / 2f;
        var cx = Origin.X + Dir.X * half;
        var cy = Origin.Y + Dir.Y * half;
        var nx = -Dir.Y;
        var ny = Dir.X;
        var dx = boxX - cx;
        var dy = boxY - cy;
        return MathF.Abs(dx) <= bx + MathF.Abs(Dir.X) * half + MathF.Abs(nx) * HalfWidth
            && MathF.Abs(dy) <= by + MathF.Abs(Dir.Y) * half + MathF.Abs(ny) * HalfWidth
            && MathF.Abs(Dir.X * dx + Dir.Y * dy) <= half + MathF.Abs(Dir.X) * bx + MathF.Abs(Dir.Y) * by
            && MathF.Abs(nx * dx + ny * dy) <= HalfWidth + MathF.Abs(nx) * bx + MathF.Abs(ny) * by;
    }

    /// <summary>Hacia dónde mira el lanzador, para un cono o una línea lanzados sin punto (o con el punto sobre sus pies).</summary>
    public static Vec2 FacingVector(Direction facing) => facing switch
    {
        Direction.N => new Vec2(0, -1),
        Direction.NE => new Vec2(Diagonal, -Diagonal),
        Direction.E => new Vec2(1, 0),
        Direction.SE => new Vec2(Diagonal, Diagonal),
        Direction.S => new Vec2(0, 1),
        Direction.SW => new Vec2(-Diagonal, Diagonal),
        Direction.W => new Vec2(-1, 0),
        _ => new Vec2(-Diagonal, -Diagonal),
    };

    private const float Diagonal = 0.70710677f;
}
