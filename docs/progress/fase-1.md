# Progreso de la Fase 1 (Tier 1)

> Archivo de trabajo del agente que implementa la Fase 1 en la rama `fase-1`. Se actualiza al cerrar cada HU para que otra
> sesión pueda retomar desde donde quedó. La especificación es la documentación (`CLAUDE.md`, `docs/`, `.claude/`); este
> archivo solo registra orden, estado, decisiones provisionales y bloqueos.

## Entorno de la sesión (2026-10-01)
- Compilación y tests: SDK .NET 10 (10.0.112) en un sandbox Linux **sin acceso a NuGet**. El repo referencia los paquetes
  reales (`Directory.Packages.props`); en el sandbox se sustituyen `xunit.v3`, `Shouldly`, `Microsoft.NET.Test.Sdk` y
  `xunit.runner.visualstudio` por *shims* locales con la misma API (fuera del repo). Todo lo que no se puede compilar sin NuGet
  (EF Core + Npgsql, Testcontainers) se escribe pero **no se ha compilado**: ver `OfflineBuild` abajo y la sección "Sin compilar".
- Godot 4.7.2 (Linux, headless) + GUT 9.6.1 disponibles en el sandbox para `--check-only` y tests GUT. YATI no se pudo descargar.
- Docker/PostgreSQL: no hay demonio Docker en el sandbox; `docker compose up -d postgres` queda por probar en el PC de Diego.
- Propiedad MSBuild `OfflineBuild=true` (`dotnet build -p:OfflineBuild=true`): excluye los paquetes y fuentes que necesitan
  NuGet (EF Core/Npgsql en `Persistence`, Testcontainers en `Persistence.Tests`) y registra el repositorio en memoria. Es
  solo para entornos sin NuGet; el build normal es el documentado en `CLAUDE.md`.

## Orden de ejecución (hitos de `docs/backlog/README.md`)
| # | HU | Hito | Depende de | Estado | Notas |
|---|---|---|---|---|---|
| 1 | HU-001 Monorepo, solución .NET y CI | M1 | — | Hecha | commit `feat(infra)` |
| 2 | HU-002 Infra local con Docker | M1 | 001 | Hecha (EF sin compilar) | migración `Initial` por generar en el PC |
| 3 | HU-003 Carga y validación de contenido | M1 | 001 | Hecha | validador propio en vez de JsonSchema.Net (provisional) |
| 4 | HU-004 Game loop 20 Hz | M1 | 001, 003 | Hecha | |
| 5 | HU-005 Proyecto Godot base | M1 | 001 | Hecha (parcial) | fuente pixel y YATI pendientes; escalado sin verificar visualmente |
| 6 | HU-006 Protocolo base | M1 | 004, 005 | Hecha | integración sin Mvc.Testing (WebApplication en puerto libre) |
| 7 | HU-010 Registro · HU-011 Login · HU-012 Crear personaje · HU-013 Listar/borrar · HU-014 Entrar al mundo | M1 | 002/003/006 | Hechas | JWT HS256 propio (provisional); pantallas sin comprobar en el editor |
| 8 | HU-020 Mapa Tiled · HU-021 Movimiento · HU-022 Predicción · HU-023 AOI · HU-024 Cámara · HU-025 Linkdead · HU-026 Guardado · HU-027 Portales | M1 | | Hechas (M1 completo salvo validaciones visuales) | cliente dibuja el mapa con colores placeholder (sin YATI/tiles); CA4b/CA5 de HU-022 y CA4/CA5 de HU-023 se cierran en M2/M5 |
| 9 | HU-030 → HU-039, HU-086, HU-085, HU-087, HU-088, HU-040, HU-041 | M2 | | M2 hecho (HU-088 parcial) | 104 tests de dominio nuevos; HUD por código sin arte |
| 10 | HU-050 → HU-059 | M3 | | Hechas | ventanas por código sin arte |
| 11 | HU-042 → HU-044, HU-060 → HU-064 | M4 | | Hechas (HU-063 parcial: lista como texto) | invitación de grupo reutiliza PartyUpdate |
| 12 | HU-070 → HU-075, HU-080 → HU-083, HU-089 | M5 | | Pendiente | HU-084 y las validaciones jugando quedan fuera |

## Decisiones provisionales (revisar)
- **HU-003 · validación de schemas sin JsonSchema.Net.** Se eligió portar el `SchemaValidator` de `tools/ContentCheck`
  (subconjunto de draft 2020-12 con exactamente las palabras clave que usan los schemas del repo) a `PixelRealms.Content`.
  Descartado: JsonSchema.Net (NuGet bloqueado en el sandbox). Reversible: sustituir la clase `Validation/SchemaValidator`
  por `JsonSchema.Net` + `SchemaRegistry` sin tocar el resto. Riesgo: una palabra clave nueva en un schema no se valida
  hasta añadirla.

- **HU-006 · tests de integración sin `Microsoft.AspNetCore.Mvc.Testing`.** `TestServer` arranca el `WebApplication` real en
  `http://127.0.0.1:0` y `TestGameClient` usa `ClientWebSocket`. Descartado: `WebApplicationFactory` (NuGet bloqueado y el
  WebSocket real es lo que importa probar). Reversible: añadir el paquete y envolver `ServerApp.Build`.
- **HU-005 · fuente pixel.** No se eligió fuente (el prompt P4 pide proponer 2 y que Diego elija); se usa la de Godot con
  tamaño 8/32. Candidatas libres: m5x7 y m6x11 (Daniel Linssen, CC0) → `client/assets/fonts/` + `CREDITS.md`.

- **HU-011 · JWT HS256 a mano (`Auth/JwtService`)** en lugar de `Microsoft.AspNetCore.Authentication.JwtBearer`. Descartado el
  paquete por NuGet; la validación es un filtro de endpoint (`RequireJwt`). Reversible: sustituir el filtro por
  `AddAuthentication().AddJwtBearer` con la misma clave.
- **HU-022/023 · unidades del protocolo.** El dominio trabaja en tiles (`Vec2`) y el protocolo en píxeles con 2 decimales
  (`docs/protocol.md`); la conversión vive solo en `SnapshotBuilder`/`PlayerMapper` y el cliente predice en píxeles con
  `movement_step.gd`. Descartado: enviar tiles con decimales (rompería los vectores compartidos, que están en px).
- **HU-023 · entidades remotas sin sprites.** `RemoteEntity` dibuja un rectángulo de color por tipo (jugador/monstruo/NPC) y
  el nombre; los sprites y `walk_<dir>`/`idle_<dir>` llegan con HU-070. Reversible: sustituir el `ColorRect` por un
  `AnimatedSprite2D` sin tocar la red.
- **HU-027 · mapas en el cliente sin escenas .tscn.** La HU habla de `res://maps/<mapId>.tscn`; como no hay YATI ni tiles,
  el cliente carga el `.tmj` directamente (`TmjMap` + renderer placeholder). Descartado: generar `.tscn` vacíos. Reversible:
  cuando exista el import de Tiled, `_load_map` cambia a `change_scene`/instanciar la escena del mapa.
- **HU-032 · sin arma no hay ataque básico.** Un jugador sin arma en la mano principal no puede activar el básico
  (`Error{invalid_target, "Necesitas un arma"}`). Descartado: daño desarmado inventado (no hay número documentado).
  Reversible: añadir `rules.weapons.unarmed` y tratarlo como un tipo más.
- **HU-033 · instantáneos emiten `CastStarted{durationMs: 0}` + `CastEnded{done}`.** El protocolo solo define CastStarted
  para casteos; se emite también para instantáneos para que el cliente dibuje el efecto con la misma secuencia. Descartado: un
  mensaje nuevo `SpellCast`. Reversible: filtrar `durationMs == 0` en el EventDispatcher.
- **HU-034 · Carga (`dash`) coloca al lanzador adyacente en el mismo tick** (la HU dice "en ≤ 3 ticks"); el cliente lo suaviza
  ~100 ms como un salto. Descartado: interpolar 3 ticks en servidor (más estado por nada visible).
- **HU-036 · monstruos sin esquiva y con crítico fijo `critBase`** (combat.md solo fija el crítico: "5 % fijo"); ritmos de la
  IA (percepción 250 ms, A* 500 ms / 2 casillas / 200 nodos, pausas de patrulla 2–6 s) como constantes de `MonsterAiSystem`
  (vienen de la skill/HU, no hay sección `rules.ai`; `TODO(balance)` en el código).
- **HU-031/HU-037 · respawn independiente del cadáver.** `respawnSec` (30 s en slime) es menor que `corpseLifetimeSec` (60 s):
  el monstruo nuevo aparece mientras el cadáver sigue visible. Descartado: esperar al cadáver (contradiría CA3 de HU-031).
- **HU-050 · "el cadáver brilla" = bit 8 de `EntitySpawn.flags`** reenviado solo a los ganadores (el protocolo no tiene mensaje
  para ello). Descartado: mensaje nuevo `LootAvailable`. Reversible: añadirlo y quitar el bit.
- **HU-050 · oro del cadáver:** la parte de cada elegible se cobra al abrir (`LootOpen`), el resto de la división al primero
  que abre; el oro no espera a `exclusiveSec`.
- **HU-061 · invitación de grupo sin mensaje propio.** El protocolo no define la invitación; viaja como
  `PartyUpdate{leader: <quien invita>, members: []}` y el cliente la interpreta como "Aceptar/Rechazar". Descartado: mensaje
  nuevo `PartyInvited` (cambio de protocolo). Reversible: añadirlo y mantener el handler.
- **HU-064/HU-059 · quién solicitó.** `DuelUpdate{requested}` y `TradeUpdate{requested}` no dicen quién pidió; el cliente marca
  sus propias solicitudes salientes para no mostrarse el diálogo a sí mismo. Reversible con un campo `requesterId`.
- **HU-044 · tras el cambio de clase el servidor reenvía `Welcome`** (hechizos, barra, recurso, equipo). Descartado: tres
  mensajes separados (StatsUpdate + InventoryUpdate + uno nuevo para hechizos conocidos).
- **HU-064 · pierde por distancia quien está más lejos del punto medio inicial del duelo.**
- **Logging:** se usa `Microsoft.Extensions.Logging` (consola) en vez de Serilog (skill dotnet-server). Reversible al añadir
  `Serilog.AspNetCore`; los mensajes ya son estructurados (`{Name}`).
- **HU-072 · JSON por consola con `AddJsonConsole` (Producción) + scope `ConnId`/`CharacterName`/`AccountId` en el router**,
  en lugar de Serilog (sin acceso a NuGet aquí). Descartado: dejar el CA2 sin cubrir. Cambiar a Serilog es sustituir el bloque
  `builder.Logging.*` de `ServerApp.Build` por `UseSerilog`; los scopes salen igual.
- **HU-072 · `allocBytesPerSec` es del proceso entero** (`GC.GetTotalAllocatedBytes`): .NET no permite medir asignaciones por
  instancia de mapa sin instrumentar cada sistema. Descartado: estimar por proporción de entidades (inventaría un número).
- **HU-072 · "áreas activas" = impactos de área pendientes** (`CastSystem.PendingImpacts`): las áreas de Fase 1 son instantáneas
  tras el casteo (ADR-015/023), no hay zonas persistentes todavía.
- **HU-070 · `/gold` audita con `item_id = Guid.Empty` y `template_id = "gold"`**: `item_audit_log` no tiene columna de oro.
  Descartado: tabla nueva `gold_audit_log` (migración extra para un comando de administración).
- **HU-071 · límites de rate en `appsettings` (`Net:RateLimits`), no en `rules.json`**: son técnicos, no de balance (ADR-008 habla
  de constantes de juego). Descartado: `rules.limits.*` (mezclaría red con gameplay). Detrás de Caddy hará falta `ForwardedHeaders`
  para que el tope por IP vea la IP real (pendiente HU-073).

## Sin compilar / sin ejecutar en esta sesión
- `server/src/PixelRealms.Persistence/Ef/*` (GameDbContext, EfAccountRepository, EfCharacterRepository, EfPersistence) y
  `server/tests/PixelRealms.Persistence.Tests/Ef/*` (PostgresFixture con Testcontainers, CharacterRepositoryTests). Pasos en el
  PC: `dotnet build server/PixelRealms.sln` → corregir lo que marque → `dotnet ef migrations add Initial -p
  server/src/PixelRealms.Persistence -s server/src/PixelRealms.Server` → `docker compose up -d postgres` → `dotnet test`.
- La versión de los paquetes en `Directory.Packages.props` se fijó de memoria (xunit.v3 3.1.0, Shouldly 4.3.0,
  Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0, EFCore.NamingConventions 10.0.0, Testcontainers.PostgreSql 4.6.0,
  Microsoft.NET.Test.Sdk 17.14.1, xunit.runner.visualstudio 3.1.4): si `dotnet restore` falla por una versión inexistente,
  `dotnet package search <id>` y ajustar.

## Bloqueos
- YATI (importador Tiled del cliente): `github.com/Skoti/YATI` no se pudo clonar desde el sandbox (repo no accesible); el
  cliente deja el hueco (`addons/yati/`) y el import de mapas se prueba en el PC.

## Problemas encontrados en la documentación
_(contradicciones o huecos descubiertos al implementar; cambios mínimos hechos en los docs se listan aquí)_
- **HU-025:** el plazo de linkdead (10 s) solo existía en `docs/architecture.md` §4 y en el texto de la HU, no en
  `rules.json` (regla 4 / ADR-008). Añadido `rules.combat.linkdeadSec: 10` + schema; `linkdeadInCombatMaxSec` ya estaba.
- **M2:** `docs/protocol.md` no dice qué pasa con `CastStarted` en hechizos instantáneos ni define los bits de
  `EntitySpawn.flags` (se usa 2 = muerto, 4 = evadiendo, documentado en `Actor.Flags`). `combat.md` no da la esquiva de los
  monstruos ni la regeneración de vida resulta modesta: `spi·0.5 + sta·0.2` por segundo cura a un Sacerdote nv 3 ~10 HP/s.
- **HU-026:** el intervalo de autosave (60 s) se trató como infraestructura (`appsettings` → `Persistence:AutosaveSec`),
  no como regla de juego; si se prefiere en `rules.json`, es un cambio de una línea en `WorldSession`.
