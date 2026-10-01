using PixelRealms.Content;
using PixelRealms.Game.Core;
using PixelRealms.Protocol.Messages;
using PixelRealms.Server.Net;
using PixelRealms.Server.Net.Handlers;

namespace PixelRealms.Server.Hosting;

/// <summary>
/// Composición del servidor, reutilizable por Program.cs y por los tests de integración (que lo arrancan en un puerto libre).
/// Devuelve null si el contenido es inválido (HU-003 CA5): los errores ya se escribieron en la consola.
/// </summary>
public static class ServerApp
{
    public static WebApplication? Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);

        var contentDir = builder.Configuration["Content:Dir"] ?? FindContentDir();
        var load = ContentLoader.Load(contentDir);
        foreach (var w in load.Report.Warnings) Console.WriteLine($"AVISO  {w}");
        if (load.Content is null)
        {
            foreach (var e in load.Report.Errors) Console.Error.WriteLine($"ERROR  {e}");
            Console.Error.WriteLine($"content/ inválido ({load.Report.Errors.Count} errores): el servidor no arranca.");
            return null;
        }
        var content = new ReloadableContent(load.Content, contentDir);
        builder.Services.AddSingleton(content);
        builder.Services.Configure<NetOptions>(builder.Configuration.GetSection(NetOptions.Section));

        var world = new World();
        var simulation = new Simulation(world, content.Rules, new SeededRng(Environment.TickCount), new TickClock());
        builder.Services.AddSingleton(world);
        builder.Services.AddSingleton(simulation);
        builder.Services.AddSingleton<ConnectionManager>();
        builder.Services.AddSingleton<MessageRouter>();
        builder.Services.AddSingleton<GameLoopService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<GameLoopService>());

        var app = builder.Build();
        var router = app.Services.GetRequiredService<MessageRouter>();
        router.Register(new PingHandler());
        simulation.OnPreTick(router.Drain);

        var net = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<NetOptions>>().Value;
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.Zero });
        app.MapGet("/health", (GameLoopService loop, ConnectionManager cm) =>
        {
            var (_, p99) = loop.Stats.Percentiles();
            return Results.Ok(new { status = "ok", players = cm.Count, tickP99Ms = Math.Round(p99, 2), tick = loop.TicksRun });
        });
        app.Map("/ws", async (HttpContext http, ConnectionManager cm, IHostApplicationLifetime lifetime) =>
        {
            if (!http.WebSockets.IsWebSocketRequest) { http.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
            if (net.RequireTicket && string.IsNullOrEmpty(http.Request.Query["ticket"]))
            {
                http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            using var socket = await http.WebSockets.AcceptWebSocketAsync();
            await cm.HandleAsync(socket, TimeSpan.FromSeconds(net.IdleTimeoutSec), lifetime.ApplicationStopping);
        });
        _ = typeof(Error); // el protocolo se referencia desde aquí para que el registro estático se inicialice al arrancar
        return app;
    }

    public static string FindContentDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "content", "rules.json"))) dir = dir.Parent;
        return dir is null ? "content" : Path.Combine(dir.FullName, "content");
    }
}
