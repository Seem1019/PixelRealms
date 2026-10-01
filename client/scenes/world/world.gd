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
@onready var _hud: CombatHud = %CombatHud
@onready var _floating: FloatingText = %FloatingText
@onready var _reticle: AoeReticle = %AoeReticle

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
## Apuntado de área (HU-086 CA1): hechizo en curso de apuntar o vacío.
var _aiming_spell: Dictionary = {}
## Último CastStarted propio de un salto/Carga: la corrección grande siguiente se suaviza en vez de saltar (HU-087 CA4).
var _forced_move_until_ms: int = -1
const TARGET_CYCLE_RANGE_TILES := 12.0


func _ready() -> void:
	Net.register_handler("Welcome", _on_welcome)
	Net.register_handler("EntitySpawn", _on_entity_spawn)
	Net.register_handler("EntityDespawn", _on_entity_despawn)
	Net.register_handler("ChangeMap", _on_change_map)
	Net.message_received.connect(_on_message)
	Net.combat_events.connect(_on_combat_events)
	Net.snapshot.connect(_on_snapshot)
	GameState.target_changed.connect(_on_target_changed)
	GameState.respawned.connect(func() -> void: prediction.snap_next = true)
	_hud.respawn_requested.connect(func() -> void: Net.send("Respawn"))
	_hud.hotbar_pressed.connect(_use_slot)
	_hud.in_range_check = _spell_in_range
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
	if GameState.is_dead:
		dx = 0
		dy = 0
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
		if not _aiming_spell.is_empty():
			var mouse := get_global_mouse_position()
			_reticle.aim_pos = mouse
			var tolerance := float(Content.rule("combat", "castRangeToleranceTiles", 0.0))
			_reticle.aim_in_range = mouse.distance_to(_player.position) <= (float(_aiming_spell.get("range", 0)) + tolerance) * 16.0
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
	if Time.get_ticks_msec() < _forced_move_until_ms:
		prediction.smooth_large_corrections = true  # salto/Carga: ~100 ms de suavizado en vez de snap (ADR-016)
	prediction.reconcile(
		Vector2(float(self_state.get("x", prediction.position.x)), float(self_state.get("y", prediction.position.y))),
		ack, float(self_state.get("speed", prediction.speed_tiles_per_sec)))
	prediction.smooth_large_corrections = false
	_overlay.ack_seq = ack
	var now := float(Time.get_ticks_msec())
	for e: Variant in d.get("ents", []):
		var ed: Dictionary = e
		var id := int(ed.get("id", -1))
		if _remotes.has(id):
			var r: RemoteEntity = _remotes[id]
			r.apply_state(ed, now)
			if id == GameState.target_id:
				_hud.set_target_info(r.display_name, r.level, r.hp_pct)


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


# --- Objetivo y combate (HU-030, HU-032, HU-033, HU-086) -------------------------------------------------------------

func _unhandled_input(event: InputEvent) -> void:
	if not in_world:
		return
	if event is InputEventMouseButton and event.is_pressed():
		var mb := event as InputEventMouseButton
		var world_pos := get_global_mouse_position()
		if mb.button_index == MOUSE_BUTTON_LEFT:
			if not _aiming_spell.is_empty():
				_cast_ground(_aiming_spell, world_pos)
				_stop_aiming()
				return
			var hit := _entity_at(world_pos)
			if hit != null:
				_select(hit.entity_id)
			else:
				_select(-1)  # clic en el suelo: deseleccionar (CA3)
		elif mb.button_index == MOUSE_BUTTON_RIGHT:
			if not _aiming_spell.is_empty():
				_stop_aiming()
				return
			var hit := _entity_at(world_pos)
			if hit != null and hit.hostile:
				_select(hit.entity_id)
				Net.send("AutoAttack", {"on": true})  # HU-032 CA1
	elif event.is_action_pressed("ui_cancel"):
		if not _aiming_spell.is_empty():
			_stop_aiming()
		elif not GameState.own_cast.is_empty():
			Net.send("CancelCast")
		else:
			_select(-1)
	elif event.is_action_pressed("target_next"):
		_cycle_target()
	else:
		for i: int in 8:
			var action := "spell_%d" % (i + 1) if i < 4 else "usable_%d" % (i - 3)
			if InputMap.has_action(action) and event.is_action_pressed(action):
				_use_slot(i)
				return


func _entity_at(world_pos: Vector2) -> RemoteEntity:
	var best: RemoteEntity = null
	var best_d := 12.0
	for r: RemoteEntity in _remotes.values():
		var d := r.position.distance_to(world_pos + Vector2(0, 4))
		if d < best_d:
			best_d = d
			best = r
	return best


func _select(entity_id: int) -> void:
	GameState.set_target(entity_id)
	Net.send("SelectTarget", {"targetId": entity_id} if entity_id > 0 else {})


func _on_target_changed(entity_id: int) -> void:
	for r: RemoteEntity in _remotes.values():
		r.selected = r.entity_id == entity_id
		if r.selected:
			_hud.set_target_info(r.display_name, r.level, r.hp_pct)


## Tab: enemigos vivos visibles a ≤ 12 casillas, del más cercano al más lejano (HU-030 CA2).
func _cycle_target() -> void:
	var candidates: Array[RemoteEntity] = []
	var max_px := TARGET_CYCLE_RANGE_TILES * 16.0
	for r: RemoteEntity in _remotes.values():
		if r.hostile and r.anim != "dead" and r.hp_pct > 0 and r.position.distance_to(_player.position) <= max_px:
			candidates.append(r)
	if candidates.is_empty():
		return
	candidates.sort_custom(func(a: RemoteEntity, b: RemoteEntity) -> bool: return a.position.distance_squared_to(_player.position) < b.position.distance_squared_to(_player.position))
	var idx := -1
	for i: int in candidates.size():
		if candidates[i].entity_id == GameState.target_id:
			idx = i
			break
	_select(candidates[(idx + 1) % candidates.size()].entity_id)


func _slot_entry(slot: int) -> Dictionary:
	for h: Variant in GameState.hotbar:
		var hd: Dictionary = h
		if int(hd.get("slot", -1)) == slot:
			return hd
	return {}


func _use_slot(slot: int) -> void:
	var entry := _slot_entry(slot)
	if entry.is_empty():
		return
	if str(entry.get("kind", "spell")) != "spell":
		Net.send("UseItem", {"itemId": str(entry.get("ref", ""))})  # HU-055
		return
	var spell := Content.spell(str(entry.get("ref", "")))
	if spell.is_empty():
		return
	var targeting := str(spell.get("targeting", "enemy"))
	var has_leap := false
	for e: Variant in spell.get("effects", []):
		if str((e as Dictionary).get("type", "")) == "leap":
			has_leap = true
	if targeting.begins_with("ground_") or has_leap:
		_start_aiming(spell)
		return
	var payload := {"spellId": str(spell["id"]), "reqId": Net.next_req_id()}
	if GameState.target_id > 0:
		payload["targetId"] = GameState.target_id
	Net.send("CastSpell", payload)
	GameState.predict_gcd(str(spell["id"]))


func _start_aiming(spell: Dictionary) -> void:
	_aiming_spell = spell
	_reticle.aiming = true
	_reticle.aim_radius_px = float(spell.get("aoeRadius", 0.5)) * 16.0


func _stop_aiming() -> void:
	_aiming_spell = {}
	_reticle.aiming = false


func _cast_ground(spell: Dictionary, world_pos: Vector2) -> void:
	var payload := {"spellId": str(spell["id"]), "targetPos": {"x": snappedf(world_pos.x, 0.01), "y": snappedf(world_pos.y, 0.01)}, "reqId": Net.next_req_id()}
	if GameState.target_id > 0:
		payload["targetId"] = GameState.target_id
	Net.send("CastSpell", payload)
	GameState.predict_gcd(str(spell["id"]))
	for e: Variant in spell.get("effects", []):
		if str((e as Dictionary).get("type", "")) == "leap":
			_forced_move_until_ms = Time.get_ticks_msec() + 500


## Alcance solo visual para oscurecer la casilla (HU-038 CA3).
func _spell_in_range(spell: Dictionary) -> bool:
	var targeting := str(spell.get("targeting", "enemy"))
	if targeting != "enemy" or GameState.target_id <= 0 or not _remotes.has(GameState.target_id):
		return true
	var r: RemoteEntity = _remotes[GameState.target_id]
	return r.position.distance_to(_player.position) <= float(spell.get("range", 0)) * 16.0


func _on_message(type: String, d: Dictionary) -> void:
	match type:
		"CastStarted":
			var caster := int(d.get("casterId", -1))
			var spell := Content.spell(str(d.get("spellId", "")))
			if caster == GameState.self_id:
				for e: Variant in spell.get("effects", []):
					if str((e as Dictionary).get("type", "")) in ["leap", "dash"]:
						_forced_move_until_ms = Time.get_ticks_msec() + 500
			elif _remotes.has(caster):
				(_remotes[caster] as RemoteEntity).begin_cast(int(d.get("durationMs", 0)))
			if d.get("targetPos") != null and int(d.get("durationMs", 0)) > 0:
				var tp: Dictionary = d["targetPos"]
				var radius: float = float(d["radius"]) if d.get("radius") != null else float(spell.get("aoeRadius", 1.0))
				var enemy: bool = _remotes.has(caster) and (_remotes[caster] as RemoteEntity).hostile
				_reticle.set_mark(caster, Vector2(float(tp.get("x", 0)), float(tp.get("y", 0))), radius * 16.0, enemy)
		"CastEnded":
			var caster := int(d.get("casterId", -1))
			_reticle.clear_mark(caster)
			if caster == GameState.self_id:
				_hud.show_cast_result(str(d.get("result", "")), str(d.get("reason", "")))
			elif _remotes.has(caster):
				(_remotes[caster] as RemoteEntity).end_cast(str(d.get("result", "")))
		"Error":
			var code := str(d.get("code", ""))
			if code in ["on_cooldown", "on_gcd", "not_enough_resource", "out_of_range", "no_los", "invalid_target", "stunned", "silenced", "rooted", "locked_out", "is_dead", "area_limit"]:
				GameState.revert_prediction("")
		_:
			pass


## Lote del tick (ADR-018): un número por entrada sobre la entidad destino.
func _on_combat_events(d: Dictionary) -> void:
	for e: Variant in d.get("e", []):
		var ed: Dictionary = e
		var dst := int(ed.get("dst", -1))
		var pos := _player.position if dst == GameState.self_id else (_remotes[dst] as RemoteEntity).position if _remotes.has(dst) else Vector2.INF
		if pos == Vector2.INF:
			continue
		_floating.show_event(dst, str(ed.get("kind", "")), int(ed.get("amount", 0)), bool(ed.get("crit", false)), pos)


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
	_reticle.clear_all()
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
