extends Node2D
## Escena del mundo (HU-014, HU-020..HU-024): conecta con el ticket, envía Hello, carga el mapa del Welcome, mueve al
## jugador con predicción + reconciliación (un MoveInput por tick de 50 ms al moverse, 0,0 al soltar), dibuja las entidades de la
## AOI con interpolación y muestra el nombre de la zona al entrar. El cliente nunca decide resultados: solo refleja.

const ZONE_FADE_SEC := 2.0

@onready var _ground: TerrainRenderer = %Ground
@onready var _above: TerrainRenderer = %Above
@onready var _entities: Node2D = %Entities
@onready var _player: Node2D = %PlayerSelf
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
## Sprite, sombra y placa del personaje propio (las remotas las lleva RemoteEntity).
var _self_visual: EntityVisual
## Barra de vida de la placa propia.
var _self_health: HealthBar
var in_world: bool = false

var _remotes: Dictionary = {}  # id → RemoteEntity
var _current_zone: String = ""
var _zone_fade_left: float = 0.0
var _zone_group: CanvasGroup
var _character_id: String = ""
## ¿Ya llegó el Welcome de esta conexión? Uno posterior no es una entrada nueva al mundo (HU-044).
var _welcomed_on_connection: bool = false
var _status_clear_at: int = -1
## Apuntado de área (HU-086 CA1): hechizo en curso de apuntar o vacío.
var _aiming_spell: Dictionary = {}
## Último CastStarted propio de un salto/Carga: la corrección grande siguiente se suaviza en vez de saltar (HU-087 CA4).
var _forced_move_until_ms: int = -1
const TARGET_CYCLE_RANGE_TILES := 12.0
## Índice de la mano principal en `equipment` (EquipSlot del protocolo).
const MAIN_HAND_SLOT := 7
## Hacia dónde mira el personaje propio (última tecla de movimiento).
var _self_dir: String = "s"
## Animaciones y efectos de combate (HU-090, HU-091).
var _presenter: CombatPresenter
var _vfx: VfxLayer
## Menú de Esc (HU-015) y qué hacer cuando el servidor confirme el Logout ("select" | "quit"; vacío = no se pidió).
var _game_menu: GameMenu
var _logout_after: String = ""
var _logout_req_id: int = -1
const CHARACTER_SELECT_SCENE := "res://scenes/character_select/character_select.tscn"
const LOGOUT_IN_COMBAT_TEXT := "No puedes salir en combate"
## Acercarse solo al objetivo fuera de alcance (HU-095) y círculo del alcance al mantener la tecla (HU-096).
var _approach: Approach = Approach.new()
var _range_ring: RangeRing
## Palancas y puertas del mapa (HU-083).
var _map_objects: MapObjectsLayer
const STUCK_TEXT := "No puedes llegar hasta el objetivo"
## Envío de mensajes (los tests lo sustituyen para ver qué se manda sin servidor).
var send_fn: Callable = func(type: String, data: Dictionary) -> void: Net.send(type, data)
const LOGOUT_REJECTED_TEXT := "No se pudo salir: el servidor rechazó la petición"
## Cambio de escena tras el Logout (los tests lo sustituyen para no salir de la escena de prueba).
var change_scene: Callable = func(path: String) -> void: get_tree().change_scene_to_file(path)


func _ready() -> void:
	Net.register_handler("Welcome", _on_welcome)
	Net.register_handler("EntitySpawn", _on_entity_spawn)
	Net.register_handler("EntityDespawn", _on_entity_despawn)
	Net.register_handler("ChangeMap", _on_change_map)
	Net.register_handler("LoggedOut", _on_logged_out)
	Net.message_received.connect(_on_message)
	Net.combat_events.connect(_on_combat_events)
	Net.snapshot.connect(_on_snapshot)
	GameState.target_changed.connect(_on_target_changed)
	GameState.auras_changed.connect(_on_auras_changed)
	GameState.respawned.connect(func() -> void: prediction.snap_next = true)
	GameState.died.connect(func(_killer: int) -> void:
		_stop_aiming()
		_approach.cancel())
	GameState.xp_gained.connect(func(amount: int) -> void: _floating.show_event(GameState.self_id, "xp", amount, false, _player.position))
	_hud.respawn_requested.connect(func() -> void: _send("Respawn"))
	_hud.hotbar_pressed.connect(_use_slot)
	_hud.in_range_check = _spell_in_range
	_inventory.sell_requested.connect(_sell_item)
	_drop_catcher.item_dropped_outside.connect(_on_item_dropped_outside)
	_drop_catcher.hotbar_slot_dropped_outside.connect(func(slot: int) -> void: _hud._assign_slot(slot, "", ""))
	_chat.bubble_requested.connect(_show_bubble)
	_chat.command.connect(_on_chat_command)
	_social.party_member_selected.connect(_select)
	_map_objects = MapObjectsLayer.new()
	_map_objects.z_index = -6  # sobre el suelo, bajo la retícula y las entidades
	add_child(_map_objects)
	GameState.map_objects_changed.connect(_apply_map_objects)
	_social.whisper_requested.connect(func(player_name: String) -> void:
		_chat._input.text = "/w %s " % player_name
		_chat._input.grab_focus())
	_social.entity_name = func(entity_id: int) -> String: return (_remotes[entity_id] as RemoteEntity).display_name if _remotes.has(entity_id) else ""
	_inventory.offer_requested.connect(_social.offer_item)
	GameState.duel_changed.connect(_on_duel_changed)
	_vendor.sell_junk_requested.connect(_inventory.sell_junk)
	Net.disconnected.connect(_on_disconnected)
	EventBus.ui_error.connect(_on_ui_error)
	_self_visual = EntityVisual.new()
	_player.add_child(_self_visual)
	_self_health = _self_visual.plate.health_bar
	_range_ring = RangeRing.new()
	_range_ring.name = "RangeRing"
	_player.add_child(_range_ring)
	_setup_combat_presenter()
	_setup_game_menu()
	GameState.vitals_changed.connect(_refresh_self_health)
	_player.visible = false
	# El rótulo de zona se desvanece dentro de un CanvasGroup: así el contorno y la letra se funden juntos (sin dobles).
	_zone_group = CanvasGroup.new()
	_zone_label.get_parent().add_child(_zone_group)
	_zone_label.get_parent().move_child(_zone_group, 1)  # debajo de las ventanas
	_zone_label.reparent(_zone_group)
	_zone_group.self_modulate.a = 0.0
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
	_send("Hello", {"protocolVersion": Protocol.VERSION, "ticket": Net.current_ticket()})


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
		_refresh_self_visual()
		if GameState.target_id > 0:
			_send("SelectTarget", {"targetId": GameState.target_id})  # el servidor limpia el objetivo al cambiar de clase
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
	_refresh_self_visual()
	_player.visible = true
	_camera.position = Vector2.ZERO
	_camera.reset_smoothing()
	in_world = true


## Sprite de la clase y nombre del personaje propio (Welcome y cambio de clase).
func _refresh_self_visual() -> void:
	_self_visual.set_sprite(EntitySprites.ref_for("player", GameState.class_id, GameState.class_id), RemoteEntity.PLAYER_COLOR, GameState.character_name)
	_self_visual.plate.display_name = GameState.character_name
	_self_visual.plate.name_color = UiTheme.ACCENT


## Capas de efectos: los brillos de casteo bajo los cuerpos (encima de la marca de área) y el resto por encima de las
## entidades y los tejados, pero por debajo de las placas (z 40), los números (FloatingText) y la interfaz (CanvasLayer).
func _setup_combat_presenter() -> void:
	var vfx_ground := Node2D.new()
	vfx_ground.name = "VfxGround"
	vfx_ground.z_index = -4
	add_child(vfx_ground)
	_vfx = VfxLayer.new()
	_vfx.name = "Vfx"
	_vfx.z_index = 12
	_vfx.ground = vfx_ground
	add_child(_vfx)
	_presenter = CombatPresenter.new()
	_presenter.name = "CombatPresenter"
	_presenter.vfx = _vfx
	_presenter.floating = _floating
	_presenter.entity_pos = _entity_feet
	_presenter.visual_of = _entity_visual
	_presenter.archetype_of = _entity_archetype
	add_child(_presenter)


## Menú de Esc encima de las ventanas del HUD (los avisos van en una capa aún más alta, por encima del velo).
func _setup_game_menu() -> void:
	_game_menu = GameMenu.new()
	_game_menu.name = "GameMenu"
	_hud.get_parent().add_child(_game_menu)
	_game_menu.resume_requested.connect(_game_menu.close)
	_game_menu.character_select_requested.connect(request_logout.bind("select"))
	_game_menu.quit_requested.connect(request_logout.bind("quit"))
	_hud.menu_requested.connect(_toggle_game_menu)


func _toggle_game_menu() -> void:
	if _game_menu.is_open():
		_game_menu.close()
	else:
		_open_game_menu()


func _open_game_menu() -> void:
	_stop_aiming()
	_game_menu.open()


## HU-015: pide `Logout`; la escena cambia (o el juego se cierra) cuando llega `LoggedOut`. Sin conexión no hay nada que
## guardar en el servidor: se sale directamente.
func request_logout(after: String) -> void:
	if not Net.is_connected:
		_finish_logout(after)
		return
	_logout_after = after
	_logout_req_id = Net.next_req_id()
	_game_menu.waiting = true
	_send("Logout", {"reqId": _logout_req_id})


func _on_logged_out(_d: Dictionary) -> void:
	_finish_logout(_logout_after if not _logout_after.is_empty() else "select")


func _finish_logout(after: String) -> void:
	_logout_after = ""
	in_world = false
	Net.disconnect_from_server()  # sin reconexión: el servidor ya nos sacó
	Net.ticket_refresher = Callable()
	_presenter.clear()
	GameState.reset()
	if after == "quit":
		get_tree().quit()
		return
	change_scene.call(CHARACTER_SELECT_SCENE)  # el token sigue en Api: la lista se carga sin volver a entrar


func _entity_feet(id: int) -> Vector2:
	if id == GameState.self_id and _player.visible:
		return _player.position
	var r: RemoteEntity = _remotes.get(id)
	return Vector2.INF if r == null else r.position


func _entity_visual(id: int) -> EntityVisual:
	if id == GameState.self_id:
		return _self_visual
	var r: RemoteEntity = _remotes.get(id)
	return null if r == null else r.visual


func _entity_archetype(id: int) -> String:
	if id == GameState.self_id:
		return GameState.class_id
	var r: RemoteEntity = _remotes.get(id)
	return "" if r == null else (r.class_id if r.kind == "player" else r.template_id)


func _refresh_self_health() -> void:
	_self_health.pct = roundi(100.0 * GameState.hp / maxf(1.0, GameState.max_hp))
	var show := not GameState.is_dead
	if _self_health.visible != show:
		_self_health.visible = show
		_self_visual.plate.refresh_layout()
	_self_visual.set_dead(GameState.is_dead)


func _load_map(map_id: String) -> void:
	map = TmjMap.load_from("res://maps/%s.tmj" % map_id)
	if map == null:
		_hud_status.text = "Falta client/maps/%s.tmj (tools/sync_content.gd)" % map_id
		return
	_ground.setup(map, ["ground", "detail", "walls"])
	_above.setup(map, ["above"])
	_map_objects.setup(map)
	_apply_map_objects()
	var ts := map.tile_size
	_camera.limit_left = 0
	_camera.limit_top = 0
	_camera.limit_right = map.width * ts
	_camera.limit_bottom = map.height * ts


## HU-083: las puertas abiertas dejan de colisionar también en la predicción (la rejilla es la misma que usa `prediction`).
func _apply_map_objects() -> void:
	if map == null:
		return
	for d: Dictionary in map.doors:
		map.set_door_open(str(d["id"]), str(GameState.map_objects.get(d["id"], "closed")) == "open")
	_map_objects.set_states(GameState.map_objects)


# --- Movimiento propio (HU-021 CA1, HU-022) -------------------------------------------------------------------------

func _physics_process(delta: float) -> void:
	if not in_world or not Net.is_connected:
		return
	var dx := int(Input.is_action_pressed("move_right")) - int(Input.is_action_pressed("move_left"))
	var dy := int(Input.is_action_pressed("move_down")) - int(Input.is_action_pressed("move_up"))
	if GameState.is_dead or _chat.is_typing() or _game_menu.is_open():
		dx = 0
		dy = 0
	if dx != 0 or dy != 0 or GameState.is_dead:
		_approach.cancel()  # moverse a mano cancela el acercamiento (HU-095 CA3)
	elif _approach.is_active() and not _game_menu.is_open():
		var step := _step_approach()
		dx = step.x
		dy = step.y
	# Ticks fijos de 50 ms como el servidor (los frames físicos van a 60 Hz): un MoveInput por tick con movimiento.
	for input: Dictionary in _movement.advance(delta, dx, dy, prediction):
		_send("MoveInput", input)
	if dx != 0 or dy != 0:
		_self_dir = ("e" if dx > 0 else "w") if dx != 0 else ("s" if dy > 0 else "n")
	_self_visual.set_motion(_self_dir, dx != 0 or dy != 0)


func _process(delta: float) -> void:
	if in_world:
		prediction.update_render(delta)
		_player.position = prediction.render_position
		_update_zone(delta)
		_update_range_ring()
		if not _aiming_spell.is_empty():
			var mouse := get_global_mouse_position()
			_reticle.aim_pos = mouse
			_update_area_preview(mouse)
			var tolerance := float(Content.rule("combat", "castRangeToleranceTiles", 0.0))
			_reticle.aim_in_range = mouse.distance_to(_player.position) <= (float(_aiming_spell.get("range", 0)) + tolerance) * 16.0
		_vfx.view_rect = _view_rect()
		_overlay.pending_inputs = prediction.pending.size()
		_overlay.reconcile_error_px = prediction.last_error_px
		_layout_nameplates()
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
				_hud.set_target_info(r.display_name, r.level, r.hp_pct, r.portrait())


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
	if id == GameState.duel_opponent_id and GameState.duel_state == "active":
		r.hostile = true  # vuelve a entrar en la AOI en pleno duelo
	if r.visual != null:
		r.visual.set_auras(GameState.auras_of(id))


## Estados sobre cualquier entidad visible, no solo en los marcos (HU-098 CA2–CA4).
func _on_auras_changed(entity_id: int) -> void:
	var v := _entity_visual(entity_id)
	if v != null:
		v.set_auras(GameState.auras_of(entity_id))


func _on_entity_despawn(d: Dictionary) -> void:
	var id := int(d.get("id", -1))
	_reticle.clear_mark(id)  # su CastEnded ya no llegará
	_presenter.forget(id)
	if _remotes.has(id):
		var r: RemoteEntity = _remotes[id]
		_remotes.erase(id)
		r.queue_free()


func _clear_remotes() -> void:
	for r: RemoteEntity in _remotes.values():
		r.queue_free()
	_remotes.clear()
	_presenter.clear()


# --- Objetivo y combate (HU-030, HU-032, HU-033, HU-086) -------------------------------------------------------------

func _unhandled_input(event: InputEvent) -> void:
	if not in_world or _chat.is_typing() or _game_menu.is_open():
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
					_send("LootOpen", {"lootId": hit.entity_id})  # HU-050 CA2
				elif hit.kind == "npc" and hit.template_id == "vendor":
					_send("VendorOpen", {"npcId": hit.entity_id})  # HU-055 CA1
				elif hit.kind == "npc" and hit.template_id == "class_change":
					_social.open_class_change(hit.entity_id)  # HU-044 CA1
			elif not _map_objects.lever_at(world_pos).is_empty():
				_send("Interact", {"objectId": _map_objects.lever_at(world_pos)})  # HU-083: el servidor valida distancia y vida
			else:
				_select(-1)  # clic en el suelo: deseleccionar (CA3)
		elif mb.button_index == MOUSE_BUTTON_RIGHT:
			if not _aiming_spell.is_empty():
				_stop_aiming()
				return
			var hit := _entity_at(world_pos)
			if hit != null and hit.hostile:
				_select(hit.entity_id)  # HU-094: el básico va con Espacio; el clic derecho solo selecciona
			elif hit != null and hit.kind == "player":
				_select(hit.entity_id)
				_open_player_menu(hit)
	elif event.is_action_pressed("toggle_inventory"):
		_inventory.toggle()
	elif event.is_action_pressed("toggle_spellbook"):
		_spellbook.toggle()
	elif event.is_action_pressed("toggle_character"):
		_character.toggle()
	elif event.is_action_pressed("toggle_online_list"):
		_social.toggle_online_list()  # HU-063 CA1: tecla O
	elif event.is_action_pressed("basic_attack"):
		basic_attack()
	elif event.is_action_pressed("ui_cancel"):
		if _approach.is_active():
			_approach.cancel()
		elif not _aiming_spell.is_empty():
			_stop_aiming()
		elif _loot.visible or _vendor.visible or _inventory.visible or _spellbook.visible or _character.visible or _social.is_online_list_open():
			_social.close_online_list()
			_loot.visible = false
			_vendor.close_window()
			_inventory.visible = false
			_inventory.vendor_mode = false
			_spellbook.visible = false
			_character.visible = false
		elif not GameState.own_cast.is_empty():
			_send("CancelCast")
		elif GameState.target_id > 0:
			_select(-1)
		else:
			_open_game_menu()  # HU-015: Esc sin nada que cerrar ni cancelar abre el menú
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
	_send("SelectTarget", {"targetId": entity_id} if entity_id > 0 else {})


func _on_target_changed(entity_id: int) -> void:
	if _approach.is_active() and _approach.target_id != entity_id:
		_approach.cancel()
	for r: RemoteEntity in _remotes.values():
		r.selected = r.entity_id == entity_id
		if r.selected:
			_hud.set_target_info(r.display_name, r.level, r.hp_pct, r.portrait())


## Enemigos vivos visibles a ≤ 12 casillas, del más cercano al más lejano.
func _nearby_hostiles() -> Array[RemoteEntity]:
	var candidates: Array[RemoteEntity] = []
	var max_px := TARGET_CYCLE_RANGE_TILES * 16.0
	for r: RemoteEntity in _remotes.values():
		if _is_live_hostile(r) and r.position.distance_to(_player.position) <= max_px:
			candidates.append(r)
	candidates.sort_custom(func(a: RemoteEntity, b: RemoteEntity) -> bool: return a.position.distance_squared_to(_player.position) < b.position.distance_squared_to(_player.position))
	return candidates


static func _is_live_hostile(r: RemoteEntity) -> bool:
	return r != null and r.hostile and r.anim != "dead" and r.hp_pct > 0


## Tab: del más cercano al más lejano (HU-030 CA2).
func _cycle_target() -> void:
	var candidates := _nearby_hostiles()
	if candidates.is_empty():
		return
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
	if _game_menu.is_open():
		return
	var entry := _slot_entry(slot)
	if entry.is_empty():
		return
	_stop_aiming()  # otra casilla sustituye al apuntado en curso (si es otra área, vuelve a apuntar abajo)
	if str(entry.get("kind", "spell")) != "spell":
		var payload := _use_item_payload(str(entry.get("ref", "")))
		if not payload.is_empty():
			_send("UseItem", payload)  # HU-055
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
	var target: RemoteEntity = _remotes.get(GameState.target_id)
	if targeting == "enemy" and _is_live_hostile(target) and not Approach.in_reach(prediction.position, target.position, _spell_reach_px(spell)):
		_approach.start(target.entity_id, _spell_reach_px(spell), Time.get_ticks_msec(), _cast_targeted.bind(spell))  # HU-095 CA2
		return
	_approach.cancel()
	_cast_targeted(spell)


func _cast_targeted(spell: Dictionary) -> void:
	var payload := {"spellId": str(spell["id"]), "reqId": Net.next_req_id()}
	if GameState.target_id > 0:
		payload["targetId"] = GameState.target_id
	_send("CastSpell", payload)
	GameState.predict_gcd(str(spell["id"]))


func _send(type: String, data: Dictionary = {}) -> void:
	send_fn.call(type, data)


# --- Básico con Espacio, acercamiento y alcance (HU-094, HU-095, HU-096) ------------------------------------------------

## Espacio: ataca al objetivo hostil o, sin él, al enemigo vivo más cercano; si está lejos, camina hasta él.
func basic_attack() -> void:
	if GameState.is_dead or _game_menu.is_open():
		return
	var target: RemoteEntity = _remotes.get(GameState.target_id)
	if not _is_live_hostile(target):
		var near := _nearby_hostiles()
		if near.is_empty():
			return
		target = near[0]
		_select(target.entity_id)
	_send("AutoAttack", {"on": true})  # el servidor pausa el básico fuera de alcance y lo reanuda al llegar
	var reach := basic_reach_px()
	if reach > 0.0 and not Approach.in_reach(prediction.position, target.position, reach):
		_approach.start(target.entity_id, reach, Time.get_ticks_msec())


## Alcance del básico del arma equipada (`rules.weapons.types[tipo].rangeTiles`) en píxeles; 0 sin arma.
func basic_reach_px() -> float:
	var main_hand: Variant = GameState.equipment[MAIN_HAND_SLOT] if GameState.equipment.size() > MAIN_HAND_SLOT else null
	if not main_hand is Dictionary:
		return 0.0
	var weapon_type := str(Content.item(str((main_hand as Dictionary).get("templateId", ""))).get("weaponType", ""))
	var types: Dictionary = Content.rule("weapons", "types", {})
	return float((types.get(weapon_type, {}) as Dictionary).get("rangeTiles", 0.0)) * 16.0


static func _spell_reach_px(spell: Dictionary) -> float:
	return float(spell.get("range", 0)) * 16.0


## Un tick de acercamiento: la dirección a caminar; al llegar lanza lo pendiente, y si se atasca avisa.
func _step_approach() -> Vector2i:
	var target: RemoteEntity = _remotes.get(_approach.target_id)
	if not _is_live_hostile(target):
		_approach.cancel()
		return Vector2i.ZERO
	var dir := _approach.update(prediction.position, target.position, Time.get_ticks_msec())
	match _approach.state:
		Approach.State.ARRIVED:
			var arrive := _approach.on_arrive
			_approach.cancel()
			if arrive.is_valid():
				arrive.call()
		Approach.State.STUCK:
			_approach.cancel()
			_hud.show_error(STUCK_TEXT)
	return dir


## Mientras se mantiene Espacio o 1–4: círculo del alcance (verde dentro, rojo fuera, crema sin objetivo).
func _update_range_ring() -> void:
	var reach := 0.0
	var enemy_only := true
	if not _chat.is_typing() and not _game_menu.is_open() and not GameState.is_dead:
		if Input.is_action_pressed("basic_attack"):
			reach = basic_reach_px()
		else:
			for i: int in 4:
				if Input.is_action_pressed("spell_%d" % (i + 1)):
					var spell := Content.spell(str(_slot_entry(i).get("ref", "")))
					reach = _spell_reach_px(spell)
					enemy_only = str(spell.get("targeting", "enemy")) == "enemy"
					break
	if reach <= 0.0:
		_range_ring.hide_range()
		return
	var target: RemoteEntity = _remotes.get(GameState.target_id)
	var state := -1
	if enemy_only and _is_live_hostile(target):
		state = 1 if prediction.position.distance_to(target.position) <= reach else 0
	_range_ring.show_range(reach, state)


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
	_send("CastSpell", payload)
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
				if int(d.get("durationMs", 0)) > 0:
					prediction.set_casting(true, float(Content.rule("combat", "castMoveSpeedMult", 0.5)))
			elif _remotes.has(caster):
				(_remotes[caster] as RemoteEntity).begin_cast(int(d.get("durationMs", 0)))
			if d.get("targetPos") != null and int(d.get("durationMs", 0)) > 0:
				var tp: Dictionary = d["targetPos"]
				var radius: float = float(d["radius"]) if d.get("radius") != null else float(spell.get("aoeRadius", 1.0))
				var enemy: bool = _remotes.has(caster) and (_remotes[caster] as RemoteEntity).hostile
				_reticle.set_mark(caster, Vector2(float(tp.get("x", 0)), float(tp.get("y", 0))), radius * 16.0, enemy, int(d.get("durationMs", 0)))
			_presenter.cast_started(d)
		"CastEnded":
			var caster := int(d.get("casterId", -1))
			_reticle.clear_mark(caster)
			if caster == GameState.self_id:
				prediction.set_casting(false, float(Content.rule("combat", "castMoveSpeedMult", 0.5)))
				_hud.show_cast_result(str(d.get("result", "")), str(d.get("reason", "")))
			elif _remotes.has(caster):
				(_remotes[caster] as RemoteEntity).end_cast(str(d.get("result", "")))
			_presenter.cast_ended(d)
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
			if code in ["on_cooldown", "on_gcd", "not_enough_resource", "out_of_range", "no_los", "invalid_target", "stunned", "silenced", "rooted", "locked_out", "is_dead", "area_limit", "not_equipped"]:
				GameState.revert_prediction("")
		_:
			pass


## Lote del tick (ADR-018): número, golpe, ataque e impacto por entrada (CombatPresenter, HU-090/HU-091).
func _on_combat_events(d: Dictionary) -> void:
	_presenter.combat_events(d)


## Clic derecho sobre otro jugador: Invitar / Retar a duelo / Intercambiar (HU-061, HU-064, HU-059).
func _open_player_menu(target: RemoteEntity) -> void:
	var menu := PopupMenu.new()
	menu.add_item("Invitar al grupo", 0)
	menu.add_item("Retar a duelo", 1)
	menu.add_item("Intercambiar", 2)
	menu.add_item("Susurrar", 3)
	menu.id_pressed.connect(func(id: int) -> void:
		match id:
			0: _send("PartyInvite", {"name": target.display_name})
			1:
				_social.mark_outgoing("duel")
				_send("DuelRequest", {"name": target.display_name})
			2:
				_social.mark_outgoing("trade")
				_send("TradeRequest", {"name": target.display_name})
			3: _chat._input.text = "/w %s " % target.display_name; _chat._input.grab_focus()
		menu.queue_free())
	add_child(menu)
	menu.position = Vector2i(get_viewport().get_mouse_position())
	menu.popup()


func _on_chat_command(name: String, args: String) -> void:
	match name:
		"invite": _send("PartyInvite", {"name": args})
		"leave": _send("PartyLeave")
		"kick": _send("PartyKick", {"name": args})
		"duel":
			_social.mark_outgoing("duel")
			_send("DuelRequest", {"name": args})
		"rendirse": _send("DuelForfeit")
		"trade":
			_social.mark_outgoing("trade")
			_send("TradeRequest", {"name": args})
		"tp", "tpto", "spawn", "give", "level", "heal", "kill", "gold", "god", "debug", "announce":
			# HU-070: comandos de administrador; el servidor responde forbidden si la cuenta no lo es.
			_send("AdminCommand", {"text": ("/%s %s" % [name, args]).strip_edges()})
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
	var bubble := PanelContainer.new()
	bubble.add_theme_stylebox_override("panel", UiTheme.nine("tooltip", 4, 3, UiTheme.TOOLTIP_BG))
	var label := Label.new()
	label.text = text.substr(0, 60)
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.custom_minimum_size = Vector2(minf(100.0, 6.0 + 5.0 * label.text.length()), 0)
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	bubble.add_child(label)
	var holder := Node2D.new()
	holder.z_index = 60
	holder.z_as_relative = false
	holder.position = Vector2(0, -52)
	holder.add_child(bubble)
	anchor.add_child(holder)
	bubble.resized.connect(func() -> void: bubble.position = Vector2(-roundf(bubble.size.x / 2.0), -bubble.size.y))
	get_tree().create_timer(4.0).timeout.connect(holder.queue_free)


## HU-064: marco del rival en naranja durante el duelo.
func _on_duel_changed(state: String, opponent_id: int, _winner_id: int, _starts_in_ms: int) -> void:
	for r: RemoteEntity in _remotes.values():
		r.set_name_color(Color(1, 0.6, 0.2) if r.entity_id == opponent_id and state in ["countdown", "active"] else Color.WHITE)
		# En pleno duelo el rival es enemigo: Espacio lo ataca, el clic derecho no abre el menú, Tab lo selecciona, anillo rojo.
		if r.kind == "player":
			r.hostile = r.entity_id == opponent_id and state == "active"
			r.queue_redraw()


func _sell_item(item: Dictionary) -> void:
	if _vendor.npc_id <= 0:
		return
	_send("VendorSell", {"npcId": _vendor.npc_id, "itemId": str(item.get("id", "")), "qty": int(item.get("qty", 1))})


## Soltar un item de la bolsa fuera de cualquier casilla → confirmar destrucción (HU-056 CA2).
func _on_item_dropped_outside(item_id: String) -> void:
	var item := GameState.bag_item(item_id)
	if not item.is_empty():
		_inventory.request_destroy(item)


## Placas de nombre: las cercanas se apilan en vez de pisarse y ninguna se sale de lo que muestra la cámara.
func _layout_nameplates() -> void:
	var plates: Array[Dictionary] = []
	var visuals := {}
	if _player.visible:
		plates.append({"id": 0, "rect": _self_visual.plate_anchor_rect()})
		visuals[0] = _self_visual
	for r: RemoteEntity in _remotes.values():
		if r.visual != null and r.visual.plate != null:
			plates.append({"id": r.entity_id, "rect": r.visual.plate_anchor_rect()})
			visuals[r.entity_id] = r.visual
	# Sin meterse bajo los marcos de arriba ni la barra rápida (la interfaz va encima).
	var view := _view_rect()
	view = view.grow_individual(0, -CombatHud.FRAMES_BOTTOM, 0, -CombatHud.HOTBAR_HEIGHT - UiTheme.SCREEN_MARGIN)
	var offsets := NameplateLayout.solve(plates, view)
	# Las placas que caerían bajo los marcos de vida o la barra rápida se ocultan (asomaban por sus huecos).
	var full := _view_rect()
	var hud_rects := [
		Rect2(full.position, Vector2(2 * CombatHud.FRAME_WIDTH + 6 * UiTheme.SCREEN_MARGIN, CombatHud.FRAMES_BOTTOM)),
		Rect2(full.position + Vector2(0, full.size.y - CombatHud.HOTBAR_HEIGHT - UiTheme.SCREEN_MARGIN), Vector2(full.size.x, CombatHud.HOTBAR_HEIGHT + UiTheme.SCREEN_MARGIN)),
	]
	for p: Dictionary in plates:
		var v: EntityVisual = visuals[p["id"]]
		var moved := Rect2((p["rect"] as Rect2).position + (offsets[p["id"]] as Vector2), (p["rect"] as Rect2).size)
		v.set_plate_offset(offsets[p["id"]])
		var hidden := false
		for r: Rect2 in hud_rects:
			hidden = hidden or r.intersects(moved)
		v.plate.visible = not hidden


## Rectángulo del mundo que se ve en pantalla.
func _view_rect() -> Rect2:
	var inv := get_viewport().get_canvas_transform().affine_inverse()
	var vp := get_viewport().get_visible_rect()
	return Rect2(inv * vp.position, vp.size / get_viewport().get_canvas_transform().get_scale())


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
				_zone_group.self_modulate.a = 1.0
				_zone_fade_left = ZONE_FADE_SEC
	if _zone_fade_left > 0.0:
		_zone_fade_left -= delta
		_zone_group.self_modulate.a = clampf(_zone_fade_left / ZONE_FADE_SEC, 0.0, 1.0)


# --- Conexión -----------------------------------------------------------------------------------------------------------

func _on_disconnected(reason: String) -> void:
	in_world = false
	_hud_status.text = "Reconectando… (intento %d/%d)" % [Net.reconnect_attempt(), Net.MAX_ATTEMPTS] if Net.reconnect_attempt() > 0 else "Desconectado: %s" % reason


func _on_ui_error(code: String, req_id: int) -> void:
	if not _logout_after.is_empty() and (req_id == _logout_req_id or (req_id <= 0 and code == "invalid_payload")):
		# El Logout no salió: en combate, o un servidor que no lo entiende (invalid_payload sin reqId). El menú queda
		# abierto y usable para Continuar o volver a intentarlo.
		_logout_after = ""
		_game_menu.waiting = false
		_hud.show_error(LOGOUT_IN_COMBAT_TEXT if code == "in_combat" else LOGOUT_REJECTED_TEXT)
		return
	match code:
		"bad_version", "bad_ticket", "disconnected":
			get_tree().set_meta("login_notice", ApiMessages.text_for(code))
			get_tree().set_meta("login_notice_code", code)
			get_tree().change_scene_to_file("res://scenes/login/login.tscn")
		_:
			_hud_status.text = Net.last_error_message if not Net.last_error_message.is_empty() else ApiMessages.text_for(code)
			_status_clear_at = Time.get_ticks_msec() + 3000
