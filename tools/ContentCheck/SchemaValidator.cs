using System.Text.Json;
using System.Text.RegularExpressions;

namespace PixelRealms.Tools.ContentCheck;

/// <summary>
/// Validador de un subconjunto de JSON Schema draft 2020-12: justo las palabras clave que usan los schemas de
/// content/schemas/ (type, enum, const, required, properties, additionalProperties, items, min/maxItems, uniqueItems,
/// minimum, maximum, exclusiveMinimum, pattern, min/maxLength, allOf, oneOf, if/then/else, $ref local y entre archivos
/// por $id, $defs). HU-003 lo sustituye por JsonSchema.Net; este es el punto de partida sin dependencias.
/// </summary>
public sealed class SchemaValidator
{
    private readonly Dictionary<string, JsonElement> _schemasById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Regex> _regexCache = new(StringComparer.Ordinal);

    public void Register(string id, JsonElement root) => _schemasById[id] = root;

    public IReadOnlyList<string> Validate(string schemaId, JsonElement instance)
    {
        var errors = new List<string>();
        var root = _schemasById[schemaId];
        Validate(root, schemaId, instance, "", errors);
        return errors;
    }

    private void Validate(JsonElement schema, string currentFile, JsonElement inst, string path, List<string> errors)
    {
        if (schema.ValueKind == JsonValueKind.True) return;
        if (schema.ValueKind == JsonValueKind.False) { errors.Add($"{path}: no se permite ningún valor"); return; }
        if (schema.ValueKind != JsonValueKind.Object) return;

        if (schema.TryGetProperty("$ref", out var refEl))
        {
            var (target, file) = Resolve(refEl.GetString()!, currentFile);
            Validate(target, file, inst, path, errors);
            // En 2020-12, $ref convive con otras palabras clave: seguimos evaluando el resto.
        }

        if (schema.TryGetProperty("type", out var typeEl) && !TypeMatches(typeEl, inst))
            errors.Add($"{path}: tipo esperado {Describe(typeEl)}, encontrado {Kind(inst)}");

        if (schema.TryGetProperty("enum", out var enumEl) && !enumEl.EnumerateArray().Any(e => JsonEquals(e, inst)))
            errors.Add($"{path}: valor {inst.GetRawText()} no está en enum [{string.Join(", ", enumEl.EnumerateArray().Select(e => e.GetRawText()))}]");

        if (schema.TryGetProperty("const", out var constEl) && !JsonEquals(constEl, inst))
            errors.Add($"{path}: se esperaba el valor constante {constEl.GetRawText()}");

        if (inst.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out var req))
                foreach (var r in req.EnumerateArray())
                    if (!inst.TryGetProperty(r.GetString()!, out _))
                        errors.Add($"{path}: falta la propiedad obligatoria '{r.GetString()}'");

            var declared = new HashSet<string>(StringComparer.Ordinal);
            if (schema.TryGetProperty("properties", out var props))
                foreach (var p in props.EnumerateObject())
                {
                    declared.Add(p.Name);
                    if (inst.TryGetProperty(p.Name, out var child))
                        Validate(p.Value, currentFile, child, $"{path}/{p.Name}", errors);
                }

            if (schema.TryGetProperty("additionalProperties", out var addl))
                foreach (var ip in inst.EnumerateObject())
                {
                    if (declared.Contains(ip.Name)) continue;
                    if (addl.ValueKind == JsonValueKind.False)
                        errors.Add($"{path}/{ip.Name}: propiedad no permitida");
                    else
                        Validate(addl, currentFile, ip.Value, $"{path}/{ip.Name}", errors);
                }
        }

        if (inst.ValueKind == JsonValueKind.Array)
        {
            var items = inst.EnumerateArray().ToList();
            if (schema.TryGetProperty("items", out var itemsSchema))
                for (var i = 0; i < items.Count; i++)
                    Validate(itemsSchema, currentFile, items[i], $"{path}/{i}", errors);
            if (schema.TryGetProperty("minItems", out var minI) && items.Count < minI.GetInt32())
                errors.Add($"{path}: mínimo {minI.GetInt32()} elementos, hay {items.Count}");
            if (schema.TryGetProperty("maxItems", out var maxI) && items.Count > maxI.GetInt32())
                errors.Add($"{path}: máximo {maxI.GetInt32()} elementos, hay {items.Count}");
            if (schema.TryGetProperty("uniqueItems", out var uniq) && uniq.ValueKind == JsonValueKind.True)
                for (var i = 0; i < items.Count; i++)
                    for (var j = i + 1; j < items.Count; j++)
                        if (JsonEquals(items[i], items[j]))
                            errors.Add($"{path}: elementos repetidos en {i} y {j} ({items[i].GetRawText()})");
        }

        if (inst.ValueKind == JsonValueKind.Number)
        {
            var v = inst.GetDouble();
            if (schema.TryGetProperty("minimum", out var min) && v < min.GetDouble())
                errors.Add($"{path}: {v} es menor que el mínimo {min.GetDouble()}");
            if (schema.TryGetProperty("maximum", out var max) && v > max.GetDouble())
                errors.Add($"{path}: {v} es mayor que el máximo {max.GetDouble()}");
            if (schema.TryGetProperty("exclusiveMinimum", out var exMin) && v <= exMin.GetDouble())
                errors.Add($"{path}: {v} debe ser mayor que {exMin.GetDouble()}");
        }

        if (inst.ValueKind == JsonValueKind.String)
        {
            var s = inst.GetString()!;
            if (schema.TryGetProperty("minLength", out var minL) && s.Length < minL.GetInt32())
                errors.Add($"{path}: longitud mínima {minL.GetInt32()}");
            if (schema.TryGetProperty("maxLength", out var maxL) && s.Length > maxL.GetInt32())
                errors.Add($"{path}: longitud máxima {maxL.GetInt32()}");
            if (schema.TryGetProperty("pattern", out var pat) && !GetRegex(pat.GetString()!).IsMatch(s))
                errors.Add($"{path}: '{s}' no cumple el patrón {pat.GetString()}");
        }

        if (schema.TryGetProperty("allOf", out var allOf))
            foreach (var sub in allOf.EnumerateArray())
                Validate(sub, currentFile, inst, path, errors);

        if (schema.TryGetProperty("oneOf", out var oneOf))
        {
            var matches = oneOf.EnumerateArray().Count(sub => IsValid(sub, currentFile, inst));
            if (matches != 1)
                errors.Add($"{path}: debe cumplir exactamente una de las {oneOf.GetArrayLength()} alternativas (cumple {matches})");
        }

        if (schema.TryGetProperty("if", out var ifSchema))
        {
            var branch = IsValid(ifSchema, currentFile, inst) ? "then" : "else";
            if (schema.TryGetProperty(branch, out var branchSchema))
                Validate(branchSchema, currentFile, inst, path, errors);
        }
    }

    private bool IsValid(JsonElement schema, string file, JsonElement inst)
    {
        var errs = new List<string>();
        Validate(schema, file, inst, "", errs);
        return errs.Count == 0;
    }

    private (JsonElement Schema, string File) Resolve(string reference, string currentFile)
    {
        var hash = reference.IndexOf('#');
        var fileId = hash < 0 ? reference : reference[..hash];
        var pointer = hash < 0 ? "" : reference[(hash + 1)..];
        if (fileId.Length == 0) fileId = currentFile;
        if (!_schemasById.TryGetValue(fileId, out var root))
            throw new InvalidOperationException($"$ref a un schema no registrado: '{fileId}' (desde {currentFile})");
        var node = root;
        foreach (var seg in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var key = seg.Replace("~1", "/").Replace("~0", "~");
            node = node.ValueKind == JsonValueKind.Array
                ? node[int.Parse(key)]
                : node.TryGetProperty(key, out var child) ? child
                : throw new InvalidOperationException($"$ref no resuelto: '{reference}' (segmento '{key}')");
        }
        return (node, fileId);
    }

    private Regex GetRegex(string pattern)
    {
        if (!_regexCache.TryGetValue(pattern, out var rx))
            _regexCache[pattern] = rx = new Regex(pattern, RegexOptions.CultureInvariant);
        return rx;
    }

    private static bool TypeMatches(JsonElement typeEl, JsonElement inst)
    {
        if (typeEl.ValueKind == JsonValueKind.Array)
            return typeEl.EnumerateArray().Any(t => TypeMatches(t, inst));
        var k = inst.ValueKind;
        return typeEl.GetString() switch
        {
            "object" => k == JsonValueKind.Object,
            "array" => k == JsonValueKind.Array,
            "string" => k == JsonValueKind.String,
            "boolean" => k is JsonValueKind.True or JsonValueKind.False,
            "null" => k == JsonValueKind.Null,
            "number" => k == JsonValueKind.Number,
            "integer" => k == JsonValueKind.Number && inst.GetDouble() == Math.Floor(inst.GetDouble()),
            _ => true,
        };
    }

    private static string Describe(JsonElement typeEl) =>
        typeEl.ValueKind == JsonValueKind.Array
            ? string.Join("|", typeEl.EnumerateArray().Select(t => t.GetString()))
            : typeEl.GetString()!;

    private static string Kind(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Number => "number",
        _ => e.ValueKind.ToString().ToLowerInvariant(),
    };

    public static bool JsonEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        return a.ValueKind switch
        {
            JsonValueKind.Number => a.GetDouble() == b.GetDouble(),
            JsonValueKind.String => a.GetString() == b.GetString(),
            JsonValueKind.Array => a.GetArrayLength() == b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(p => JsonEquals(p.First, p.Second)),
            JsonValueKind.Object => a.EnumerateObject().Count() == b.EnumerateObject().Count()
                                    && a.EnumerateObject().All(p => b.TryGetProperty(p.Name, out var o) && JsonEquals(p.Value, o)),
            _ => true,
        };
    }
}
