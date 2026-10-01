using Microsoft.AspNetCore.Identity;

namespace PixelRealms.Server.Auth;

/// <summary>PasswordHasher de ASP.NET Core Identity (PBKDF2) sin el resto de Identity (HU-010 notas técnicas).</summary>
public sealed class Passwords
{
    private readonly PasswordHasher<object> _hasher = new();
    private static readonly object User = new();

    public string Hash(string password) => _hasher.HashPassword(User, password);

    public bool Verify(string hash, string password) => _hasher.VerifyHashedPassword(User, hash, password) is not PasswordVerificationResult.Failed;
}
