extends Node2D
## Escena del mundo (HU-014, HU-020..HU-024): conecta con el ticket, envía Hello, carga el mapa del Welcome, mueve al
## jugador con predicción + reconciliación (MoveInput cada 200 ms / al cambiar / 0,0 al soltar), dibuja las entidades de la
## AOI con interpolación y muestra el nombre de la zona al entrar. El cliente nunca decide resultados: solo refleja.

const MOVE_RESEND_MS := 200
const ZONE_FADE_SEC := 2.0

@onready var _ground: PlaceholderMapRenderer = %Ground
@onready var _above: PlaceholderMapRenderer = %Above
@onready var _entities: Node2D = %Entities
@onready var _player: Node2D = %PlayerSelf
@onready var _player_name: Label = %PlayerName
@onready var _camera: Camera2D = %Camera
@onready var _hud_status: Label = %HudStatus
@onready var _zone_label: Label = %ZoneName
@onready var _fade: ColorRect = %Fade
@onready var _overlay: DebugOverlay = $DebugOverlay

var map: TmjMap
var prediction: Prediction = Prediction.new()
var in_world: bool = false

var _seq: int = 0
var _last_dx: int = 0
var _last_dy: int = 0
var _last_sent_ms: int = 0
var _remotes: Dictionary = {}  # id → RemoteEntity
var _current_zone: String = ""
var _zone_fade_left: float = 0.0
var _character_id: String = ""
var _status_clear_at: int = -1


func _ready() -> void:
	Net.register_handler("Welcome", _on_welcome)
	Net.register_handler("EntitySpawn", _on_entity_spawn)
	Net.register_handler("EntityDespawn", _on_entity_despawn)
	Net.register_handler("ChangeMap", _on_change_map)
	Net.snapshot.connect(_on_snapshot)
	Net.disconnected.connect(_on_disconnected)
	EventBus.ui_error.connect(_on_ui_error)
	_player.visible = false
	_zone_label.modulate.a = 0.0
	_fade.modulate.a = 0.0
	_hud_status.text = "Conectando…"
	var ticket := GameState.pending_ticket
	GameState.pending_ticket = ""
	_character_id = GameState.pending_character_id
	Net.ticket_refresher = _refresh_ticket
	Net.connect_to(Settings.ws_url(), ticket)
	if not Net.connected.is_connected(_on_connected):
		Net.connected.connect(_on_connected.bind(ticket))


func _on_connected(_ticket: String) -> void:
	Net.send("Hello", {"protocolVersion": Protocol.VERSION, "ticket": Net.current_ticket()})


## HU-025 CA2: ticket nuevo con el JWT guardado para retomar el mismo personaje sin pasar por la selección.
func _refresh_ticket() -> String:
	var api := get_node("/root/Api") as ApiClient
	if api == null or _character_id.is_empty():
		return ""
	var r := await api.game_ticket(_character_id)
	if not r.ok():
		return ""
	return str((r.data as Dictionary).get("ticket", ""))


func _on_welcome(d: Dictionary) -> void:
	GameState._on_welcome(d)
	_hud_status.text = ""
	_clear_remotes()
	_load_map(GameState.map_id)
	var self_state: Dictionary = d.get("self", {})
	var start := Vector2(float(self_state.get("x", 0)), float(self_state.get("y", 0)))
	var grid: CollisionGrid = map.collision if map != null else CollisionGrid.new()
	prediction.setup(grid, start, float(Content.rule("movement", "baseSpeedTilesPerSec", 4.0)))
	_player.position = start
	_player_name.text = GameState.character_name
	_player.visible = true
	_camera.position = Vector2.ZERO
	_camera.reset_smoothing()
	in_world = true
	_seq = 0
	_last_dx = 0
	_last_dy = 0


func _load_map(map_id: String) -> void:
	map = TmjMap.load_from("res://maps/%s.tmj" % map_id)
	if map == null:
		_hud_status.text = "Falta client/maps/%s.tmj (tools/sync_content.gd)" % map_id
		return
	_ground.setup(map, ["ground", "detail", "walls"])
	_above.setup(map, ["above"])
	var ts := map.tile_size
	_camera.limit_left = 0
	_camera.limit_top = 0
	_camera.limit_right = map.width * ts
	_camera.limit_bottom = map.height * ts


# --- Movimiento propio (HU-021 CA1, HU-022) -------------------------------------------------------------------------

func _physics_process(_delta: float) -> void:
	if not in_world or not Net.is_connected:
		return
	var dx := int(Input.is_action_pressed("move_right")) - int(Input.is_action_pressed("move_left"))
	var dy := int(Input.is_action_pressed("move_down")) - int(Input.is_action_pressed("move_up"))
	var now := Time.get_ticks_msec()
	var changed := dx != _last_dx or dy != _last_dy
	var moving := dx != 0 or dy != 0
	if changed or (moving and now - _last_sent_ms >= MOVE_RESEND_MS):
		_seq += 1
		Net.send("MoveInput", {"seq": _seq, "dx": dx, "dy": dy})
		_last_sent_ms = now
		_last_dx = dx
		_last_dy = dy
	if moving:
		# Un tick de simulación por frame físico (50 ms = 20 Hz, igual que el servidor).
		prediction.apply_input(_seq, dx, dy)


func _process(delta: float) -> void:
	if in_world:
		prediction.update_render(delta)
		_player.position = prediction.render_position
		_update_zone(delta)
		_overlay.pending_inputs = prediction.pending.size()
		_overlay.reconcile_error_px = prediction.last_error_px
	if _status_clear_at >= 0 and Time.get_ticks_msec() >= _status_clear_at:
		_status_clear_at = -1
		_hud_status.text = ""


func _on_snapshot(d: Dictionary) -> void:
	if not in_world:
		return
	var self_state: Dictionary = d.get("self", {})
	var ack := int(d.get("ackSeq", 0))
	prediction.reconcile(
		Vector2(float(self_state.get("x", prediction.position.x)), float(self_state.get("y", prediction.position.y))),
		ack, float(self_state.get("speed", prediction.speed_tiles_per_sec)))
	_overlay.ack_seq = ack
	var now := float(Time.get_ticks_msec())
	for e: Variant in d.get("ents", []):
		var ed: Dictionary = e
		var id := int(ed.get("id", -1))
		if _remotes.has(id):
			var r: RemoteEntity = _remotes[id]
			r.apply_state(ed, now)


# --- Entidades remotas (HU-023, HU-024) -----------------------------------------------------------------------------

func _on_entity_spawn(d: Dictionary) -> void:
	var id := int(d.get("id", -1))
	if id < 0 or id == GameState.self_id:
		return
	var r: RemoteEntity
	if _remotes.has(id):
		r = _remotes[id]
	else:
		r = RemoteEntity.new()
		_entities.add_child(r)
		_remotes[id] = r
	r.setup(d)


func _on_entity_despawn(d: Dictionary) -> void:
	var id := int(d.get("id", -1))
	if _remotes.has(id):
		var r: RemoteEntity = _remotes[id]
		_remotes.erase(id)
		r.queue_free()


func _clear_remotes() -> void:
	for r: RemoteEntity in _remotes.values():
		r.queue_free()
	_remotes.clear()


# --- Cambio de mapa (HU-027 CA2) ---------------------------------------------------------------------------------------

## Fundido a negro, carga del nuevo .tmj y recolocación del jugador; el HUD (misma escena) no se reinicia.
func _on_change_map(d: Dictionary) -> void:
	GameState._on_change_map(d)
	in_world = false
	var target := Vector2(float(d.get("x", 0)), float(d.get("y", 0)))
	var tween := create_tween()
	tween.tween_property(_fade, "modulate:a", 1.0, 0.25)
	await tween.finished
	_clear_remotes()
	_current_zone = ""
	_load_map(GameState.map_id)
	var grid: CollisionGrid = map.collision if map != null else CollisionGrid.new()
	prediction.setup(grid, target, prediction.speed_tiles_per_sec)
	_player.position = target
	_camera.reset_smoothing()
	_seq += 1
	_last_dx = 0
	_last_dy = 0
	in_world = true
	var tween_in := create_tween()
	tween_in.tween_property(_fade, "modulate:a", 0.0, 0.25)


# --- Zonas (HU-024 CA4) -----------------------------------------------------------------------------------------------

func _update_zone(delta: float) -> void:
	if map != null:
		var zone := map.zone_at(prediction.position / float(map.tile_size))
		var zone_name := str(zone.get("name", ""))
		if zone_name != _current_zone:
			_current_zone = zone_name
			if not zone_name.is_empty():
				_zone_label.text = zone_name
				_zone_label.modulate.a = 1.0
				_zone_fade_left = ZONE_FADE_SEC
	if _zone_fade_left > 0.0:
		_zone_fade_left -= delta
		_zone_label.modulate.a = clampf(_zone_fade_left / ZONE_FADE_SEC, 0.0, 1.0)


# --- Conexión -----------------------------------------------------------------------------------------------------------

func _on_disconnected(reason: String) -> void:
	in_world = false
	_hud_status.text = "Reconectando… (intento %d/%d)" % [Net.reconnect_attempt(), Net.MAX_ATTEMPTS] if Net.reconnect_attempt() > 0 else "Desconectado: %s" % reason


func _on_ui_error(code: String, _req_id: int) -> void:
	match code:
		"bad_version", "bad_ticket", "disconnected":
			get_tree().set_meta("login_notice", ApiMessages.text_for(code))
			get_tree().change_scene_to_file("res://scenes/login/login.tscn")
		_:
			_hud_status.text = Net.last_error_message if not Net.last_error_message.is_empty() else ApiMessages.text_for(code)
			_status_clear_at = Time.get_ticks_msec() + 3000
