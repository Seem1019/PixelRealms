# E0 · Fundaciones técnicas

### HU-001 · Monorepo, solución .NET y CI
**Como** desarrollador **quiero** la estructura del monorepo, la solución .NET y un pipeline de CI **para** que cada
cambio compile y se pruebe automáticamente desde el primer día.
- Prioridad: Must · Estimación: M · Estado: Hecha
- Dependencias: —
- Skills: `dotnet-server`, `hu-implementation`

**Criterios de aceptación**
1. **Dado** un clon limpio **cuando** ejecuto `dotnet build server/PixelRealms.sln -warnaserror` **entonces** compila sin errores ni warnings.
2. **Dado** la solución **cuando** ejecuto `dotnet test server/PixelRealms.sln` **entonces** corre al menos un test de humo por proyecto de tests y pasa.
3. **Dado** un push o PR a `main` **cuando** corre GitHub Actions **entonces** ejecuta build + tests .NET + tests GUT headless (job separado con Godot 4.7.2 headless, la versión instalada en el PC de desarrollo) y falla si alguno falla.
4. **Dado** el repo **entonces** existen `.gitignore` (bin/obj, .godot/, .env, *.user), `.gitattributes` (LF para `*.gd`, `*.cs`, `*.json`; LFS para `*.png`, `*.wav`, `*.ogg`), `.editorconfig`, `README.md`.

**Notas técnicas**
- Proyectos: los 5 de `src/`, `tools/ContentValidator`, 4 de `tests/` (ver skill `dotnet-server`).
- `Directory.Build.props`: `net10.0`, Nullable, TreatWarningsAsErrors, `AnalysisLevel latest-recommended`.
- `Directory.Packages.props` con versiones centralizadas.
- CI: `actions/setup-dotnet@v4` con `10.0.x`; Godot vía imagen `barichello/godot-ci` o descarga del binario headless.
- Referencias entre proyectos según `docs/architecture.md` §2 (Game **no** referencia Server ni Persistence).

**Notas de implementación**
- Solución clásica `server/PixelRealms.sln` (formato .sln, no .slnx) con 5 proyectos `src/`, `tools/ContentValidator` y 4 de tests; `Directory.Build.props` (net10.0, Nullable, TreatWarningsAsErrors, AnalysisLevel latest-recommended) y `Directory.Packages.props` (CPM).
- Propiedad `OfflineBuild=true` para compilar sin NuGet (excluye EF Core/Npgsql y Testcontainers); solo para entornos sin red, ver `docs/progress/fase-1.md`.
- CI en `.github/workflows/ci.yml`: job .NET (restore, build -warnaserror, test, validador) y job GUT con Godot 4.7.2 headless que se omite si no existe `client/project.godot`.
- `.editorconfig` en la raíz con las reglas CA desactivadas y su motivo; `.gitattributes` con LF para gd/cs/json y LFS para png/wav/ogg.

---
### HU-002 · Infra local con Docker (PostgreSQL)
**Como** desarrollador **quiero** levantar la base de datos con un comando **para** no instalar Postgres a mano.
- Prioridad: Must · Estimación: S · Estado: Pendiente
- Dependencias: HU-001
- Skills: `dotnet-server`

**Criterios de aceptación**
1. **Dado** Docker Desktop **cuando** ejecuto `docker compose up -d postgres` **entonces** Postgres 17 queda en `localhost:5432` con volumen persistente `pgdata`.
2. **Dado** `.env.example` **entonces** documenta `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB`, `JWT_SIGNING_KEY`.
3. **Dado** el servidor arrancando en Development **cuando** la BD está vacía **entonces** aplica migraciones automáticamente y loguea cuáles.
4. **Dado** `PixelRealms.Persistence.Tests` **cuando** se ejecutan **entonces** usan Testcontainers (no la BD local).

**Notas técnicas**
- `docker-compose.yml` en la raíz con `healthcheck` (`pg_isready`). Servicio `server` comentado para HU-073.
- Cadena de conexión en `appsettings.Development.json` leyendo variables de entorno; secretos con `dotnet user-secrets`.
- Primera migración `Initial` con `accounts` y `characters` (ver `docs/database.md`).

---

### HU-003 · Carga y validación de contenido (ContentValidator)
**Como** diseñador **quiero** que el contenido JSON se valide automáticamente **para** detectar errores antes de ejecutar el juego.
- Prioridad: Must · Estimación: M · Estado: Hecha
- Dependencias: HU-001
- Skills: `game-content`, `dotnet-server`

**Criterios de aceptación**
1. **Dado** `content/` válido **cuando** ejecuto `dotnet run --project server/tools/ContentValidator -- content/` **entonces** imprime un resumen (`4 clases, 41 hechizos, 26 auras, 37 items, 10 monstruos, 10 tablas, 1 vendedor, rules OK`) y sale con código 0.
2. **Dado** un item con un campo no permitido **cuando** valido **entonces** falla indicando archivo, ruta JSON (`/items/3/dmgMin`) y mensaje, código ≠ 0.
3. **Dado** una tabla de botín que referencia un `itemId` inexistente **cuando** valido **entonces** falla con "lt_wolf: itemId 'wolf_fangg' no existe en items.json".
4. **Dado** un hechizo de clase cuyo `cost.resource` no coincide con el recurso de la clase **entonces** falla.
4b. **Dado** `content/rules.json` **entonces** se valida con `rules.schema.json` y se comprueba: `affinity.byClass` cubre todos los `weaponType`/`armorType`, `weaponScaling` coincide con `items[].scaling`, `bonusBySize` tiene `maxMembers` entradas, los `levelReq` de cada clase coinciden con `spellUnlockLevels`, `monsters[].level ≤ maxLevel`, y `groups[].rolls ≤ entradas`.
4c. **Dado** `RulesDb` cargado **entonces** los sistemas reciben las constantes por inyección (`IRules`), y `/reload rules` (admin) recarga el archivo en caliente sin reiniciar; si el nuevo archivo es inválido se conserva el anterior y se loguea `error`.
4d. **Dado** el contenido **entonces** también se comprueba: los tipos de `rules.weapons.types` coinciden con `affinity.weaponScaling`; ningún hechizo de clase instantáneo tiene `cooldownMs < rules.combat.minInstantSpellCooldownMs`; ninguna clase tiene más de `rules.loadout.maxSpellsPerClass` hechizos; un hechizo que use una función del motor aún no implementada (forma, targeting, efecto o campo) se marca como no disponible con un aviso, sin fallar (ADR-023).
5. **Dado** el servidor arrancando **cuando** el contenido es inválido **entonces** no arranca y muestra los mismos errores.
6. **Dado** `ContentDb` cargado **entonces** expone `Spell(id)`, `Item(id)`, `Monster(id)`, `Aura(id)`, `Class(id)`, `LootTable(id)`, `Vendor(id)`, `Rules` con `FrozenDictionary` y lanza `KeyNotFoundException` con mensaje claro.

**Notas técnicas**
- Schemas en `content/schemas/` (draft 2020-12, `$ref` entre archivos) → `JsonSchema.Net` con `SchemaRegistry`.
- Validaciones cruzadas en `CrossRefValidator` (lista completa en skill `game-content` §Procedimiento).
- Modelos `record` inmutables en `PixelRealms.Content/Defs`. Deserialización con `JsonSerializerOptions` camelCase y
  `UnmappedMemberHandling.Disallow`.
- Al terminar, añadir hook en `.claude/settings.json` (PostToolUse sobre `Write|Edit`) que ejecute el validador si el
  archivo editado está en `content/` (script `tools/hooks/validate-content.sh` que lee el JSON de stdin).
- Tests: el contenido real del repo pasa; 1 test por regla con JSON inválido mínimo.
- **Punto de partida:** `tools/ContentCheck/` (consola .NET 10 sin NuGet, `dotnet run --project tools/ContentCheck -- content/`) ya
  valida los schemas (subconjunto de 2020-12) y todas las referencias cruzadas de la skill `game-content`, y emite los avisos de
  ADR-023. Esta HU lo sustituye por `server/tools/ContentValidator` con JsonSchema.Net y `CrossRefValidator` en
  `PixelRealms.Content`: porta sus reglas (y su lista de funciones del motor implementadas), añade los tests y el hook, y
  después borra `tools/ContentCheck/`.

**Notas de implementación**
- `PixelRealms.Content`: `Defs/` (records inmutables, enums en snake_case, defaults del schema), `ContentJson` (camelCase, `UnmappedMemberHandling.Disallow`), `ContentLoader` (schemas → cruzadas → `ContentDb`), `ContentDb` (`FrozenDictionary`, `KeyNotFoundException` con mensaje), `ReloadableContent` (recarga de rules.json conservando el anterior si es inválido).
- Validación de schemas con `Validation/SchemaValidator` propio (subconjunto de 2020-12 que usan los schemas del repo) en lugar de JsonSchema.Net: decisión provisional porque el entorno no tenía NuGet; cambiar a JsonSchema.Net es un reemplazo local de esa clase (ver `docs/progress/fase-1.md`).
- `Validation/CrossRefValidator` con todas las reglas de la skill game-content y CA 3/4/4b/4d; `EngineCapabilities` es la lista de funciones del motor implementadas (ADR-023): los 4 hechizos de cono/línea quedan no disponibles con aviso.
- CLI `server/tools/ContentValidator`; el servidor no arranca con contenido inválido (CA5); hook PostToolUse en `.claude/settings.json` → `tools/hooks/validate-content.sh`; `tools/ContentCheck` retirado. Tests en `Game.Tests/Content/ContentLoaderTests` (20 casos, uno por regla).

---
### HU-004 · Esqueleto del game loop de 20 Hz
**Como** desarrollador **quiero** un bucle de simulación de paso fijo en un hilo dedicado **para** tener una base
determinista donde agregar sistemas.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-001, HU-003
- Skills: `dotnet-server`

**Criterios de aceptación**
1. **Dado** el servidor corriendo **entonces** el loop ejecuta 20 ticks/s (±1 medido en 10 s) y loguea cada 30 s `tick p50/p99` y número de entidades.
2. **Dado** un tick que tarda > 50 ms **entonces** se loguea `warn` y el loop no intenta "recuperar" más de 3 ticks atrasados (evita espiral de la muerte).
3. **Dado** `PixelRealms.Game` **entonces** no referencia ASP.NET ni EF, y existen `IGameClock`, `IRng`, `World`, `MapData` (estático, compartido), `MapInstance` (estado dinámico), `TickContext`, `IGameEvent`. `World` contiene una colección de `MapInstance` y el loop las recorre todas (ADR-007), aunque al inicio haya una sola.
4. **Dado** los helpers de test **entonces** existen `FakeClock`, `SeededRng`, `FixedRng`, `WorldBuilder`, `TickRunner` y un test de ejemplo que avanza 40 ticks.
5. **Dado** Ctrl+C **entonces** el loop se detiene limpiamente (`StopAsync`) en < 1 s.

**Notas técnicas**
- `GameLoopService : BackgroundService` crea `Thread` (`IsBackground = true`, nombre "GameLoop").
- `Channel<InboundMessage>` bounded 10 000. `TickContext { long Tick; long NowMs; List<IGameEvent> Events; ... }`.
- Orden de sistemas documentado en `docs/architecture.md` §3; registrarlos en una lista explícita, no por reflexión.

---

### HU-005 · Proyecto Godot base
**Como** desarrollador **quiero** el proyecto Godot configurado para pixel art y con la arquitectura de autoloads
**para** construir pantallas sobre una base consistente.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-001
- Skills: `godot-client`, `pixel-art-assets`

**Criterios de aceptación**
1. **Dado** `client/project.godot` **cuando** lo abro en Godot 4.5+ **entonces** arranca en la escena `Boot` sin errores y muestra "PixelRealms" con fuente pixel nítida a escala entera.
2. **Dado** una ventana redimensionada **entonces** el juego escala solo por factores enteros con barras negras (sin sprites borrosos).
3. **Dado** los autoloads **entonces** existen `EventBus`, `Settings`, `Content`, `Net`, `GameState` con tipado estático y sin errores del analizador.
4. **Dado** `Content` **cuando** arranca **entonces** carga `res://content/*.json` y `Content.spell("mage_fireball").name == "Bola de fuego"`.
5. **Dado** GUT instalado **cuando** ejecuto los tests headless **entonces** pasa un test de ejemplo.
6. **Dado** el script `tools/sync_content` **cuando** se ejecuta **entonces** copia `../content/*.json` a `client/content/` (esta carpeta está en `.gitignore`).

**Notas técnicas**
- Ajustes exactos de ventana/filtros en skill `godot-client`.
- Input Map: `move_up/down/left/right` (WASD + flechas), `target_next` (Tab), `spell_1..4` (teclas 1–4), `usable_1..4` (teclas 5–8), `toggle_inventory` (I), `toggle_character` (C), `toggle_spellbook` (P), `chat_focus` (Enter), `ui_cancel` (Esc).
- `Theme` pixel inicial con fuente libre (ver `CREDITS.md`).

---

### HU-006 · Protocolo base: sobre, registro, Ping/Pong
**Como** desarrollador **quiero** la infraestructura de mensajes en ambos lados **para** agregar mensajes nuevos de forma mecánica.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-004, HU-005
- Skills: `net-protocol`, `dotnet-server`, `godot-client`

**Criterios de aceptación**
1. **Dado** un cliente WebSocket conectado a `/ws` (sin auth todavía, flag de desarrollo) **cuando** envía `{"t":"Ping","d":{"clientTime":123}}` **entonces** recibe `{"t":"Pong","d":{"clientTime":123,"serverTick":N}}`.
2. **Dado** un mensaje con `t` desconocido, JSON inválido o > 4 KB **entonces** recibe `Error{code:"invalid_payload"}` y a la 3.ª vez se cierra la conexión.
3. **Dado** el cliente Godot **cuando** conecta **entonces** muestra RTT en el overlay de depuración (F3) actualizado cada 5 s.
4. **Dado** `PixelRealms.Protocol.Tests` **entonces** hay test de ida y vuelta para `Ping`, `Pong`, `Error` con JSON exacto.
5. **Dado** la conexión **cuando** no hay tráfico en 15 s **entonces** el servidor la cierra.

**Notas técnicas**
- `WebSocketSession`: bucle de lectura (acumula frames hasta `EndOfMessage`), tarea de escritura desde su `Channel`.
- `MessageRouter`: `Dictionary<string, Func<JsonElement, IClientMessage>>` generado en `MessageRegistry`.
- `net.gd`: `WebSocketPeer`, `poll()` en `_process`, `_handlers: Dictionary[String, Callable]`.
