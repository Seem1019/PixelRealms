using PixelRealms.Content;

var builder = WebApplication.CreateBuilder(args);

// HU-003 CA5: el contenido se valida al arrancar; si es inválido, el servidor no arranca y muestra los mismos errores.
var contentDir = builder.Configuration["Content:Dir"] ?? FindContentDir();
var load = ContentLoader.Load(contentDir);
foreach (var w in load.Report.Warnings) Console.WriteLine($"AVISO  {w}");
if (load.Content is null)
{
    foreach (var e in load.Report.Errors) Console.Error.WriteLine($"ERROR  {e}");
    Console.Error.WriteLine($"content/ inválido ({load.Report.Errors.Count} errores): el servidor no arranca.");
    return 1;
}
builder.Services.AddSingleton(new ReloadableContent(load.Content, contentDir));

var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();
return 0;

static string FindContentDir()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "content", "rules.json"))) dir = dir.Parent;
    return dir is null ? "content" : Path.Combine(dir.FullName, "content");
}
