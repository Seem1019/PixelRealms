# Progreso de la Fase 1 (Tier 1)

> Archivo de trabajo del agente que implementa la Fase 1 en la rama `fase-1`. Se actualiza al cerrar cada HU para que otra
> sesión pueda retomar desde donde quedó. La especificación es la documentación (`CLAUDE.md`, `docs/`, `.claude/`); este
> archivo solo registra orden, estado, decisiones provisionales y bloqueos.
>
> **El estado vigente de cada HU está en `docs/backlog/README.md`** (revisado contra el código en la auditoría del 2026-10-02,
> `main` @ `0cd4f508`). Las secciones "Entorno de la sesión", "Sin compilar" y "Bloqueos" describen la sesión de la rama
> `fase-1` (2026-10-01) y se conservan como histórico, con una nota de lo que ya se resolvió.
>
> Tras la rama `fix/phase1-audit-blockers` (2026-10-02): 57 HUs hechas, 17 parciales y 1 pendiente (HU-084). De las
> parciales, 8 esperan trabajo estético (HU-005, 024, 035, 038, 041, 050, 081 y 082) y las otras 9 esperan una decisión
> o una prueba fuera del repo (HU-064 CA3, 072, 074, 075, 080, 083, 086 CA7b, 088, 089 CA3). Cada ficha dice qué le falta.
>
> Tras la rama `feat/phase1-close-out` (2026-10-03, decisiones del 2026-10-03 aplicadas): 63 hechas, 13 parciales y HU-100
> pendiente (áreas duraderas, Fase 2). Las parciales son 8 estéticas (HU-005, 024, 035, 038, 041, 050, 081, 082) y 5 que esperan
> una prueba fuera del repo o la partida con amigos (HU-074, 075, 083 CA3, 084 CA3/CA4, 089 CA3).
>
> Tras la rama `feat/duel-zone-and-polish` (2026-10-04): 71 hechas (con la nueva HU-101, zona del duelo), 6 parciales y HU-100
> pendiente. Las parciales esperan la partida con amigos o una prueba fuera del repo (HU-074, 075, 083 CA3, 084 CA3/CA4, 089 CA3,
> con la plantilla `docs/design/playtest-notes.md`) y HU-081 CA3, la licencia de las hojas de referencia (compañero de arte).

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
| 12 | HU-070 → HU-075, HU-080 → HU-083, HU-089 | M5 | | HU-070/071/072/080 hechas; HU-073/074/075/083/088/089 parciales; HU-081/082/084 fuera | despliegue escrito sin VPS; mapas generados con `tools/maps/gen_tier1_maps.py`; LoadBot en proceso |
| 13 | HU-015, HU-090 → HU-098 (tras fusionar `fase-1`) | — | | ver README | se escribieron durante y después de la primera prueba de juego (2026-10-02); el rediseño visual de ADR-026 entró sin HU y cubre buena parte de HU-081/HU-082 |

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
  el nombre; los sprites y `walk_<dir>`/`idle_<dir>` llegaron después con el rediseño de ADR-026 (`fe8b10e`). Reversible: sustituir el `ColorRect` por un
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
  que abre (`LootBag.GoldRemainder`; hasta la auditoría se le daba siempre al primer elegible al crear la bolsa); el oro no
  espera a `exclusiveSec`. `LootWindow.gold` muestra el oro cobrado (`GoldCollected`; antes salía siempre 0).
- **HU-061 · invitación de grupo sin mensaje propio.** El protocolo no define la invitación; viaja como
  `PartyUpdate{leader: <quien invita>, members: []}` y el cliente la interpreta como "Aceptar/Rechazar". Descartado: mensaje
  nuevo `PartyInvited` (cambio de protocolo). Reversible: añadirlo y mantener el handler.
- **HU-064/HU-059 · quién solicitó.** `DuelUpdate{requested}` y `TradeUpdate{requested}` no dicen quién pidió; el cliente marca
  sus propias solicitudes salientes para no mostrarse el diálogo a sí mismo. Reversible con un campo `requesterId`.
- **HU-044 · tras el cambio de clase el servidor reenvía `Welcome`** (hechizos, barra, recurso, equipo). Descartado: tres
  mensajes separados (StatsUpdate + InventoryUpdate + uno nuevo para hechizos conocidos).
- **HU-064 · pierde por distancia quien está más lejos del punto medio inicial del duelo.** *Sustituido el 2026-10-03 por la zona
  del duelo (HU-101): pierde quien pasa más de `zoneGraceSec` fuera de ella.*
- **Logging:** se usa `Microsoft.Extensions.Logging` (consola) en vez de Serilog (skill dotnet-server). Reversible al añadir
  `Serilog.AspNetCore`; los mensajes ya son estructurados (`{Name}`).
- **HU-072 · JSON por consola con `AddJsonConsole` (Producción) + scope `ConnId`/`CharacterName`/`AccountId` en el router**,
  en lugar de Serilog (sin acceso a NuGet aquí). Descartado: dejar el CA2 sin cubrir. Cambiar a Serilog es sustituir el bloque
  `builder.Logging.*` de `ServerApp.Build` por `UseSerilog`; los scopes salen igual. *Decidido el 2026-10-03: se queda
  `AddJsonConsole` con rotación de logs de Docker (CA2 enmendado).*
- **HU-072 · `allocBytesPerSec` es del proceso entero** (`GC.GetTotalAllocatedBytes`): .NET no permite medir asignaciones por
  instancia de mapa sin instrumentar cada sistema. Descartado: estimar por proporción de entidades (inventaría un número).
  *Corregido el 2026-10-03: todas las instancias corren en el hilo del tick, así que `GC.GetAllocatedBytesForCurrentThread`
  alrededor de cada sistema y cada instancia sí da la memoria asignada por instancia (`Simulation.InstanceAllocs`); `/admin/stats`
  la publica por instancia y mantiene la del proceso.*
- **HU-072 · "áreas activas" = impactos de área pendientes** (`CastSystem.PendingImpacts`): las áreas de Fase 1 son instantáneas
  tras el casteo (ADR-015/023), no hay zonas persistentes todavía.
- **HU-070 · `/gold` audita con `item_id = Guid.Empty` y `template_id = "gold"`**: `item_audit_log` no tiene columna de oro.
  Descartado: tabla nueva `gold_audit_log` (migración extra para un comando de administración).
- **HU-071 · límites de rate en `appsettings` (`Net:RateLimits`), no en `rules.json`**: son técnicos, no de balance (ADR-008 habla
  de constantes de juego). Descartado: `rules.limits.*` (mezclaría red con gameplay). Detrás de Caddy hará falta `ForwardedHeaders`
  para que el tope por IP vea la IP real (hecho en HU-073: `UseForwardedHeaders` solo en Producción).
- **HU-080 · mapas generados por script (`tools/maps/gen_tier1_maps.py`) con el tileset placeholder**, no dibujados en Tiled.
  Descartado: editar a mano 27 500 casillas. Reversible: abrir el `.tmj` en Tiled y retocarlo (el script es solo para
  regenerar); los nombres de landmark que no están en el GDD ("Roble centenario") y la forma de los campamentos son
  decisiones del generador.
- **HU-083 · el "puzle de palancas" de la Sala 2 se deja como pilares sin mecánica.** Descartado: inventar un sistema de
  palancas/puertas (ningún ADR ni skill lo define). Si se quiere, es una HU nueva (objeto `switch` en `maps/` + estado en
  `MapInstance`). *Hecho el 2026-10-03 (rama `feat/phase1-close-out`): capas `levers`/`doors` y `MapObjectSystem`.*
- **HU-089 · prueba de carga en proceso (dominio puro) en vez de bots por WebSocket.** Descartado: un `LoadBot` de red (no
  hay servidor ni Postgres levantados en el sandbox y lo que la HU mide es el coste del tick). *Actualización 2026-10-02:
  ya existe el modo de red (`--network`) y midió la salida p95 por cliente; los FPS del cliente web siguen sin medir.* Los ~200 auras se rellenan con sangrados porque los kits del Tier 1 no
  llegan solos; las "40 áreas duraderas" no existen en la Fase 1 (áreas instantáneas).
- **HU-074 · enlace de actualización = `<servidor>/play/`** (la build web siempre es la última), configurable en
  `settings.cfg`. Descartado: una URL fija de itch.io en el código.

## Sin compilar / sin ejecutar en esta sesión
> **Resuelto:** EF Core compila con los paquetes reales; la migración `InitialCreate` existe desde `a7a2dcb` (y
> `CharacterCooldowns` desde `1a07ae1`); los tests de Testcontainers pasan en el CI y en el PC de Diego (2026-10-02).

- `server/src/PixelRealms.Persistence/Ef/*` (GameDbContext, EfAccountRepository, EfCharacterRepository, EfPersistence) y
  `server/tests/PixelRealms.Persistence.Tests/Ef/*` (PostgresFixture con Testcontainers, CharacterRepositoryTests). Pasos en el
  PC: `dotnet build server/PixelRealms.sln` → corregir lo que marque → `dotnet ef migrations add Initial -p
  server/src/PixelRealms.Persistence -s server/src/PixelRealms.Server` → `docker compose up -d postgres` → `dotnet test`.
- La versión de los paquetes en `Directory.Packages.props` se fijó de memoria (xunit.v3 3.1.0, Shouldly 4.3.0,
  Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0, EFCore.NamingConventions 10.0.0, Testcontainers.PostgreSql 4.6.0,
  Microsoft.NET.Test.Sdk 17.14.1, xunit.runner.visualstudio 3.1.4): si `dotnet restore` falla por una versión inexistente,
  `dotnet package search <id>` y ajustar.

## Bloqueos
> **Resuelto:** hay VPS y el despliegue funciona (2026-10-02, `docs/deploy.md`); Docker funciona en el PC de Diego; YATI ya no
> hace falta (ADR-026: el cliente hornea el `.tmj`); el bundle de la rama ya se importó y `fase-1` está fusionada.

- Sin Docker ni VPS: `server/Dockerfile`, `docker-compose.prod.yml`, `deploy/*` y los workflows de GitHub están escritos pero
  sin ejecutar (HU-073/074/075). Sin plantillas de exportación de Godot: `export_presets.cfg` sin probar (HU-074).
- El dispositivo de Diego estuvo desconectado toda la sesión: el bundle `fase-1.bundle` (rama completa) queda en la
  conversación; en el PC: `git fetch <ruta>/fase-1.bundle fase-1:fase-1`.
- YATI (importador Tiled del cliente): `github.com/Skoti/YATI` no se pudo clonar desde el sandbox (repo no accesible); el
  cliente deja el hueco (`addons/yati/`) y el import de mapas se prueba en el PC.

## Problemas encontrados en la documentación
_(contradicciones o huecos descubiertos al implementar; cambios mínimos hechos en los docs se listan aquí)_
- **HU-025:** el plazo de linkdead (10 s) solo existía en `docs/architecture.md` §4 y en el texto de la HU, no en
  `rules.json` (regla 4 / ADR-008). Añadido `rules.combat.linkdeadSec: 10` + schema; `linkdeadInCombatMaxSec` ya estaba.
- **M2:** `docs/protocol.md` no dice qué pasa con `CastStarted` en hechizos instantáneos ni define los bits de
  `EntitySpawn.flags` (se usa 2 = muerto, 4 = evadiendo, documentado en `Actor.Flags`). `combat.md` no da la esquiva de los
  monstruos ni la regeneración de vida resulta modesta: `spi·0.5 + sta·0.2` por segundo cura a un Sacerdote nv 3 ~10 HP/s.
- **HU-080:** la skill `world-maps` y el GDD piden "~100×100 casillas útiles por zona" **y** "60–90 s para cruzarla": a
  `baseSpeedTilesPerSec = 4`, 100 casillas son 25 s; para 60–90 s harían falta ~240–360 casillas o caminos muy sinuosos. Se
  siguió el tamaño (100×100) y se deja la contradicción para que Diego decida (tamaño, velocidad o tiempo objetivo).
  *Decidido el 2026-10-03: se mantiene el tamaño y el objetivo pasa a 25–40 s.*
- **HU-070:** `item_audit_log` no tiene columna para oro; `/gold` se audita con `item_id = Guid.Empty` y `template_id = "gold"`.
- **HU-071:** `docs/architecture.md` §4 fija los límites de rate pero `rules.json`/ADR-008 solo hablan de constantes de juego;
  quedaron en `appsettings` (`Net:RateLimits`). Conviene decir en architecture.md dónde viven.
- **HU-072:** el CA3 pide "memoria asignada por segundo por instancia": .NET solo da la asignación del proceso.
- **HU-089:** el CA1 pide 40 áreas duraderas superpuestas y ~200 auras; con el contenido de la Fase 1 no existen áreas
  duraderas y los kits generan < 40 auras: el escenario de la HU describe el Tier 3, no el 1.
- **HU-026:** el intervalo de autosave (60 s) se trató como infraestructura (`appsettings` → `Persistence:AutosaveSec`),
  no como regla de juego; si se prefiere en `rules.json`, es un cambio de una línea en `WorldSession`.
