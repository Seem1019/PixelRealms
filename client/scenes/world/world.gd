extends Node2D
## Escena del mundo (HU-014, HU-020..HU-024): conecta con el ticket, envía Hello, carga el mapa del Welcome, mueve al
## jugador con predicción + reconciliación (un MoveInput por tick de 50 ms al moverse, 0,0 al soltar), dibuja las entidades de la
## AOI con interpolación y muestra el nombre de la zona al entrar. El cliente nunca decide resultados: solo refleja.

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
@onready var _inventory: InventoryWindow = %InventoryWindow
@onready var _loot: LootWindow = %LootWindow
@onready var _vendor: VendorWindow = %VendorWindow
@onready var _drop_catcher: DropCatcher = %DropCatcher
@onready var _chat: ChatPanel = %ChatPanel
@onready var _social: SocialPanels = %SocialPanels
@onready var _spellbook: SpellbookWindow = %SpellbookWindow
@onready var _character: CharacterPanel = %CharacterPanel

var map: TmjMap
var prediction: Prediction = Prediction.new()
var _movement: MovementDriver = MovementDriver.new()
## Barra de vida sobre el personaje propio (las remotas las lleva RemoteEntity).
var _self_health: HealthBar
var in_world: bool = false

var _remotes: Dictionary = {}  # id → RemoteEntity
var _current_zone: String = ""
var _zone_fade_left: float = 0.0
var _character_id: String = ""
## ¿Ya llegó el Welcome de esta conexión? Uno posterior no es una entrada nueva al mundo (HU-044).
var _welcomed_on_connection: bool = false
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
	GameState.died.connect(func(_killer: int) -> void: _stop_aiming())
	GameState.xp_gained.connect(func(amount: int) -> void: _floating.show_event(GameState.self_id, "xp", amount, false, _player.position))
	_hud.respawn_requested.connect(func() -> void: Net.send("Respawn"))
	_hud.hotbar_pressed.connect(_use_slot)
	_hud.in_range_check = _spell_in_range
	_inventory.sell_requested.connect(_sell_item)
	_drop_catcher.item_dropped_outside.connect(_on_item_dropped_outside)
	_drop_catcher.hotbar_slot_dropped_outside.connect(func(slot: int) -> void: _hud._assign_slot(slot, "", ""))
	_chat.bubble_requested.connect(_show_bubble)
	_chat.command.connect(_on_chat_command)
	_social.party_member_selected.connect(_select)
	_inventory.offer_requested.connect(_social.offer_item)
	GameState.duel_changed.connect(_on_duel_changed)
	_vendor.sell_junk_requested.connect(_inventory.sell_junk)
	Net.disconnected.connect(_on_disconnected)
	EventBus.ui_error.connect(_on_ui_error)
	var self_body := _player.get_node("Body") as ColorRect
	var body_rect := BodyShape.rect_px(Vector2.ZERO)
	self_body.position = body_rect.position
	self_body.size = body_rect.size
	_self_health = HealthBar.new()
	_self_health.position = Vector2(0, -14)
	_player.add_child(_self_health)
	GameState.vitals_changed.connect(_refresh_self_health)
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
	_movement.reset()  # el seq es por conexión: el servidor lo reinicia con cada Hello
	_welcomed_on_connection = false
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
	# Welcome reenviado en la misma conexión (cambio de clase): el mundo sigue igual y el servidor no reenvía la AOI.
	if _welcomed_on_connection and str(d.get("mapId", "")) == GameState.map_id:
		GameState._on_welcome(d, true)
		_player_name.text = GameState.character_name
		if GameState.target_id > 0:
			Net.send("SelectTarget", {"targetId": GameState.target_id})  # el servidor limpia el objetivo al cambiar de clase
		return
	_welcomed_on_connection = true
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


func _refresh_self_health() -> void:
	_self_health.pct = roundi(100.0 * GameState.hp / maxf(1.0, GameState.max_hp))
	_self_health.visible = not GameState.is_dead


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

func _physics_process(delta: float) -> void:
	if not in_world or not Net.is_connected:
		return
	var dx := int(Input.is_action_pressed("move_right")) - int(Input.is_action_pressed("move_left"))
	var dy := int(Input.is_action_pressed("move_down")) - int(Input.is_action_pressed("move_up"))
	if GameState.is_dead or _chat.is_typing():
		dx = 0
		dy = 0
	# Ticks fijos de 50 ms como el servidor (los frames físicos van a 60 Hz): un MoveInput por tick con movimiento.
	for input: Dictionary in _movement.advance(delta, dx, dy, prediction):
		Net.send("MoveInput", input)


func _process(delta: float) -> void:
	if in_world:
		prediction.update_render(delta)
		_player.position = prediction.render_position
		_update_zone(delta)
		if not _aiming_spell.is_empty():
			var mouse := get_global_mouse_position()
			_reticle.aim_pos = mouse
			_update_area_preview(mouse)
			var tolerance := float(Content.rule("combat", "castRangeToleranceTiles", 0.0))
			_reticle.aim_in_range = mouse.distance_to(_player.position) <= (float(_aiming_spell.get("range", 0)) + tolerance) * 16.0
		_overlay.pending_inputs = prediction.pending.size()
		_overlay.reconcile_error_px = prediction.last_error_px
	if _vendor.visible and _vendor.npc_id > 0:
		var range_tiles := float(Content.rule("economy", "vendorRangeTiles", 3.0))
		var npc: RemoteEntity = _remotes.get(_vendor.npc_id)
		if npc == null or npc.position.distance_to(_player.position) > range_tiles * 16.0:
			_vendor.close_window()
			_inventory.vendor_mode = false
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
	_reticle.clear_mark(id)  # su CastEnded ya no llegará
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
	if not in_world or _chat.is_typing():
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
				if hit.kind == "monster" and hit.anim == "dead":
					Net.send("LootOpen", {"lootId": hit.entity_id})  # HU-050 CA2
				elif hit.kind == "npc" and hit.template_id == "vendor":
					Net.send("VendorOpen", {"npcId": hit.entity_id})  # HU-055 CA1
				elif hit.kind == "npc" and hit.template_id == "class_change":
					_social.open_class_change(hit.entity_id)  # HU-044 CA1
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
			elif hit != null and hit.kind == "player":
				_select(hit.entity_id)
				_open_player_menu(hit)
	elif event.is_action_pressed("toggle_inventory"):
		_inventory.toggle()
	elif event.is_action_pressed("toggle_spellbook"):
		_spellbook.toggle()
	elif event.is_action_pressed("toggle_character"):
		_character.toggle()
	elif event.is_action_pressed("ui_cancel"):
		if not _aiming_spell.is_empty():
			_stop_aiming()
		elif _loot.visible or _vendor.visible or _inventory.visible:
			_loot.visible = false
			_vendor.close_window()
			_inventory.visible = false
			_inventory.vendor_mode = false
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
	_stop_aiming()  # otra casilla sustituye al apuntado en curso (si es otra área, vuelve a apuntar abajo)
	if str(entry.get("kind", "spell")) != "spell":
		var payload := _use_item_payload(str(entry.get("ref", "")))
		if not payload.is_empty():
			Net.send("UseItem", payload)  # HU-055
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


## La barra guarda la plantilla (SetHotbar.ref) pero UseItem pide el id de una instancia de la bolsa: se usa la primera pila.
## Vacío si no queda ninguna (la casilla ya se ve gris).
func _use_item_payload(template_id: String) -> Dictionary:
	var item := GameState.first_bag_item(template_id)
	if item.is_empty():
		return {}
	return {"itemId": str(item.get("id", "")), "reqId": Net.next_req_id()}


func _start_aiming(spell: Dictionary) -> void:
	_aiming_spell = spell
	_reticle.aiming = true
	_reticle.aim_radius_px = float(spell.get("aoeRadius", 0.5)) * 16.0


func _stop_aiming() -> void:
	_aiming_spell = {}
	_reticle.aiming = false
	for r: RemoteEntity in _remotes.values():
		r.area_hint = false


## Mientras se apunta: marca a quién alcanzaría el área en `center` con la misma cuenta que el servidor (solo visual: las
## posiciones de los demás llegan con ~100 ms de retraso).
func _update_area_preview(center: Vector2) -> void:
	var targeting := str(_aiming_spell.get("targeting", ""))
	var candidates: Array[Dictionary] = []
	for r: RemoteEntity in _remotes.values():
		var affected := r.hostile if targeting == "ground_aoe_enemies" else (not r.hostile if targeting == "ground_aoe_allies" else targeting == "ground_aoe_all")
		if affected and r.kind != "npc" and r.anim != "dead":
			candidates.append({"id": r.entity_id, "feet_px": r.position})
	var max_targets := mini(int(_aiming_spell.get("maxTargets", 99)), int(Content.rule("limits", "aoeMaxTargetsCap", 10)))
	var hits := BodyShape.area_hits(center, _reticle.aim_radius_px, max_targets, candidates, map.collision if map != null else null)
	for r: RemoteEntity in _remotes.values():
		r.area_hint = hits.has(r.entity_id)


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
				_reticle.set_mark(caster, Vector2(float(tp.get("x", 0)), float(tp.get("y", 0))), radius * 16.0, enemy, int(d.get("durationMs", 0)))
		"CastEnded":
			var caster := int(d.get("casterId", -1))
			_reticle.clear_mark(caster)
			if caster == GameState.self_id:
				_hud.show_cast_result(str(d.get("result", "")), str(d.get("reason", "")))
			elif _remotes.has(caster):
				(_remotes[caster] as RemoteEntity).end_cast(str(d.get("result", "")))
		"LootWindow":
			var names := {}
			for r: RemoteEntity in _remotes.values():
				names[r.entity_id] = r.display_name
			names[GameState.self_id] = GameState.character_name
			_loot.show_window(d, names)
		"VendorWindow":
			var npc_id := int(d.get("npcId", -1))
			var vendor_name: String = (_remotes[npc_id] as RemoteEntity).display_name if _remotes.has(npc_id) else "Vendedor"
			_vendor.show_window(d, vendor_name)
			_inventory.vendor_mode = true
			_inventory.visible = true
			_inventory.refresh()
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
		var kind := str(ed.get("kind", ""))
		_floating.show_event(dst, kind, int(ed.get("amount", 0)), bool(ed.get("crit", false)), pos)
		if kind in ["dmg", "heal"] and _remotes.has(dst):
			(_remotes[dst] as RemoteEntity).flash(Color(3, 3, 3) if kind == "dmg" else Color(0.6, 2.2, 0.6))  # destello por objetivo (área)


## Clic derecho sobre otro jugador: Invitar / Retar a duelo / Intercambiar (HU-061, HU-064, HU-059).
func _open_player_menu(target: RemoteEntity) -> void:
	var menu := PopupMenu.new()
	menu.add_item("Invitar al grupo", 0)
	menu.add_item("Retar a duelo", 1)
	menu.add_item("Intercambiar", 2)
	menu.add_item("Susurrar", 3)
	menu.id_pressed.connect(func(id: int) -> void:
		match id:
			0: Net.send("PartyInvite", {"name": target.display_name})
			1:
				_social.mark_outgoing("duel")
				Net.send("DuelRequest", {"name": target.display_name})
			2:
				_social.mark_outgoing("trade")
				Net.send("TradeRequest", {"name": target.display_name})
			3: _chat._input.text = "/w %s " % target.display_name; _chat._input.grab_focus()
		menu.queue_free())
	add_child(menu)
	menu.position = Vector2i(get_viewport().get_mouse_position())
	menu.popup()


func _on_chat_command(name: String, args: String) -> void:
	match name:
		"invite": Net.send("PartyInvite", {"name": args})
		"leave": Net.send("PartyLeave")
		"kick": Net.send("PartyKick", {"name": args})
		"duel":
			_social.mark_outgoing("duel")
			Net.send("DuelRequest", {"name": args})
		"rendirse": Net.send("DuelForfeit")
		"trade":
			_social.mark_outgoing("trade")
			Net.send("TradeRequest", {"name": args})
		"tp", "tpto", "spawn", "give", "level", "heal", "kill", "gold", "god", "debug", "announce":
			# HU-070: comandos de administrador; el servidor responde forbidden si la cuenta no lo es.
			Net.send("AdminCommand", {"text": ("/%s %s" % [name, args]).strip_edges()})
		_: GameState.notice.emit("Comando desconocido: /%s" % name)


## Burbuja de chat 4 s sobre la cabeza (HU-060 CA2).
func _show_bubble(from: String, text: String) -> void:
	var anchor: Node2D = _player if from == GameState.character_name else null
	if anchor == null:
		for r: RemoteEntity in _remotes.values():
			if r.display_name == from:
				anchor = r
				break
	if anchor == null:
		return
	var label := Label.new()
	label.text = text.substr(0, 60)
	label.add_theme_font_size_override("font_size", UiTheme.FONT_SMALL)
	label.position = Vector2(-40, -40)
	label.custom_minimum_size = Vector2(80, 10)
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.z_index = 60
	anchor.add_child(label)
	get_tree().create_timer(4.0).timeout.connect(label.queue_free)


## HU-064: marco del rival en naranja durante el duelo.
func _on_duel_changed(state: String, opponent_id: int, _winner_id: int, _starts_in_ms: int) -> void:
	for r: RemoteEntity in _remotes.values():
		r.set_name_color(Color(1, 0.6, 0.2) if r.entity_id == opponent_id and state in ["countdown", "active"] else Color.WHITE)


func _sell_item(item: Dictionary) -> void:
	if _vendor.npc_id <= 0:
		return
	Net.send("VendorSell", {"npcId": _vendor.npc_id, "itemId": str(item.get("id", "")), "qty": int(item.get("qty", 1))})


## Soltar un item de la bolsa fuera de cualquier casilla → confirmar destrucción (HU-056 CA2).
func _on_item_dropped_outside(item_id: String) -> void:
	var item := GameState.bag_item(item_id)
	if not item.is_empty():
		_inventory.request_destroy(item)


# --- Cambio de mapa (HU-027 CA2) ---------------------------------------------------------------------------------------

## Fundido a negro, carga del nuevo .tmj y recolocación del jugador; el HUD (misma escena) no se reinicia.
func _on_change_map(d: Dictionary) -> void:
	GameState._on_change_map(d)
	in_world = false
	_stop_aiming()
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
	_movement.forget_last_input()
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
			get_tree().set_meta("login_notice_code", code)
			get_tree().change_scene_to_file("res://scenes/login/login.tscn")
		_:
			_hud_status.text = Net.last_error_message if not Net.last_error_message.is_empty() else ApiMessages.text_for(code)
			_status_clear_at = Time.get_ticks_msec() + 3000
