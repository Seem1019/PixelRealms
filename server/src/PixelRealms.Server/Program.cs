using PixelRealms.Content;
using PixelRealms.Game.Core;
using PixelRealms.Server.Hosting;

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
var content = new ReloadableContent(load.Content, contentDir);
builder.Services.AddSingleton(content);

// HU-004: mundo + simulación en un hilo dedicado (los sistemas se registran en orden explícito en cada HU).
var world = new World();
var simulation = new Simulation(world, content.Rules, new SeededRng(Environment.TickCount), new TickClock());
builder.Services.AddSingleton(world);
builder.Services.AddSingleton(simulation);
builder.Services.AddSingleton<GameLoopService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<GameLoopService>());

var app = builder.Build();
app.MapGet("/health", (GameLoopService loop) =>
{
    var (_, p99) = loop.Stats.Percentiles();
    return Results.Ok(new { status = "ok", tick = loop.TicksRun, tickP99Ms = Math.Round(p99, 2) });
});
app.Run();
return 0;

static string FindContentDir()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "content", "rules.json"))) dir = dir.Parent;
    return dir is null ? "content" : Path.Combine(dir.FullName, "content");
}
