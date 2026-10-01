using System.Text.Json;
using System.Text.Json.Nodes;

namespace PixelRealms.Game.Tests.Helpers;

/// <summary>Copia temporal de content/ para romper un JSON en un test sin tocar el repo.</summary>
public sealed class TempContent : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pr-content-" + Guid.NewGuid().ToString("N"));

    public TempContent()
    {
        Directory.CreateDirectory(System.IO.Path.Combine(Path, "schemas"));
        foreach (var f in Directory.GetFiles(TestContent.ContentDir, "*.json")) File.Copy(f, System.IO.Path.Combine(Path, System.IO.Path.GetFileName(f)));
        foreach (var f in Directory.GetFiles(System.IO.Path.Combine(TestContent.ContentDir, "schemas"), "*.json")) File.Copy(f, System.IO.Path.Combine(Path, "schemas", System.IO.Path.GetFileName(f)));
    }

    public void Patch(string file, Action<JsonNode> mutate)
    {
        var p = System.IO.Path.Combine(Path, file);
        var node = JsonNode.Parse(File.ReadAllText(p))!;
        mutate(node);
        File.WriteAllText(p, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Sustituye el valor en un JSON pointer ("/spells/3/cooldownMs") por el JSON dado.</summary>
    public void PatchPointer(string file, string pointer, string valueJson) => Patch(file, root =>
    {
        var segs = pointer.Split('/', StringSplitOptions.RemoveEmptyEntries);
        JsonNode node = root;
        for (var i = 0; i < segs.Length - 1; i++) node = int.TryParse(segs[i], out var idx) ? node[idx]! : node[segs[i]]!;
        var last = segs[^1];
        var value = JsonNode.Parse(valueJson);
        if (int.TryParse(last, out var li)) node[li] = value; else node[last] = value;
    });

    public void Dispose() => Directory.Delete(Path, true);
}
