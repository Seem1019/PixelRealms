using System.Globalization;

namespace PixelRealms.Tools.LoadBot;

public sealed record LoadOptions(int DurationSec, int Bots, int Monsters, int Size, int Seed, string? ContentDir, bool Bench)
{
    public static LoadOptions Parse(string[] args)
    {
        int duration = 300, bots = 30, monsters = 300, size = 60, seed = 7; string? content = null; var bench = false;
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"falta valor para {args[i]}");
            switch (args[i])
            {
                case "--duration": duration = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--bots": bots = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--monsters": monsters = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--size": size = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--seed": seed = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--content": content = Next(); break;
                case "--bench": bench = true; break;
                default: throw new ArgumentException($"argumento desconocido: {args[i]}");
            }
        }
        return new LoadOptions(duration, bots, monsters, size, seed, content, bench);
    }

    public static string FindContentDir(string? explicitDir)
    {
        if (explicitDir is not null) return explicitDir;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "content", "rules.json"))) dir = dir.Parent;
        return dir is null ? throw new InvalidOperationException("No se encontró content/rules.json; usa --content <dir>") : Path.Combine(dir.FullName, "content");
    }
}
