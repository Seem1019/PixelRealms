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
        CheckUpgradedSpells(db, report);
        return new LoadResult(report.IsValid ? db : null, report);
    }

    /// <summary>Área apuntada de daño sin casteo que no es un cono cuerpo a cuerpo (ADR-027 D4).</summary>
    private static bool Undodgeable(SpellDef s, CombatRules combat) =>
        s.Targeting.IsGround() && s.IsInstant && s.Effects.Any(e => e.Type == EffectType.Damage) && !s.Effects.Any(e => e.Type == EffectType.Leap)
        && !(s.Shape == Shape.Cone && s.AoeRadius <= combat.InstantConeMaxRadiusTiles);

    /// <summary>
    /// HU-104: el hechizo con cada mejora aplicada sigue siendo válido (nada negativo, al menos un objetivo, auras con duración) y, si
    /// la mejora lo deja instantáneo, respeta `minInstantSpellCooldownMs` como cualquier hechizo de clase. Un aura beneficiosa que
    /// añade una mejora no dura más que la recarga: si no, se lanzaría con una mejora, se cambiaría gratis y se tendrían las dos.
    /// </summary>
    internal static void CheckUpgradedSpells(ContentDb db, ValidationReport report)
    {
        var minInstantCd = db.Rules.Combat.MinInstantSpellCooldownMs;
        foreach (var (key, s) in db.UpgradedSpells)
        {
            if (Undodgeable(s, db.Rules.Combat) && !Undodgeable(db.Spell(s.Id), db.Rules.Combat))
                report.Error("spells.json", "", $"{key}: la mejora deja un área de daño sin casteo que no se puede esquivar (ADR-015, ADR-027 D4)");
            if (s.CastMs < 0 || s.CooldownMs < 0 || s.Cost is { Amount: < 0 } || s.Range < 0 || s.AoeRadius < 0 || s.AoeAngleDeg < 0
                || s.AoeAngleDeg > 180 || s.AoeLength < 0 || s.AoeWidth < 0 || s.MaxTargets < 1)
                report.Error("spells.json", "", $"{key}: la mejora deja un valor fuera de rango (negativo, cono de más de 180° o sin objetivos)");
            if (s.IsInstant && s.CooldownMs < minInstantCd)
                report.Error("spells.json", "", $"{key}: con la mejora queda instantáneo con cooldown {s.CooldownMs} ms < rules.combat.minInstantSpellCooldownMs ({minInstantCd})");
            foreach (var e in s.Effects)
                if (e.AuraOverride is { DurationMs: <= 0 } a)
                    report.Error("spells.json", "", $"{key}: la mejora deja el aura '{a.Id}' sin duración");
            var baseCount = db.Spell(s.Id).Effects.Count;
            for (var i = baseCount; i < s.Effects.Count; i++)
                if (s.Effects[i] is { Type: EffectType.ApplyAura, AuraId: { } added } && db.Aura(added) is { IsDebuff: false } buff && buff.DurationMs > s.CooldownMs)
                    report.Error("spells.json", "", $"{key}: el aura beneficiosa '{added}' que añade la mejora dura {buff.DurationMs} ms, más que la recarga ({s.CooldownMs} ms): se acumularían las dos mejoras");
        }
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
