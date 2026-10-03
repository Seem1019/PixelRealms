---
name: dotnet-server
description: Convenciones, arquitectura y comandos del servidor .NET 10 (ASP.NET Core, game loop de 20 Hz, WebSocket, EF Core/PostgreSQL, xUnit). Úsala al crear o modificar cualquier archivo en server/.
---

# Servidor .NET — guía de trabajo

## Stack y paquetes (fijar versiones en `Directory.Packages.props`, Central Package Management)
- .NET 10 SDK, `LangVersion latest`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`,
  `<ImplicitUsings>enable</ImplicitUsings>`, `InvariantGlobalization=true`.
- ASP.NET Core minimal APIs, `System.Text.Json` con **source generation** (`ProtocolJsonContext`).
- `Npgsql.EntityFrameworkCore.PostgreSQL`, `EFCore.NamingConventions`.
- Logs con `Microsoft.Extensions.Logging` (consola; `AddJsonConsole` en Producción). Schemas de contenido con el validador
  propio `PixelRealms.Content/Validation/SchemaValidator.cs` (subconjunto de JSON Schema 2020-12, sin paquete).
- Tests: `xunit.v3`, `Shouldly`, `Testcontainers.PostgreSql` (+ `SSH.NET` fijado por la advertencia NU1903). Sin
  `Mvc.Testing`: la integración arranca el servidor real (`TestServer`, ver §Tests al final).

## Estructura
```
server/
  PixelRealms.sln  Directory.Build.props  Directory.Packages.props  Dockerfile   (.editorconfig en la raíz del repo)
  src/PixelRealms.Protocol/    Messages/{ClientMessages.cs, ServerMessages.cs}, MessageRegistry.cs, ProtocolJsonContext.cs
  src/PixelRealms.Content/     Defs/*.cs (records), ContentLoader.cs, ContentDb.cs, ReloadableContent.cs,
                               Validation/ (SchemaValidator.cs, CrossRefValidator.cs, EngineCapabilities.cs)
  src/PixelRealms.Game/
    Core/        World.cs, Simulation.cs, EntityId.cs, IGameClock.cs, IRng.cs, GameEvents.cs, Vec2.cs
    Entities/    Actor.cs, Player.cs, Monster.cs, Npc.cs
    Map/         CollisionGrid.cs, TiledMapLoader.cs, MapData.cs, MapInstance.cs
    Movement/    MovementSystem.cs, MovementStep.cs (algoritmo compartido con cliente)
    Combat/      CombatCalculator.cs, CastSystem.cs, EffectResolver.cs, AuraSystem.cs, ThreatTable.cs, LineOfSight.cs,
                 ResourceSystem.cs, DeathSystem.cs
    Ai/          MonsterAiSystem.cs, MonsterBrain.cs, Pathfinder.cs (A*), SpawnSystem.cs
    Items/       ItemInstance.cs (+ Inventory, Equipment), InventoryOps.cs, LootSystem.cs (+ LootBag),
                 ItemUseService.cs (+ VendorService)
    Progression/ XpCurve.cs, ProgressionSystem.cs, StatCalculator.cs
    Social/      PartyService.cs, ChatService.cs, TradeService.cs, PvpService.cs
    Interest/    InterestSystem.cs (grid AOI)
  src/PixelRealms.Persistence/ Ef/GameDbContext.cs, Entities/Entities.cs, Repositories/*.cs, Migrations/
  src/PixelRealms.Server/
    Program.cs, Auth/ (JWT, tickets), Api/ (endpoints REST), Net/ (WebSocketSession, ConnectionManager,
    MessageRouter, Handlers/*.cs, MessageRateLimiter.cs, SnapshotBuilder.cs, EventDispatcher.cs),
    Hosting/ (GameLoopService, SaveService), Players/ (WorldSession, PlayerMapper, MapTransferService)
  tools/ContentValidator/  tools/LoadBot/
  tests/PixelRealms.Game.Tests  PixelRealms.Protocol.Tests  PixelRealms.Server.Tests  PixelRealms.Persistence.Tests
```

## Reglas del game loop
- `GameLoopService` crea un `Thread` dedicado. Bucle con `Stopwatch`, acumulador y `Thread.Sleep` fino
  (o `SpinWait` los últimos 2 ms). Tick = 50 ms. Métrica `tick_ms` (p50/p99) logueada cada 30 s.
- Entrada: `Channel<InboundMessage>` (`BoundedChannelOptions(10_000){ FullMode = DropWrite }`); si se llena,
  `WebSocketSession` loguea `warn` y descarta el mensaje (no desconecta). El tick saca como mucho 500 por tick.
- Salida: cada `WebSocketSession` tiene su `Channel<ReadOnlyMemory<byte>>` (bounded 256); si se llena el cliente
  es lento → desconectar. El tick **serializa** y encola; la tarea de envío solo escribe al socket.
- Handlers: `interface IMessageHandler<T> { void Handle(T msg, HandlerContext ctx); }` ejecutados en el tick (el jugador
  es `ctx.Player`, el tick `ctx.Tick`). Agrupados por dominio en `Net/Handlers/*.cs` (`CombatHandlers.cs`,
  `InventoryHandlers.cs`, `SocialHandlers.cs`…) y registrados con `router.Register(...)` en `Hosting/ServerApp.cs`.
  Validan y **emiten errores** con `ctx.SendError(ErrorCodes.OnCooldown, msg.ReqId)` (firma `SendError(code, reqId, message)`).
- Eventos: los sistemas agregan `IGameEvent` a `ctx.Events`; al final del tick `EventDispatcher` los traduce a
  mensajes y los envía a los observadores (AOI).
- Nada de `async` dentro de `PixelRealms.Game`. Nada de `DateTime.UtcNow` (usar `IGameClock.NowMs`).

## Persistencia
- `SaveService : BackgroundService` consume `Channel<CharacterSaveDto>`; cada DTO es un `record` inmutable creado en el tick.
- Guardado en una transacción; reintento con backoff 3 veces; si falla, log `error` con el DTO serializado.
- Al apagar: `GameLoopService.StopAsync` detiene el loop, encola guardado de todos los jugadores y espera a
  `SaveService` (máx 10 s).
- Migraciones: `dotnet ef migrations add Nombre -p server/src/PixelRealms.Persistence -s server/src/PixelRealms.Server`.
  En desarrollo `Database.Migrate()` al arrancar; en prod también, con log.

## Auth
- `POST /api/auth/register {username,password}` (3–20 chars `^[a-zA-Z0-9_]+$`, password ≥ 8) → 201.
- `POST /api/auth/login` → `{ token }` JWT HS256 (clave desde config/secret, 15 min).
- `GET/POST/DELETE /api/characters` (JWT). `POST /api/game/ticket {characterId}` → `{ ticket }` (32 bytes random,
  base64url, guardado en un `ConcurrentDictionary` de `TicketService` 30 s, un solo uso).
- Rate limit REST con `Microsoft.AspNetCore.RateLimiting`: login 5/min por IP.

## Estilo C#
- `record`/`readonly record struct` para datos; clases `sealed` por defecto; `file`-scoped namespaces.
- Colecciones calientes del tick: `List<T>` reutilizadas, evitar LINQ en rutas por-tick (alloc). `Dictionary<int, Player>`.
- Combate sin asignaciones por tick: reservas de capacidad fija (impactos, auras), sin LINQ ni closures en sistemas, eventos como
  records dentro de un presupuesto por tick (HU-088 CA1 enmendado, `TickAllocationTests`); memoria nueva ≤ 1 MB/s bajo carga (ADR-018, HU-089).
- Colisión: los sistemas leen `map.Collision` (la de la instancia: puertas, HU-083), nunca `map.Data.Collision`.
- Logs estructurados: `logger.LogInformation("Player {Name} joined map {Map}", ...)`. Nunca loguear passwords/tokens.
- Solo constantes técnicas en `GameConstants` (duración del tick, tamaños de buffers, límites de red). Todo número de juego
  (GCD, radios AOI, crit, XP, afinidades…) se lee de `content/rules.json` vía `IRules` (ADR-008; regla 4 de `CLAUDE.md`).

## Tests: helpers que deben existir (crearlos en HU-003/HU-004 si no están)
- `FakeClock` (avance manual), `SeededRng(seed)` y `FixedRng(params double[] rolls)`.
- `WorldBuilder` fluido: `.WithMap(grid).WithPlayer("Ana", "mage", level: 3, at: (5,5)).WithMonster("wolf", at: (8,5)).Build()`.
- `TickRunner.Run(world, ticks)`. `TestContent.Load()` carga `content/` real del repo.
- Integración (`PixelRealms.Server.Tests/Helpers`): `TestServer` arranca el servidor real en `127.0.0.1:0` y `TestGameClient`
  habla por WebSocket con él.
