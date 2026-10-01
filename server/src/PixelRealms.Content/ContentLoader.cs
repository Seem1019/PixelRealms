using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PixelRealms.Content.Defs;
using PixelRealms.Content.Validation;

namespace PixelRealms.Content;

/// <summary>El contenido no pasó la validación: la lista de errores se imprime tal cual (ContentValidator, arranque del servidor).</summary>
public sealed class ContentValidationException(IReadOnlyList<string> errors)
    : Exception($"content/ inválido: {errors.Count} error(es)\n" + string.Join("\n", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// Carga content/: valida cada archivo contra su schema, comprueba las referencias cruzadas y deserializa a modelos inmutables.
/// Orden: schemas → datos → cruzadas → ContentDb. Cualquier error impide construir el ContentDb (HU-003 CA5).
/// </summary>
public static class ContentLoader
{
    public static readonly string[] DataFiles = ["classes", "spells", "auras", "items", "monsters", "loot_tables", "vendors", "rules"];

    public sealed record LoadResult(ContentDb? Content, ValidationReport Report)
    {
        public ContentDb ContentOrThrow => Content ?? throw new ContentValidationException(Report.Errors);
    }

    /// <summary>Valida y carga; nunca lanza por errores de contenido (los devuelve en el informe).</summary>
    public static LoadResult Load(string contentDir)
    {
        var report = new ValidationReport();
        var files = ValidateFiles(contentDir, report);
        if (!report.IsValid || files is null) return new LoadResult(null, report);

        ContentDb db;
        try
        {
            db = Build(files, contentDir);
        }
        catch (JsonException ex)
        {
            report.Error("content", "", $"error de deserialización: {ex.Message}");
            return new LoadResult(null, report);
        }
        return new LoadResult(report.IsValid ? db : null, report);
    }

    public static ContentDb LoadOrThrow(string contentDir) => Load(contentDir).ContentOrThrow;

    /// <summary>Solo rules.json (para la recarga en caliente de HU-003 CA4c): válido contra su schema y sus reglas internas.</summary>
    public static (RulesDb? Rules, ValidationReport Report) LoadRules(string contentDir)
    {
        var report = new ValidationReport();
        var files = ValidateFiles(contentDir, report);
        if (!report.IsValid || files is null) return (null, report);
        var text = File.ReadAllText(Path.Combine(contentDir, "rules.json"));
        var rules = ContentJson.Deserialize<RulesDb>(text) with { Hash = Sha256(text) };
        return (rules, report);
    }

    private static Dictionary<string, JsonDocument>? ValidateFiles(string contentDir, ValidationReport report)
    {
        if (!Directory.Exists(contentDir))
        {
            report.Error("content", "", $"no existe la carpeta '{contentDir}'");
            return null;
        }
        var schemaDir = Path.Combine(contentDir, "schemas");
        var validator = new SchemaValidator();
        foreach (var schemaPath in Directory.GetFiles(schemaDir, "*.schema.json").Order())
        {
            var doc = JsonDocument.Parse(File.ReadAllText(schemaPath));
            var id = doc.RootElement.TryGetProperty("$id", out var idEl) ? idEl.GetString()! : Path.GetFileName(schemaPath);
            validator.Register(id, doc.RootElement);
        }

        var files = new Dictionary<string, JsonDocument>(StringComparer.Ordinal);
        foreach (var name in DataFiles)
        {
            var path = Path.Combine(contentDir, $"{name}.json");
            if (!File.Exists(path)) { report.Error($"{name}.json", "", "archivo no encontrado"); continue; }
            JsonDocument doc;
            try { doc = JsonDocument.Parse(File.ReadAllText(path)); }
            catch (JsonException ex) { report.Error($"{name}.json", "", $"JSON inválido: {ex.Message}"); continue; }
            files[name] = doc;

            var schemaId = doc.RootElement.TryGetProperty("$schema", out var s) ? Path.GetFileName(s.GetString()!) : $"{name}.schema.json";
            if (!File.Exists(Path.Combine(schemaDir, schemaId)))
            {
                report.Error($"{name}.json", "/$schema", $"el schema '{schemaId}' no existe en {schemaDir}");
                continue;
            }
            foreach (var err in validator.Validate(schemaId, doc.RootElement))
            {
                var sep = err.IndexOf(": ", StringComparison.Ordinal);
                report.Error($"{name}.json", sep < 0 ? "" : err[..sep], sep < 0 ? err : err[(sep + 2)..]);
            }
        }
        if (files.Count == DataFiles.Length && report.IsValid)
            CrossRefValidator.Run(files, report);
        return files;
    }

    private static ContentDb Build(Dictionary<string, JsonDocument> files, string contentDir)
    {
        string Text(string name) => File.ReadAllText(Path.Combine(contentDir, $"{name}.json"));
        var rulesText = Text("rules");
        var rules = ContentJson.Deserialize<RulesDb>(rulesText) with { Hash = Sha256(rulesText) };
        var db = new ContentDb(
            ContentJson.Deserialize<ClassesFile>(Text("classes")).Classes,
            ContentJson.Deserialize<SpellsFile>(Text("spells")).Spells,
            ContentJson.Deserialize<AurasFile>(Text("auras")).Auras,
            ContentJson.Deserialize<ItemsFile>(Text("items")).Items,
            ContentJson.Deserialize<MonstersFile>(Text("monsters")).Monsters,
            ContentJson.Deserialize<LootTablesFile>(Text("loot_tables")).LootTables,
            ContentJson.Deserialize<VendorsFile>(Text("vendors")).Vendors,
            rules);
        _ = files; // los avisos de ADR-023 ya los emitió CrossRefValidator con su ruta JSON
        return db;
    }

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal))))[..16];
}
