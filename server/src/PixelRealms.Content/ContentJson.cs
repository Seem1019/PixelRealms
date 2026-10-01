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

    /// <summary>Nombre de un enum tal como aparece en el JSON (snake_case minúscula): GroundAoeAll → "ground_aoe_all".</summary>
    public static string EnumName<T>(T value) where T : struct, Enum => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new InvalidDataException($"JSON vacío al deserializar {typeof(T).Name}");
}
