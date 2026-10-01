using PixelRealms.Content;

namespace PixelRealms.Server.Tests.Helpers;

public static class TestContent
{
    private static readonly Lazy<ContentDb> Db = new(() => ContentLoader.LoadOrThrow(ContentDir));

    public static string RepoRoot { get; } = FindRepoRoot();
    public static string ContentDir => Path.Combine(RepoRoot, "content");
    public static ContentDb Load() => Db.Value;

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "content", "rules.json"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repo (content/rules.json)");
    }
}
