using PixelRealms.Content;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Interest;
using PixelRealms.Game.Portals;
using PixelRealms.Game.Movement;
using PixelRealms.Persistence;
using PixelRealms.Protocol.Messages;
using PixelRealms.Server.Api;
using PixelRealms.Server.Auth;
using PixelRealms.Server.Net;
using PixelRealms.Server.Players;
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
        builder.Configuration.AddInMemoryCollection(DotEnv.Load()); // .env de la raíz (POSTGRES_*, JWT_SIGNING_KEY)
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
        builder.Services.Configure<PersistenceOptions>(builder.Configuration.GetSection(PersistenceOptions.Section));
        builder.Services.AddPersistence(builder.Configuration); // HU-002: Postgres (EF Core) o InMemory según Persistence:Provider

        // HU-010/011/014: auth REST, JWT de 15 min y tickets de 30 s.
        var signingKey = builder.Configuration["JWT_SIGNING_KEY"];
        if (string.IsNullOrEmpty(signingKey) || signingKey.Length < 32)
        {
            if (!builder.Environment.IsDevelopment()) throw new InvalidOperationException("Falta JWT_SIGNING_KEY (>= 32 caracteres) en .env o user-secrets");
            signingKey = JwtService.GenerateRandomKey();
            Console.WriteLine("AVISO  JWT_SIGNING_KEY no configurada: se usa una clave aleatoria por proceso (solo Development)");
        }
        builder.Services.AddSingleton(new JwtService(signingKey, TimeSpan.FromMinutes(15)));
        builder.Services.AddSingleton(new TicketService(TimeSpan.FromSeconds(30)));
        builder.Services.AddSingleton<Passwords>();
        builder.Services.AddSingleton<CharacterFactory>();
        AuthEndpoints.AddRateLimiting(builder.Services);

        var world = new World();
        // HU-020 CA1/CA2: un MapData por .tmj y una MapInstance de cada uno; un mapa inválido impide arrancar.
        var mapsDir = builder.Configuration["Maps:Dir"] ?? Path.Combine(Path.GetDirectoryName(contentDir.TrimEnd(Path.DirectorySeparatorChar))!, "maps");
        try
        {
            var db = content.Current;
            foreach (var map in Game.Map.TiledMapLoader.LoadAll(mapsDir, new Game.Map.TiledMapLoader.ContentCheck(id => db.TryGetMonster(id, out _), db.HasVendor)))
            {
                world.RegisterMap(map);
                world.CreateInstance(map.MapId);
                Console.WriteLine($"Mapa {map.MapId}: {map.Width}×{map.Height}, {map.Spawns.Count} spawns ({map.Spawns.Sum(s => s.Count)} monstruos), {map.Graveyards.Count} puntos seguros, {map.Portals.Count} portales");
            }
        }
        catch (Game.Map.MapLoadException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("maps/ inválido: el servidor no arranca.");
            return null;
        }
        var rng = new SeededRng(Environment.TickCount);
        var simulation = new Simulation(world, content.Rules, rng, new TickClock());
        var movementSystem = new MovementSystem();
        var interestSystem = new InterestSystem();
        var combat = CombatModule.Create(() => content.Current, world, movementSystem, interestSystem);
        // HU-031 CA1: monstruos de cada spawn al arrancar.
        foreach (var instance in world.Instances)
        {
            var created = combat.Spawns.Populate(instance, rng);
            foreach (var npcDef in instance.Data.Npcs)
                instance.Add(new Game.Entities.Npc(world.EntityIds.Next(), npcDef) { Position = npcDef.Position, Hp = 1, MaxHp = 1 });
            Console.WriteLine($"Instancia {instance.MapId}: {created} monstruos y {instance.Data.Npcs.Count} NPC creados");
        }
        builder.Services.AddSingleton(world);
        builder.Services.AddSingleton(simulation);
        builder.Services.AddSingleton(combat);
        builder.Services.AddSingleton(combat.Loot);
        builder.Services.AddSingleton(combat.Parties);
        builder.Services.AddSingleton(movementSystem);
        builder.Services.AddSingleton(interestSystem);
        builder.Services.AddSingleton<CombatHandlerDeps>();
        builder.Services.AddSingleton<ConnectionManager>();
        builder.Services.AddSingleton<MessageRouter>();
        builder.Services.AddSingleton<PlayerRegistry>();
        builder.Services.AddSingleton<PlayerMapper>();
        builder.Services.AddSingleton<IHelloGate, HelloGate>();
        builder.Services.AddSingleton<WorldSession>();
        builder.Services.AddSingleton<SaveService>();
        builder.Services.AddSingleton<PortalSystem>();
        builder.Services.AddSingleton<MapTransferService>();
        builder.Services.AddSingleton<SnapshotBuilder>();
        builder.Services.AddSingleton<EventDispatcher>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<SaveService>());
        builder.Services.AddSingleton<GameLoopService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<GameLoopService>());

        var app = builder.Build();
        // HU-002 CA3: migraciones automáticas al arrancar (Development y producción, con log de cuáles).
        PersistenceModule.MigrateAsync(app.Services, app.Configuration).GetAwaiter().GetResult();
        var router = app.Services.GetRequiredService<MessageRouter>();
        var worldSession = app.Services.GetRequiredService<WorldSession>();
        app.Services.GetRequiredService<PlayerMapper>().MapIdOf = worldSession.MapIdOf;
        worldSession.Combat = app.Services.GetRequiredService<CombatModule>();
        router.AddObserver(worldSession);
        router.Register(new PingHandler());
        router.Register(new MoveInputHandler(app.Services.GetRequiredService<ILogger<MoveInputHandler>>()));
        router.Register(new UsePortalHandler());
        var combatDeps = app.Services.GetRequiredService<CombatHandlerDeps>();
        router.Register(new SelectTargetHandler());
        router.Register(new CastSpellHandler(combatDeps));
        router.Register(new CancelCastHandler(combatDeps));
        router.Register(new AutoAttackHandler(combatDeps));
        router.Register(new RespawnHandler(combatDeps));
        router.Register(new SetHotbarHandler(content));
        router.Register(new InventoryMoveHandler(combatDeps, content));
        router.Register(new UseItemHandler(combatDeps));
        router.Register(new DestroyItemHandler(combatDeps, content));
        router.Register(new LootOpenHandler(combatDeps));
        router.Register(new LootTakeHandler(combatDeps));
        router.Register(new LootTakeAllHandler(combatDeps));
        router.Register(new VendorOpenHandler(combatDeps, content));
        router.Register(new VendorBuyHandler(combatDeps));
        router.Register(new VendorSellHandler(combatDeps));
        var registry0 = app.Services.GetRequiredService<PlayerRegistry>();
        router.Register(new ChatSendHandler(combatDeps, registry0, worldSession));
        router.Register(new PartyInviteHandler(combatDeps, registry0));
        router.Register(new PartyRespondHandler(combatDeps, registry0));
        router.Register(new PartyLeaveHandler(combatDeps));
        router.Register(new PartyKickHandler(combatDeps));
        router.Register(new DuelRequestHandler(combatDeps, registry0));
        router.Register(new DuelRespondHandler(combatDeps));
        router.Register(new DuelForfeitHandler(combatDeps));
        router.Register(new TradeRequestHandler(combatDeps, registry0));
        router.Register(new TradeRespondHandler(combatDeps));
        router.Register(new TradeOfferHandler(combatDeps));
        router.Register(new TradeConfirmHandler(combatDeps));
        router.Register(new TradeCancelHandler(combatDeps));
        router.Register(new ChangeClassHandler(combatDeps, worldSession));
        router.Register(new AdminCommandHandler(combatDeps, registry0, app.Services.GetRequiredService<MapTransferService>(), content, app.Services.GetRequiredService<ILogger<AdminCommandHandler>>()));
        // Orden del tick (docs/architecture.md §3): entrada → movimiento → … → interés → salida.
        simulation.OnPreTick(router.Drain);
        app.Services.GetRequiredService<CombatModule>().Register(simulation, app.Services.GetRequiredService<PortalSystem>());
        simulation
            .OnPostTick(app.Services.GetRequiredService<MapTransferService>().OnPostTick)
            .OnPostTick(worldSession.SweepLinkdead)
            .OnPostTick(app.Services.GetRequiredService<EventDispatcher>().OnPostTick)
            .OnPostTick(app.Services.GetRequiredService<SnapshotBuilder>().OnPostTick);
        // Al apagar (Ctrl+C): guardar a todos los jugadores conectados antes de salir (HU-026 CA2), en el hilo del tick.
        app.Services.GetRequiredService<GameLoopService>().OnStopping = () =>
        {
            var registry = app.Services.GetRequiredService<PlayerRegistry>();
            foreach (var p in registry.All.ToList()) worldSession.Leave(p, "shutdown");
        };

        var net = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<NetOptions>>().Value;
        app.UseRateLimiter();
        AuthEndpoints.Map(app);
        CharacterEndpoints.Map(app);
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
            var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (!cm.TryReserveIp(ip))
            {
                http.Response.StatusCode = StatusCodes.Status429TooManyRequests; // HU-071 CA3
                return;
            }
            using var socket = await http.WebSockets.AcceptWebSocketAsync();
            await cm.HandleAsync(socket, TimeSpan.FromSeconds(net.IdleTimeoutSec), lifetime.ApplicationStopping, ip);
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
