using System.Text.Json;
using System.Text.Json.Nodes;

namespace PixelRealms.Server.Tests.Helpers;

/// <summary>Copia `content/` y `maps/` a carpetas temporales y permite modificar `rules.json` o un `.tmj` (plazos cortos, portales de prueba).</summary>
public sealed class PatchedContent : IDisposable
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public string Dir { get; }

    public string MapsDir { get; }

    private PatchedContent(string dir, string mapsDir) { Dir = dir; MapsDir = mapsDir; }

    public static PatchedContent WithRules(Action<JsonObject> patch) => Create(patch, null);

    public static PatchedContent WithMap(string mapId, Action<JsonObject> patch) => Create(null, (id, map) => { if (id == mapId) patch(map); });

    public static PatchedContent Create(Action<JsonObject>? rules, Action<string, JsonObject>? maps)
    {
        var root = Path.Combine(Path.GetTempPath(), "pr-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, "content");
        var mapsDir = Path.Combine(root, "maps");
        CopyDir(TestContent.ContentDir, dir);
        CopyDir(Path.Combine(TestContent.RepoRoot, "maps"), mapsDir);
        if (rules is not null) Patch(Path.Combine(dir, "rules.json"), rules);
        if (maps is not null)
            foreach (var f in Directory.GetFiles(mapsDir, "*.tmj"))
                Patch(f, o => maps(Path.GetFileNameWithoutExtension(f), o));
        return new PatchedContent(dir, mapsDir);
    }

    /// <summary>Ajustes para `TestServer.StartAsync`.</summary>
    public Dictionary<string, string?> Settings => new() { ["Content:Dir"] = Dir, ["Maps:Dir"] = MapsDir };

    /// <summary>Busca el objeto de la capa `layerName` con ese `name` en un .tmj.</summary>
    public static JsonObject FindObject(JsonObject map, string layerName, string name)
    {
        var layer = map["layers"]!.AsArray().First(l => l!["name"]!.GetValue<string>() == layerName)!;
        return layer["objects"]!.AsArray().First(o => o!["name"]!.GetValue<string>() == name)!.AsObject();
    }

    /// <summary>Fija (o quita, con null) una propiedad personalizada de un objeto Tiled.</summary>
    public static void SetProperty(JsonObject obj, string name, JsonNode? value, string type = "int")
    {
        var props = obj["properties"]!.AsArray();
        var existing = props.FirstOrDefault(p => p!["name"]!.GetValue<string>() == name);
        if (existing is not null) props.Remove(existing);
        if (value is not null) props.Add(new JsonObject { ["name"] = name, ["type"] = type, ["value"] = value });
    }

    private static void Patch(string path, Action<JsonObject> patch)
    {
        var node = (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;
        patch(node);
        File.WriteAllText(path, node.ToJsonString(Indented));
    }

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(Dir)!, true); } catch (IOException) { }
    }
}
