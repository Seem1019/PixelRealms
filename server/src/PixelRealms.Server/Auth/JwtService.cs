using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PixelRealms.Server.Auth;

/// <summary>Claims del token de sesión (15 min). `Admin` sale de accounts.is_admin. `AuthTime` = cuándo se escribió la contraseña:
/// se conserva al renovar (HU-015) para poner un tope absoluto a la sesión.</summary>
public sealed record JwtClaims(Guid AccountId, string Username, bool Admin, DateTimeOffset ExpiresAt, DateTimeOffset AuthTime);

/// <summary>
/// JWT HS256 hecho a mano con la BCL (HU-011): header.payload.firma en base64url. Decisión provisional: evita el paquete
/// Microsoft.AspNetCore.Authentication.JwtBearer (NuGet bloqueado en la sesión). La clave viene de JWT_SIGNING_KEY (.env o
/// user-secrets); en Development, si falta, se genera una aleatoria por proceso con un aviso.
/// </summary>
public sealed class JwtService
{
    private readonly byte[] _key;

    public JwtService(string signingKey, TimeSpan lifetime)
    {
        if (signingKey.Length < 32) throw new ArgumentException("JWT_SIGNING_KEY debe tener al menos 32 caracteres", nameof(signingKey));
        _key = Encoding.UTF8.GetBytes(signingKey);
        Lifetime = lifetime;
    }

    public TimeSpan Lifetime { get; }

    public static string GenerateRandomKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    /// <param name="authTime">Login original al renovar (`/api/auth/refresh`); null = este login.</param>
    public (string Token, DateTimeOffset ExpiresAt) Issue(Guid accountId, string username, bool admin, DateTimeOffset now, DateTimeOffset? authTime = null)
    {
        var exp = now + Lifetime;
        var header = B64(Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}"""));
        var payload = B64(JsonSerializer.SerializeToUtf8Bytes(
            new JwtPayload(accountId.ToString(), username, admin, now.ToUnixTimeSeconds(), exp.ToUnixTimeSeconds(), (authTime ?? now).ToUnixTimeSeconds()), AuthJsonContext.Default.JwtPayload));
        var signature = B64(HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes($"{header}.{payload}")));
        return ($"{header}.{payload}.{signature}", exp);
    }

    /// <summary>Null si la firma no cuadra, el formato es inválido o el token expiró.</summary>
    public JwtClaims? Validate(string token, DateTimeOffset now)
    {
        var parts = token.Split('.');
        if (parts.Length != 3) return null;
        var expected = B64(HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}")));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[2]))) return null;
        try
        {
            using var doc = JsonDocument.Parse(UnB64(parts[1]));
            var root = doc.RootElement;
            var exp = DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("exp").GetInt64());
            if (exp <= now) return null;
            var sub = Guid.Parse(root.GetProperty("sub").GetString()!);
            var name = root.GetProperty("name").GetString() ?? "";
            var admin = root.TryGetProperty("admin", out var a) && a.ValueKind == JsonValueKind.True;
            var authTime = DateTimeOffset.FromUnixTimeSeconds(root.TryGetProperty("auth_time", out var at) ? at.GetInt64() : root.GetProperty("iat").GetInt64());
            return new JwtClaims(sub, name, admin, exp, authTime);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] UnB64(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }
}

internal sealed record JwtPayload(
    [property: System.Text.Json.Serialization.JsonPropertyName("sub")] string Sub,
    [property: System.Text.Json.Serialization.JsonPropertyName("name")] string Name,
    [property: System.Text.Json.Serialization.JsonPropertyName("admin")] bool Admin,
    [property: System.Text.Json.Serialization.JsonPropertyName("iat")] long Iat,
    [property: System.Text.Json.Serialization.JsonPropertyName("exp")] long Exp,
    [property: System.Text.Json.Serialization.JsonPropertyName("auth_time")] long AuthTime);

[System.Text.Json.Serialization.JsonSerializable(typeof(JwtPayload))]
internal sealed partial class AuthJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
