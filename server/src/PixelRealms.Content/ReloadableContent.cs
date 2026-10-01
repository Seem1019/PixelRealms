using PixelRealms.Content.Defs;
using PixelRealms.Content.Validation;

namespace PixelRealms.Content;

/// <summary>
/// Contenido activo del servidor con recarga en caliente de rules.json (HU-003 CA4c, `/reload rules`). Si el archivo nuevo
/// es inválido se conserva el anterior y se devuelve el informe con los errores. El intercambio es atómico (una referencia).
/// </summary>
public sealed class ReloadableContent
{
    private volatile ContentDb _current;

    public ReloadableContent(ContentDb initial, string contentDir)
    {
        _current = initial;
        ContentDir = contentDir;
    }

    public string ContentDir { get; }

    public ContentDb Current => _current;

    public IRules Rules => _current.Rules;

    public ValidationReport ReloadRules()
    {
        var (rules, report) = ContentLoader.LoadRules(ContentDir);
        if (rules is null || !report.IsValid) return report;
        var old = _current;
        _current = new ContentDb(old.Classes, old.Spells, old.Auras, old.Items, old.Monsters, old.LootTables, old.Vendors, rules);
        return report;
    }
}
