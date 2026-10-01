using System.Text.Json.Nodes;

namespace PixelRealms.Server.Tests.Helpers;

/// <summary>Copia `content/` a una carpeta temporal y modifica `rules.json` (para acortar plazos en tests de integración).</summary>
public sealed class PatchedContent : IDisposable
{
    public string Dir { get; }

    private PatchedContent(string dir) => Dir = dir;

    public static PatchedContent WithRules(Action<JsonObject> patch)
    {
        var dir = Path.Combine(Path.GetTempPath(), "pr-content-" + Guid.NewGuid().ToString("N"));
        CopyDir(TestContent.ContentDir, dir);
        var rulesPath = Path.Combine(dir, "rules.json");
        var rules = (JsonObject)JsonNode.Parse(File.ReadAllText(rulesPath))!;
        patch(rules);
        File.WriteAllText(rulesPath, rules.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return new PatchedContent(dir);
    }

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }

    public void Dispose()
    {
        try { Directory.Delete(Dir, true); } catch (IOException) { }
    }
}
