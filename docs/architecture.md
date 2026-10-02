# Arquitectura técnica

## 1. Vista general

```mermaid
flowchart LR
  subgraph Cliente["Cliente Godot 4 (GDScript)"]
    Input[Input / UI] --> Net[autoload Net\nWebSocketPeer]
    Net --> GS[autoload GameState]
    GS --> World[World scene\nTileMap + entidades]
    Content1[autoload Content\ncontent/*.json]
  end
  subgraph Servidor[".NET 10 — proceso único"]
    REST[REST /api\nauth, personajes] --> DB[(PostgreSQL 17)]
    WS[WebSocket /ws] -->|Channel<Inbound>| Loop[GameLoop 20 Hz\nhilo dedicado]
    Loop -->|Channel<Outbound> por conexión| WS
    Loop -->|CharacterSaveDto inmutable| Saver[SaveService\nBackgroundService]
    Saver --> DB
    Content2[ContentDb\ncontent/*.json] --> Loop
  end
  Net <-->|JSON sobre WS| WS
  Input -->|HTTPS| REST
```

## 2. Proyectos .NET y dependencias

```
Protocol  ←  Game  ←  Server  →  Persistence
   ↑          ↑                      ↑
   └──── Content ─────────────────────┘ (Content solo depende de Protocol para enums compartidos)
```
- `PixelRealms.Protocol`: `record` DTOs, `MessageType` enum, `ProtocolVersion` const, `JsonSerializerContext`
  (System.Text.Json **source generated**, `camelCase`). Cero dependencias.
- `PixelRealms.Content`: modelos inmutables (`ClassDef`, `SpellDef`, `EffectDef`, `ItemTemplate`,
  `MonsterTemplate`, `LootTable`, `Rules`), `ContentDb` con diccionarios `FrozenDictionary<string, T>`, validación.
  `Rules` (de `content/rules.json`) se inyecta como `IRules` en todos los sistemas; recarga en caliente por comando admin (ADR-008).
- `PixelRealms.Game`: `World` = colección de `MapInstance` (estado vivo: jugadores, monstruos, loot, amenaza, AOI) sobre
  `MapData` inmutables compartidos (ADR-007); `Player`, `Monster`, `Actor`; sistemas por instancia (`MovementSystem`, `CastSystem`,
  `AuraSystem`, `AiSystem`, `RegenSystem`, `LootSystem`, `RespawnSystem`, `InterestSystem`), servicios globales
  (`CombatCalculator`, `StatCalculator`, `AffinityResolver`, `Inventory`, `TradeService`, `PartyService`, `ChatService`, `PvpService`).
  Emite `IGameEvent`s en una lista por tick.
- `PixelRealms.Persistence`: `GameDbContext`, entidades EF, `ICharacterRepository`, `IAccountRepository`.
- `PixelRealms.Server`: `Program.cs` (minimal APIs), `ConnectionManager`, `WebSocketSession`,
  `GameLoopService : BackgroundService` (arranca el hilo), `MessageRouter`, `SnapshotBuilder`, `SaveService`.

## 3. Game loop (servidor)

Tick fijo `Δt = 50 ms` (20 Hz) en un **hilo dedicado** (`new Thread(..., IsBackground=true)`), con
acumulador y `Stopwatch` para no derivar. Si un tick tarda > 50 ms se registra `warn` con duración.

Orden estricto de cada tick (los pasos 3–9 se ejecutan **por cada `MapInstance`**; el cambio de mapa de un jugador se aplica en el paso 2):
1. `DrainInbound()` – vacía `Channel<InboundMessage>` (máx 500 msgs/tick). Conexiones/desconexiones incluidas.
2. `Commands` – valida y aplica intenciones (`MessageRouter` → handlers). Handlers **no** calculan daño:
   encolan acciones (`BeginCast`, `SetMoveInput`, `InventoryOp`).
3. `MovementSystem` – integra movimiento con colisión AABB contra la grilla de colisión del mapa.
4. `CastSystem` – avanza casteos, resuelve los completados → `EffectResolver` (daño, cura, auras).
5. `AuraSystem` – ticks de DoT/HoT, expiraciones.
6. `AiSystem` – monstruos: Idle → Aggro → Chase → Attack → Leash/Evade.
7. `RegenSystem` – maná/energía/ira, vida fuera de combate.
8. `DeathSystem` / `LootSystem` / `RespawnSystem`.
9. `InterestSystem` – recalcula AOI (celdas de 16×16 tiles) → spawns/despawns por observador.
10. `Outbound` – cada 2 ticks `SnapshotBuilder` construye un snapshot por jugador; eventos del tick
    (`CombatEvents` agrupados por tick, `AuraApplied`…) se envían a quienes tengan la entidad en su AOI.
11. `Persistence` – jugadores `Dirty` con autosave vencido (60 s) → `CharacterSaveDto` a `SaveService`.

```mermaid
sequenceDiagram
  participant C as Cliente
  participant S as WebSocketSession (IO)
  participant L as GameLoop (tick thread)
  C->>S: {"t":"CastSpell","d":{"spellId":"mage_fireball","targetId":42,"reqId":7}}
  S->>L: InboundMessage(connId, msg) vía Channel
  L->>L: valida (rango, CD, GCD, maná, vivo, LOS) → CastState
  L-->>S: CastStarted (a AOI) vía Channel por conexión
  S-->>C: {"t":"CastStarted",...}
  Note over L: 2.0 s después (40 ticks)
  L->>L: EffectResolver → daño 37 (crit)
  L-->>C: CombatEvents + Snapshot (hpPct)
```

## 4. Red

- Transporte: **WebSocket** (`/ws`) — funciona en escritorio, móvil y **web export** de Godot.
- Formato: JSON texto, sobre `{ "t": string, "d": object }`. Límite 4 KB por mensaje entrante.
- Autenticación: login REST → JWT (15 min) → `POST /api/game/ticket` (ticket de un solo uso, 30 s) →
  `ws://host/ws?ticket=...` (el navegador no permite headers en WebSocket). Primer mensaje: `Hello`.
- Rate limiting por conexión (token bucket): `MoveInput` 30/s con ráfaga de 90 (un corte de red breve entrega los inputs de golpe), `CastSpell` 10/s, `Chat` 5/5 s, resto 20/s.
  Exceder 3 veces en 10 s → desconexión con `Error{code:"rate_limited"}`.
- Heartbeat: `Ping` cada 5 s; sin tráfico 15 s → desconectar. Reconexión: el personaje queda 10 s en el mundo
  ("linkdead") para evitar abuso de desconectar en combate.

### Movimiento (predicción + reconciliación)
- Cliente simula a ticks fijos de 50 ms (acumulador propio, no los frames) y envía un `MoveInput { seq, dx, dy }`
  (8 direcciones, valores −1/0/1) por cada tick con movimiento, más uno con 0,0 al parar. El `seq` es por conexión: vuelve a 1
  con cada `Hello` (el servidor lo reinicia al reconectar) y un `Welcome` posterior en la misma conexión no lo toca.
- Si dos inputs llegan en el mismo tick del servidor (variación de latencia), el servidor avanza un paso y confirma el
  `seq` mayor: el cliente corrige un paso (3,2 px). Con variaciones de ±10 ms no ocurre.
- Servidor aplica el último input por jugador en cada tick: `vel = normalize(dx,dy) * speed` (speed base 4 tiles/s),
  colisión AABB (caja 10×6 px en los pies) eje por eje contra la grilla de colisión.
- Snapshot incluye `ackSeq` (último `seq` procesado) y posición autoritativa del jugador propio.
- Cliente: guarda inputs no confirmados; al recibir snapshot, fija posición autoritativa y **re-simula** los
  inputs con `seq > ackSeq` usando **el mismo algoritmo** (tests en `shared/test-vectors/movement.json`).
  Si el error es < 2 px, se corrige suavemente (lerp 100 ms); si es mayor, snap.
- Entidades remotas: buffer de interpolación de 100 ms entre los dos snapshots que rodean `renderTime`.
- Desplazamientos por habilidad (Carga, saltos a un punto): los aplica el servidor; el cliente no los predice y muestra la
  posición recibida con un suavizado de ~100 ms (ADR-016). No pasan por `MovementStep` ni por los vectores de movimiento.

## 5. Persistencia
- El estado vivo está en memoria. La BD es la copia durable.
- Guardado: al salir, al cambiar de mapa, cada 60 s si `Dirty`, y al apagar el servidor (`IHostApplicationLifetime`).
- `SaveService` guarda **el personaje completo** (stats, posición, items) en **una transacción**:
  `DELETE character_items WHERE character_id = X` + `INSERT` masivo. Simple y sin inconsistencias para el volumen MVP.
- `ItemInstance.Id` (UUID v7) nunca se reutiliza → permite auditar duplicaciones (`item_audit_log`).

## 6. Cliente Godot
- Autoloads: `Net` (WebSocketPeer, cola, reconexión, dispatch por `t` a señales), `Content` (carga JSON de
  `res://content`), `GameState` (personaje propio, inventario, target, party — **solo reflejo** del servidor),
  `Settings`, `UiStyle` (tema de la interfaz).
- Escenas: `Boot` → `Login` → `CharacterSelect` → `World` (TileMapLayer por Tiled/YATI, `Entities` YSort,
  `Camera2D` pixel-perfect) + `HUD` (CanvasLayer: barras, hotbar, target frame, cast bar, chat, party, ventanas).
- Resolución lógica 480×270 (16:9), escalado entero (`stretch mode = canvas_items`, `scale mode = integer`, ADR-025): el 2D
  se dibuja a la resolución de la ventana, así que el texto sale nítido a su tamaño y el mundo conserva la escala entera.
- Un único `Theme` creado por código (`scripts/ui/ui_theme.gd`; el autoload `UiStyle` lo fusiona con el tema por defecto
  del motor, porque los `Control` dentro de un `CanvasLayer` no heredan el de la ventana):
  tamaños de letra, espaciados, colores y estilos. Tooltips propios (`RichTooltip`) de ancho contenido.
- Aspecto (ADR-026): fuente pixel Tiny5 a su tamaño nativo (8 px, ×2 para titulares), estilos 9-slice (`StyleBoxTexture`)
  y arte en la paleta Resurrect 64 generado por `tools/art/`. El mapa se dibuja con `TerrainBaker`/`TerrainRenderer`: al
  cargar, hornea el `.tmj` (los mismos GIDs que lee la colisión) con autotile dual-grid en texturas por trozos, una capa
  bajo las entidades y otra (copas, aleros) encima. Las entidades son `EntityVisual` (sprite de 32×32 animado, sombra y
  placa de nombre); `NameplateLayout` separa las placas que se pisan.

## 7. Despliegue (amigos)
- VPS Linux (2 vCPU / 2–4 GB). `docker compose`: `server`, `postgres`, `caddy` (TLS automático → `wss://`).
- Cliente: export Web en el mismo dominio (`/play`) + builds Windows en itch.io (canal privado).
- Backups: `pg_dump` diario por cron → 7 copias.

## 8. Límites conocidos (aceptados en MVP)
- Un solo proceso; varias `MapInstance` (MVP: `meadow` y `mine`, una copia de cada); sin sharding. Objetivo: 50 jugadores y 300 monstruos a < 10 ms/tick; combate ≤ 4 ms p99 por instancia, límites de áreas en `rules.limits` y escenario de carga "Mina llena" (ADR-018, HU-089).
- Una copia por mapa: el jefe de la Mina es compartido. Instancias por grupo = N `MapInstance` del mismo `MapData` (post-MVP, sin cambios de protocolo).
- JSON en vez de binario (≈ 25 KB/s por cliente a 10 Hz). Optimización a MessagePack post-MVP (ADR-002), sin HU todavía.
