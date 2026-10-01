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
- `Serilog.AspNetCore` (consola, JSON en prod). `JsonSchema.Net` (validador de contenido).
- Tests: `xunit.v3`, `Shouldly`, `NSubstitute`, `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.PostgreSql`.

## Estructura
```
server/
  PixelRealms.sln  Directory.Build.props  Directory.Packages.props  .editorconfig
  src/PixelRealms.Protocol/    Messages/{ClientMessages.cs, ServerMessages.cs}, MessageRegistry.cs, ProtocolJsonContext.cs
  src/PixelRealms.Content/     Defs/*.cs (records), ContentLoader.cs, ContentDb.cs, CrossRefValidator.cs
  src/PixelRealms.Game/
    Core/        World.cs, EntityId.cs, IGameClock.cs, IRng.cs, GameEvents.cs, Vec2.cs
    Entities/    Actor.cs, Player.cs, Monster.cs, LootBag.cs, Npc.cs
    Map/         CollisionGrid.cs, TiledMapLoader.cs, LineOfSight.cs, Pathfinder.cs (A*)
    Movement/    MovementSystem.cs, MovementStep.cs (algoritmo compartido con cliente)
    Combat/      CombatCalculator.cs, CastSystem.cs, EffectResolver.cs, AuraSystem.cs, ThreatTable.cs
    Ai/          AiSystem.cs, MonsterBrain.cs
    Items/       Inventory.cs, Equipment.cs, ItemInstance.cs, InventoryOps.cs, LootSystem.cs, VendorService.cs
    Progression/ XpService.cs, StatCalculator.cs
    Social/      PartyService.cs, ChatService.cs
    Interest/    InterestSystem.cs (grid AOI)
  src/PixelRealms.Persistence/ GameDbContext.cs, Entities/*.cs, Repositories/*.cs, Migrations/
  src/PixelRealms.Server/
    Program.cs, Auth/ (JWT, tickets), Api/ (endpoints REST), Net/ (WebSocketSession, ConnectionManager,
    MessageRouter, Handlers/*.cs, RateLimiter.cs, SnapshotBuilder.cs), Hosting/ (GameLoopService, SaveService)
  tools/ContentValidator/
  tests/PixelRealms.Game.Tests  PixelRealms.Protocol.Tests  PixelRealms.Server.Tests  PixelRealms.Persistence.Tests
```

## Reglas del game loop
- `GameLoopService` crea un `Thread` dedicado. Bucle con `Stopwatch`, acumulador y `Thread.Sleep` fino
  (o `SpinWait` los últimos 2 ms). Tick = 50 ms. Métrica `tick_ms` (p50/p99) logueada cada 30 s.
- Entrada: `Channel<InboundMessage>` (`BoundedChannelOptions(10_000){ FullMode = DropWrite }`); si se llena,
  loguear `warn` y desconectar la conexión más ruidosa.
- Salida: cada `WebSocketSession` tiene su `Channel<ReadOnlyMemory<byte>>` (bounded 256); si se llena el cliente
  es lento → desconectar. El tick **serializa** y encola; la tarea de envío solo escribe al socket.
- Handlers: `interface IMessageHandler<T> { void Handle(Player p, T msg, TickContext ctx); }` ejecutados en el tick.
  Validan y **emiten errores** con `ctx.SendError(p, "on_cooldown", msg.ReqId)`.
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
  base64url, guardado en `IMemoryCache` 30 s, un solo uso).
- Rate limit REST con `Microsoft.AspNetCore.RateLimiting`: login 5/min por IP.

## Estilo C#
- `record`/`readonly record struct` para datos; clases `sealed` por defecto; `file`-scoped namespaces.
- Colecciones calientes del tick: `List<T>` reutilizadas, evitar LINQ en rutas por-tick (alloc). `Dictionary<int, Player>`.
- Combate sin asignaciones por tick: reservas de capacidad fija (áreas, impactos, auras), eventos en buffer circular; memoria nueva ≤ 1 MB/s bajo carga (ADR-018, HU-089).
- Logs estructurados: `logger.LogInformation("Player {Name} joined map {Map}", ...)`. Nunca loguear passwords/tokens.
- Solo constantes técnicas en `GameConstants` (duración del tick, tamaños de buffers, límites de red). Todo número de juego
  (GCD, radios AOI, crit, XP, afinidades…) se lee de `content/rules.json` vía `IRules` (ADR-008; regla 4 de `CLAUDE.md`).

## Tests: helpers que deben existir (crearlos en HU-003/HU-004 si no están)
- `FakeClock` (avance manual), `SeededRng(seed)` y `FixedRng(params double[] rolls)`.
- `WorldBuilder` fluido: `.WithMap(grid).WithPlayer("Ana", "mage", level: 3, at: (5,5)).WithMonster("wolf", at: (8,5)).Build()`.
- `TickRunner.Run(world, ticks)`. `TestContent.Load()` carga `content/` real del repo.
