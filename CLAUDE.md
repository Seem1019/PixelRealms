# PixelRealms — Memoria del proyecto para Claude Code

Mini-MMORPG 2D pixel art top-down para ~20 jugadores (amigos). Inspiración: Heartwood Online.
Combate **híbrido** en tiempo real (tab-target para un objetivo, áreas apuntadas libremente; ADR-015), 4 clases con
**equipo libre y afinidad**, hechizos, botín asignado por item, inventario, intercambio, chat, grupos y **duelos**.
Nivel máximo 15 en 3 tiers; el MVP son los 3 tiers en 3 fases y la **Fase 1** es el Tier 1 (aldea, campos, colinas, Mina y
su jefe). Los pilares de diseño (`docs/design/gdd.md` §Pilares) filtran cualquier feature nueva.

> Idioma: el código, los identificadores y los commits van en **inglés**. La documentación, las HUs y los
> comentarios de diseño van en **español**.

## Arquitectura en una línea
Cliente **Godot 4 (GDScript)** "tonto" ⇄ **WebSocket + JSON** ⇄ Servidor **.NET 10 autoritativo** (tick 20 Hz) ⇄ **PostgreSQL 17**.
El contenido del juego vive en `content/*.json` (validado con JSON Schema) y lo leen el cliente y el servidor.
**Toda constante numérica** (XP, GCD, crit, afinidades, PvP…) está en `content/rules.json` (ADR-008): nada hardcodeado.
El mundo son varios mapas (`MapData` estático + `MapInstance` dinámica, ADR-007): las zonas de un tier en un mapa, cada cueva en el suyo.

Detalle: `docs/architecture.md` · Protocolo: `docs/protocol.md` · BD: `docs/database.md` · Diseño: `docs/design/`

## Estructura del monorepo
```
server/            Solución .NET (PixelRealms.sln)
  src/PixelRealms.Protocol      DTOs de mensajes + registro de tipos (sin dependencias)
  src/PixelRealms.Content       Carga y validación de content/*.json → modelos inmutables
  src/PixelRealms.Game          Dominio PURO: mundo, sistemas, combate, inventario (sin IO, sin ASP.NET)
  src/PixelRealms.Persistence   EF Core + Npgsql, repositorios, migraciones
  src/PixelRealms.Server        Host ASP.NET Core: REST auth/personajes, WebSocket, GameLoop
  tools/ContentValidator        CLI: valida schemas + referencias cruzadas
  tests/*.Tests                 xUnit v3 + Shouldly (+ Testcontainers para Persistence)
client/            Proyecto Godot (project.godot)
  autoload/ scenes/ scripts/ ui/ assets/ tests/ (GUT)
content/           JSON de juego + rules.json (constantes) + content/schemas/*.schema.json
maps/              Mapas Tiled (.tmj) + tilesets (fuente de verdad del mundo)
shared/test-vectors/  Casos JSON que DEBEN pasar en servidor (xUnit) y cliente (GUT)
docs/              Arquitectura, ADRs, diseño, backlog (HUs), prompts
```

## Reglas NO negociables
1. **El servidor es la autoridad.** El cliente envía *intenciones* (`CastSpell`, `InventoryMove`, `MoveInput`),
   nunca resultados (daño, posición final, items). Todo se valida en servidor: rango, cooldown, recurso,
   objetivo vivo, línea de visión, propiedad del item, rate limit.
2. **Un solo hilo muta el mundo.** Solo el `GameLoop` (tick thread) toca `World`. La red encola en
   `Channel<T>`; la persistencia recibe *copias inmutables*. Nunca `lock` dentro de `PixelRealms.Game`.
3. **`PixelRealms.Game` es puro y determinista:** sin `DateTime.Now`, sin `Random` global, sin IO.
   Inyecta `IGameClock` y `IRng` (semilla). Todo sistema nuevo nace con tests unitarios.
4. **Datos, no código.** Una clase, hechizo, item o monstruo nuevo = JSON en `content/` + validador verde.
   Si hace falta código, es porque falta un *tipo de efecto* genérico (ver skill `game-content`). Ningún número mágico:
   toda constante se lee de `rules.json` vía `IRules`. Nada de `if (classId == ...)`: las diferencias entre clases son tablas.
5. **Protocolo versionado.** Cambiar un mensaje = actualizar `docs/protocol.md`, DTO C#, parser GDScript y
   subir `ProtocolVersion` si rompe compatibilidad (skill `net-protocol`).
6. **Movimiento idéntico en ambos lados.** Cualquier cambio a movimiento/colisión debe pasar
   `shared/test-vectors/movement.json` en xUnit **y** en GUT.
7. **GDScript con tipado estático siempre** (`var hp: int`, `func f(x: float) -> void`). Sin `get_node` con rutas
   mágicas fuera de `_ready`; usar `@onready` y `%UniqueName`.
8. **Nada de secretos en el repo.** Usar `dotnet user-secrets` / `.env` (ignorado). `.env.example` sí se versiona.

## Comandos
```bash
# Infra local
docker compose up -d postgres                       # Postgres 17 en :5432
# Servidor
dotnet build server/PixelRealms.sln
dotnet test  server/PixelRealms.sln
dotnet run --project server/src/PixelRealms.Server  # http://localhost:5080, ws://localhost:5080/ws
dotnet run --project server/tools/ContentValidator -- content/
dotnet ef migrations add <Name> -p server/src/PixelRealms.Persistence -s server/src/PixelRealms.Server
# Cliente (Godot en PATH como `godot`)
godot --path client --headless -s addons/gut/gut_cmdln.gd -gdir=res://tests -gexit
godot --path client                                  # abrir/ejecutar
```

## Cómo trabajamos (flujo por HU)
- Cada tarea parte de una HU en `docs/backlog/`. Usa la skill **`hu-implementation`**.
- Empieza en **modo plan**: lee la HU, las skills relacionadas y los archivos implicados; propone plan; espera OK.
- Tests primero a partir de los criterios de aceptación (Dado/Cuando/Entonces).
- Antes de cerrar: `dotnet test`, tests GUT, validador de contenido y el subagente `server-authority-reviewer`
  si tocaste servidor. Marca la HU como `Hecha` en su archivo y en `docs/backlog/README.md`.
- Commits pequeños con Conventional Commits: `feat(combat): ...`, `fix(net): ...`, `content(items): ...`.
- **Los commits los firma una persona del equipo, nunca Claude**, también en sesiones de Claude Code en la web: sin
  autor/committer `Claude <noreply@anthropic.com>` y sin trailers `Co-Authored-By: Claude…` ni `Claude-Session:`. Si
  `git var GIT_AUTHOR_IDENT` muestra a Claude, no hagas commit y pide al usuario su identidad. Lo hacen cumplir el hook
  `tools/hooks/guard-commit-author.sh` (`.claude/settings.json`) y el job `commit-authors` del CI.

## Skills del proyecto (`.claude/skills/`)
| Skill | Úsala cuando… |
|---|---|
| `hu-implementation` | implementes cualquier historia de usuario de principio a fin |
| `dotnet-server` | toques cualquier cosa en `server/` |
| `godot-client` | toques cualquier cosa en `client/` |
| `net-protocol` | agregues o cambies un mensaje cliente⇄servidor |
| `game-content` | crees/edites clases, hechizos, items, monstruos o botín |
| `combat-system` | toques daño, curación, casteo, auras, amenaza, muerte |
| `inventory-items` | toques inventario, equipo, botín, vendedor u oro |
| `world-maps` | crees o edites mapas Tiled, colisiones o spawns |
| `pixel-art-assets` | generes o importes sprites, tiles, iconos o UI |

Subagentes (`.claude/agents/`): `server-authority-reviewer`, `content-designer`, `qa-test-writer`.

## Glosario
- **Tick**: paso de simulación del servidor (50 ms). **Snapshot**: estado enviado al cliente (cada 2 ticks = 10 Hz).
- **AOI** (Area of Interest): entidades que un jugador ve (celdas de 16×16 tiles, radio 1 celda).
- **GCD**: global cooldown (`rules.combat.gcdMs`). **Aura**: efecto temporal (DoT, HoT, buff, debuff, stun, escudo).
- **Afinidad**: alta/media/baja de una clase con un tipo de arma/armadura; multiplica todo lo numérico del item (`rules.affinity`).
- **MapData / MapInstance**: datos estáticos de un mapa (compartidos) / estado vivo de una copia del mapa. **Portal**: objeto que cambia de mapa.
- **Ruleset PvP**: reglas de un modo de PvP (`rules.pvp`); el MVP solo tiene `duel`.
- **Fase**: etapa del MVP, una por tier (Fase 1 = Tier 1, tope de nivel 6); `rules.world.currentPhase` y `rules.progression.levelCapByPhase` (ADR-013).
- **Barra**: 4 hechizos equipados (teclas 1–4) + 4 utilizables (teclas 5–8), `rules.loadout` (ADR-014). El ataque básico lo da el arma y no ocupa casilla (ADR-019).
- **Pentagrama**: perfil de cada clase en 5 puntas (mono, área, control, movilidad, armadura); balance con la regla 40/75 (ADR-020, `docs/design/class-kits.md`).
- **Template** (`ItemTemplate`, `MonsterTemplate`): definición en JSON. **Instance**: copia viva en el mundo.
