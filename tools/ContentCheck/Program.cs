using System.Text.Json;
using PixelRealms.Tools.ContentCheck;

// ContentCheck — validador base de content/ (punto de partida de HU-003, que lo sustituye por
// server/tools/ContentValidator con JsonSchema.Net y lo integra en PixelRealms.Content).
//   dotnet run --project tools/ContentCheck -- content/
// Salida: errores (código 1) y avisos (no fallan). Sin paquetes NuGet.

var contentDir = args.Length > 0 ? args[0] : "content";
if (!Directory.Exists(contentDir))
{
    Console.Error.WriteLine($"No existe la carpeta de contenido '{contentDir}'. Uso: ContentCheck <ruta a content/>");
    return 2;
}

var report = new Report();
var files = new Dictionary<string, JsonDocument>(StringComparer.Ordinal);
var validator = new SchemaValidator();

// 1. Schemas: se registran por $id para resolver $ref entre archivos.
var schemaDir = Path.Combine(contentDir, "schemas");
foreach (var schemaPath in Directory.GetFiles(schemaDir, "*.schema.json").Order())
{
    var doc = JsonDocument.Parse(File.ReadAllText(schemaPath));
    var id = doc.RootElement.TryGetProperty("$id", out var idEl) ? idEl.GetString()! : Path.GetFileName(schemaPath);
    validator.Register(id, doc.RootElement);
}

// 2. Datos: cada archivo se valida con el schema que declara en "$schema".
var dataFiles = new[] { "classes", "spells", "auras", "items", "monsters", "loot_tables", "vendors", "rules" };
foreach (var name in dataFiles)
{
    var path = Path.Combine(contentDir, $"{name}.json");
    if (!File.Exists(path)) { report.Error($"{name}.json", "", "archivo no encontrado"); continue; }
    JsonDocument doc;
    try { doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow }); }
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

// 3. Referencias cruzadas (lista de la skill game-content §Procedimiento y HU-003 CA 3/4/4b/4d).
if (files.Count == dataFiles.Length)
    CrossRefChecks.Run(files, report);

// 4. Resumen.
Console.WriteLine();
if (files.Count == dataFiles.Length)
{
    static int Count(JsonDocument d, string key) => d.RootElement.TryGetProperty(key, out var a) ? a.GetArrayLength() : 0;
    Console.WriteLine($"{Count(files["classes"], "classes")} clases, {Count(files["spells"], "spells")} hechizos, {Count(files["auras"], "auras")} auras, " +
                      $"{Count(files["items"], "items")} items, {Count(files["monsters"], "monsters")} monstruos, {Count(files["loot_tables"], "lootTables")} tablas, " +
                      $"{Count(files["vendors"], "vendors")} vendedor(es), rules {(report.Errors.Any(e => e.StartsWith("rules.json", StringComparison.Ordinal)) ? "KO" : "OK")}");
}
Console.WriteLine($"{report.Errors.Count} error(es), {report.Warnings.Count} aviso(s).");
return report.Errors.Count == 0 ? 0 : 1;

public sealed class Report
{
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();

    public void Error(string file, string path, string message)
    {
        var line = $"{file}{path}: {message}";
        Errors.Add(line);
        Console.WriteLine($"ERROR  {line}");
    }

    public void Warn(string file, string path, string message)
    {
        var line = $"{file}{path}: {message}";
        Warnings.Add(line);
        Console.WriteLine($"AVISO  {line}");
    }
}
