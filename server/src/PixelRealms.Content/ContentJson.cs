using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixelRealms.Content;

/// <summary>Opciones de deserialización de content/*.json: camelCase, enums en snake_case, sin miembros desconocidos.</summary>
public static class ContentJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
    };

    /// <summary>Nombre de un enum tal como aparece en el JSON (snake_case minúscula): GroundAoeAll → "ground_aoe_all". Se calcula
    /// una vez por valor: lo piden el tick y el dispatcher en cada golpe (HU-088 CA1).</summary>
    public static string EnumName<T>(T value) where T : struct, Enum =>
        EnumNames<T>.ByValue.TryGetValue(value, out var name) ? name : JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    private static class EnumNames<T> where T : struct, Enum
    {
        public static readonly Dictionary<T, string> ByValue = Enum.GetValues<T>().Distinct()
            .ToDictionary(v => v, v => JsonNamingPolicy.SnakeCaseLower.ConvertName(v.ToString()));
    }

    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new InvalidDataException($"JSON vacío al deserializar {typeof(T).Name}");
}
