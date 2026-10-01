using PixelRealms.Content;

// ContentValidator (HU-003): `dotnet run --project server/tools/ContentValidator -- content/`
// Valida schemas + referencias cruzadas y carga el ContentDb. Código 0 si no hay errores (los avisos no fallan).
var contentDir = args.Length > 0 ? args[0] : "content";
var result = ContentLoader.Load(contentDir);

foreach (var w in result.Report.Warnings) Console.WriteLine($"AVISO  {w}");
foreach (var e in result.Report.Errors) Console.WriteLine($"ERROR  {e}");

if (result.Content is { } db)
{
    Console.WriteLine($"{db.Classes.Count} clases, {db.Spells.Count} hechizos, {db.Auras.Count} auras, {db.Items.Count} items, " +
                      $"{db.Monsters.Count} monstruos, {db.LootTables.Count} tablas, {db.Vendors.Count} vendedor, rules OK (hash {db.Rules.Hash})");
    if (db.UnavailableSpells.Count > 0)
        Console.WriteLine($"{db.UnavailableSpells.Count} hechizo(s) no disponibles (ADR-023): {string.Join(", ", db.UnavailableSpells.Keys)}");
}
Console.WriteLine($"{result.Report.Errors.Count} error(es), {result.Report.Warnings.Count} aviso(s).");
return result.Report.IsValid ? 0 : 1;
