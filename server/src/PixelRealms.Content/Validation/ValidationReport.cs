namespace PixelRealms.Content.Validation;

/// <summary>Errores (fallan la carga) y avisos (no fallan) de una validación de content/.</summary>
public sealed class ValidationReport
{
    private readonly List<string> _errors = new();
    private readonly List<string> _warnings = new();

    public IReadOnlyList<string> Errors => _errors;
    public IReadOnlyList<string> Warnings => _warnings;
    public bool IsValid => _errors.Count == 0;

    public void Error(string file, string path, string message) => _errors.Add($"{file}{path}: {message}");

    public void Warn(string file, string path, string message) => _warnings.Add($"{file}{path}: {message}");
}
