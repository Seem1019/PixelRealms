---
name: godot-client
description: Convenciones del cliente Godot 4 en GDScript tipado — autoloads, red WebSocket, predicción/interpolación, escenas, HUD pixel-perfect y tests GUT. Úsala al crear o modificar cualquier archivo en client/.
---

# Cliente Godot — guía de trabajo

El cliente es un **espejo** del servidor: dibuja, recoge input y envía intenciones. Nunca decide daño, botín,
cooldowns reales ni inventario. Puede *predecir* visualmente (movimiento propio, cooldown en la hotbar) pero
siempre acepta la corrección del servidor.

## Versión y ajustes de proyecto
- Godot 4.5+ **edición estándar** (no .NET). Renderer `gl_compatibility` (necesario para Web y equipos modestos).
- `display/window/size/viewport_width=480`, `viewport_height=270`, `window_width_override=1440`, `window_height_override=810`,
  `stretch/mode="viewport"`, `stretch/aspect="keep"`, `stretch/scale_mode="integer"`.
- `rendering/textures/canvas_textures/default_texture_filter=0` (Nearest). `rendering/2d/snap/snap_2d_transforms_to_pixel=true`.
- Plugins en `addons/`: **GUT** (tests), **YATI** (importador Tiled). Fuente pixel: `assets/fonts/` (p. ej. m5x7/m6x11, licencia libre).

## Estructura
```
client/
  project.godot
  autoload/   net.gd  content.gd  game_state.gd  settings.gd  event_bus.gd
  scenes/     boot/  login/  character_select/  world/ (world.tscn, entity.tscn, player_self.tscn, loot_bag.tscn)
  scripts/    net/ (protocol.gd, prediction.gd, interpolation_buffer.gd, movement_step.gd)
              ui/  (tooltip_builder.gd, money_format.gd) util/
  ui/         hud.tscn, hotbar/, unit_frame/, cast_bar/, chat/, inventory/, character_panel/, loot_window/,
              vendor_window/, party_frames/, floating_text/, theme/pixel_theme.tres
  assets/     sprites/ tiles/ icons/ fonts/ sfx/
  content/    ← copia de ../content (script tools/sync_content.gd o paso de export); NO editar aquí
  tests/      test_*.gd (GUT)
```

## Autoloads (orden)
1. `EventBus` — solo señales globales (`ui_error(code)`, `target_changed(id)`).
2. `Settings` — config local (`user://settings.cfg`).
3. `Content` — carga `res://content/*.json` en diccionarios tipados por id (`Content.spell("mage_fireball")`).
4. `Net` — `WebSocketPeer`; `connect_to(url, ticket)`; `send(t: String, d: Dictionary)`; en `_process` hace `poll()`,
   lee todos los paquetes, `JSON.parse_string`, y despacha a `_handlers[t]` → emite señal `message_received(t, d)`
   y señales específicas (`snapshot(d)`, `combat_event(d)`…). Reconexión con backoff 1-2-4-8 s (máx 5 intentos).
5. `GameState` — estado espejo: `self_id`, `stats`, `inventory`, `equipment`, `hotbar`, `known_spells`, `target_id`,
   `party`, `cooldowns` (predichos). Emite señales `inventory_changed`, `stats_changed`, etc. La UI **solo** escucha a GameState.

## Estilo GDScript (obligatorio)
- Tipado estático en todo: `var speed: float = 64.0`, `func apply(d: Dictionary) -> void:`, `Array[int]`.
- `class_name` para scripts reutilizables. Señales en pasado: `signal health_changed(new_hp: int)`.
- `@onready var _bar: TextureProgressBar = %HpBar` (nodos con *unique name*). Nada de `get_node("../../X")`.
- Constantes en `UPPER_SNAKE`. Privados con `_prefijo`. Un script por escena, < 300 líneas; si crece, extraer.
- Nada de lógica en `_process` que pueda ir en señales. `_physics_process` solo para predicción propia.
- Textos visibles al jugador en español; preparar `tr()` si se agrega i18n.

## Red en el cliente
- Envío: `Net.send("CastSpell", {"spellId": id, "targetId": GameState.target_id, "reqId": Net.next_req_id()})`.
- Protocolo: nombres y campos **idénticos** a `docs/protocol.md`. Nunca inventar campos.
- Errores: `Error{code, reqId}` → `EventBus.ui_error.emit(code)` → texto rojo centrado ("Fuera de alcance").

## Movimiento
- `scripts/net/movement_step.gd` es una **traducción literal** de `MovementStep.cs`. Debe pasar
  `res://../shared/test-vectors/movement.json` (copiado a `tests/vectors/`). Si cambias uno, cambias ambos.
- Jugador propio: aplica input localmente cada 50 ms fijos (acumulador propio, no `delta` variable), guarda
  `{seq, dx, dy}` pendientes; al llegar `Snapshot.ackSeq` descarta los confirmados, fija la posición del servidor y
  re-simula los pendientes. Error < 2 px → lerp 100 ms; mayor → snap.
- Desplazamientos por habilidad (Carga, saltos): no se predicen; la posición del servidor se aplica con suavizado de ~100 ms (ADR-016).
- Castear en movimiento: desde el `CastStarted` propio hasta `CastEnded`, la predicción usa `velocidad · castMoveSpeedMult`; la
  reconciliación corrige el desfase (ADR-019).
- Remotos: `InterpolationBuffer` con `render_time = server_time − 100 ms`; lerp entre los dos snapshots vecinos.
  Si faltan datos > 250 ms, extrapolar máx 100 ms y luego congelar.

## Efectos visuales (ADR-018)
- Reservas precreadas: 32 marcas de área, 64 proyectiles, 48 textos flotantes, 32 impactos; se reutilizan (`visible = false`).
- Máximo visible: 24 marcas, 48 proyectiles, 40 textos. Prioridad: propio y grupo → áreas enemigas que te alcanzan (nunca se
  ocultan) → resto. Ticks de una misma aura agrupados; más de 6 números por entidad y segundo → uno sumado.

## UI pixel art
- `Theme` único `ui/theme/pixel_theme.tres` (NinePatch 9-slice, fuente pixel tamaño 16/8, sin antialias).
- Colores de rareza: junk `#9d9d9d`, common `#ffffff`, uncommon `#1eff00`, rare `#0070dd`, epic `#a335ee`.
- Ventanas arrastrables (`ui/common/draggable_window.tscn`), cerrar con Esc, posición guardada en Settings.
- Atajos: WASD mover, Tab ciclar objetivo, 1–4 hechizos y 5–8 utilizables (las áreas se apuntan con el ratón), I inventario, C personaje, P hechizos, Enter chat, Esc cerrar/deseleccionar.
- Drag & drop con `_get_drag_data` / `_can_drop_data` / `_drop_data` → el drop **envía** `InventoryMove` y no mueve
  nada localmente hasta recibir `InventoryUpdate` (se puede mostrar el ícono "fantasma" mientras tanto).

## Tests (GUT)
```bash
godot --path client --headless -s addons/gut/gut_cmdln.gd -gdir=res://tests -gexit
```
- Testear lógica pura: `movement_step`, `interpolation_buffer`, `money_format`, parseo de mensajes, modelo de inventario.
- Las escenas se prueban manualmente; describe al usuario los pasos.

## Export
- Presets: `Windows Desktop`, `Linux`, `Web` (threads OFF para compatibilidad con itch.io; sin SharedArrayBuffer), `Android`.
- URL del servidor por `Settings` / feature tag (`debug` → `ws://localhost:5080/ws`, `release` → `wss://<dominio>/ws`).
