using System.Text.RegularExpressions;

namespace PixelRealms.Server.Api;

/// <summary>Reglas de validación de la API (HU-010 CA3, HU-012 CA2/CA3). Son reglas técnicas, no de balance.</summary>
public static partial class Validation
{
    public const int UsernameMin = 3;
    public const int UsernameMax = 20;
    public const int PasswordMin = 8;
    public const int CharacterNameMin = 3;
    public const int CharacterNameMax = 16;
    public const int MaxCharactersPerAccount = 4;

    public static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
        { "admin", "gm", "system", "server", "pixelrealms", "moderador", "mod", "soporte", "marta", "grask" };

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex UsernameRegex();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]+$")]
    private static partial Regex CharacterNameRegex();

    public static FieldError? Username(string? username)
    {
        if (string.IsNullOrEmpty(username) || username.Length < UsernameMin || username.Length > UsernameMax)
            return new FieldError("username", "invalid_length", $"El usuario debe tener entre {UsernameMin} y {UsernameMax} caracteres");
        if (!UsernameRegex().IsMatch(username))
            return new FieldError("username", "invalid_chars", "Solo letras, números y guion bajo");
        return null;
    }

    public static FieldError? Password(string? password) =>
        string.IsNullOrEmpty(password) || password.Length < PasswordMin
            ? new FieldError("password", "too_short", $"La contraseña debe tener al menos {PasswordMin} caracteres")
            : null;

    public static FieldError? CharacterName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length < CharacterNameMin || name.Length > CharacterNameMax)
            return new FieldError("name", "invalid_length", $"El nombre debe tener entre {CharacterNameMin} y {CharacterNameMax} caracteres");
        if (!CharacterNameRegex().IsMatch(name))
            return new FieldError("name", "invalid_chars", "Empieza por letra y solo letras o números");
        if (ReservedNames.Contains(name))
            return new FieldError("name", "reserved", "Ese nombre está reservado");
        return null;
    }
}
