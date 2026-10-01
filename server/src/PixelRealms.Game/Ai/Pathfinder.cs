using PixelRealms.Game.Core;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Ai;

/// <summary>
/// A* sobre la grilla de colisión (skill combat-system §IA): 8 direcciones sin cortar esquinas, límite de nodos expandidos
/// (si se agota, devuelve false → el monstruo evade). Devuelve el camino como centros de casilla, sin incluir la de origen.
/// </summary>
public static class Pathfinder
{
    public const int DefaultMaxNodes = 200;

    private static readonly (int Dx, int Dy, float Cost)[] Neighbors =
    [
        (1, 0, 1f), (-1, 0, 1f), (0, 1, 1f), (0, -1, 1f),
        (1, 1, 1.41421356f), (-1, 1, 1.41421356f), (1, -1, 1.41421356f), (-1, -1, 1.41421356f),
    ];

    // Buffers reutilizados por hilo (HU-088 CA1): el tick es un solo hilo; los tests en paralelo tienen cada uno el suyo.
    [ThreadStatic] private static PriorityQueue<(int X, int Y), float>? _open;
    [ThreadStatic] private static Dictionary<(int, int), (int, int)>? _cameFrom;
    [ThreadStatic] private static Dictionary<(int, int), float>? _gScore;

    public static bool FindPath(CollisionGrid grid, Vec2 from, Vec2 to, List<Vec2> path, int maxNodes = DefaultMaxNodes)
    {
        path.Clear();
        var start = ((int)MathF.Floor(from.X), (int)MathF.Floor(from.Y));
        var goal = ((int)MathF.Floor(to.X), (int)MathF.Floor(to.Y));
        if (start == goal) return true;
        if (grid.IsSolid(goal.Item1, goal.Item2)) goal = NearestFree(grid, goal, start);

        var open = _open ??= new PriorityQueue<(int X, int Y), float>(256);
        var cameFrom = _cameFrom ??= new Dictionary<(int, int), (int, int)>(256);
        var gScore = _gScore ??= new Dictionary<(int, int), float>(256);
        open.Clear(); cameFrom.Clear(); gScore.Clear();
        gScore[start] = 0;
        open.Enqueue(start, Heuristic(start, goal));
        var expanded = 0;
        while (open.Count > 0)
        {
            var current = open.Dequeue();
            if (current == goal) { Reconstruct(cameFrom, current, start, path); return true; }
            if (++expanded > maxNodes) return false;
            var g = gScore[current];
            foreach (var (dx, dy, cost) in Neighbors)
            {
                var nx = current.X + dx; var ny = current.Y + dy;
                if (grid.IsSolid(nx, ny)) continue;
                // Sin cortar esquinas: en diagonal, las dos ortogonales deben estar libres.
                if (dx != 0 && dy != 0 && (grid.IsSolid(current.X + dx, current.Y) || grid.IsSolid(current.X, current.Y + dy))) continue;
                var ng = g + cost;
                var key = (nx, ny);
                if (gScore.TryGetValue(key, out var old) && ng >= old) continue;
                gScore[key] = ng;
                cameFrom[key] = current;
                open.Enqueue(key, ng + Heuristic(key, goal));
            }
        }
        return false;
    }

    private static float Heuristic((int X, int Y) a, (int X, int Y) b)
    {
        var dx = Math.Abs(a.X - b.X); var dy = Math.Abs(a.Y - b.Y);
        return Math.Max(dx, dy) + 0.41421356f * Math.Min(dx, dy);
    }

    private static (int, int) NearestFree(CollisionGrid grid, (int X, int Y) goal, (int X, int Y) from)
    {
        var best = goal; var bestD = int.MaxValue;
        for (var r = 1; r <= 2; r++)
            for (var dy = -r; dy <= r; dy++)
                for (var dx = -r; dx <= r; dx++)
                {
                    var p = (goal.X + dx, goal.Y + dy);
                    if (grid.IsSolid(p.Item1, p.Item2)) continue;
                    var d = Math.Abs(p.Item1 - from.X) + Math.Abs(p.Item2 - from.Y);
                    if (d < bestD) { bestD = d; best = p; }
                }
        return best;
    }

    private static void Reconstruct(Dictionary<(int, int), (int, int)> cameFrom, (int, int) current, (int, int) start, List<Vec2> path)
    {
        while (current != start)
        {
            path.Add(new Vec2(current.Item1 + 0.5f, current.Item2 + 0.5f));
            current = cameFrom[current];
        }
        path.Reverse();
    }
}
