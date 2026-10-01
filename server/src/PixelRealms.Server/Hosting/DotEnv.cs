namespace PixelRealms.Server.Hosting;

/// <summary>Carga `.env` de la raíz del repo (si existe) en la configuración, para que POSTGRES_* y JWT_SIGNING_KEY lleguen al servidor (HU-002).</summary>
public static class DotEnv
{
    public static Dictionary<string, string?> Load(string? path = null)
    {
        path ??= FindUp(".env");
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (path is null || !File.Exists(path)) return result;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim().Trim('"');
            result[key] = value;
        }
        return result;
    }

    public static string? FindUp(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
