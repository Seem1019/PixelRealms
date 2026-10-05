class_name SocialPanels
extends Control
## Marcos de grupo (HU-062 CA1/CA5), diálogos de invitación/duelo/intercambio (Aceptar/Rechazar), cuenta atrás y resultado del
## duelo (HU-064), ventana de intercambio (HU-059) y ventana de cambio de clase (HU-044). Todo construido por código.

signal party_member_selected(entity_id: int)
## HU-063 CA2: "Susurrar" en la lista de conectados (el mundo rellena el chat con "/w Nombre ").
signal whisper_requested(player_name: String)

var _frames: VBoxContainer
var _frame_buttons: Array[Button] = []
var _prompt: ConfirmationDialog
var _prompt_kind: String = ""
var _duel_label: Label
var _duel_until: float = 0.0
var _zone_warning: bool = false  # HU-101: el rótulo cuenta los segundos para volver a la zona
var _trade: TradeWindow
## Nombre de una entidad por id (lo rellena el mundo): para los avisos y la ventana de intercambio.
var entity_name: Callable = Callable():
	set(value):
		entity_name = value
		if _trade != null:
			_trade.entity_name = value
var _class_window: PanelContainer
## Ancho del diálogo del maestro de clases: cabe con margen en 480 px y deja leer la descripción en 2–3 líneas.
const CLASS_WINDOW_WIDTH := 250
var _class_npc_id: int = -1
## Solicitudes que envié yo (el protocolo no distingue quién pidió): no mostrar el diálogo de aceptar.
var _outgoing: Dictionary = {}  # "duel" | "trade" → ms de envío
var _online_window: PanelContainer
var _online_rows: VBoxContainer
## Ancho de la lista de conectados (HU-063): nombre, clase, nivel y zona en una línea.
const ONLINE_WINDOW_WIDTH := 230


func _ready() -> void:
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)  # tamaño de la pantalla: los hijos se centran respecto a él
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	_frames = VBoxContainer.new()
	_frames.position = Vector2(UiTheme.SCREEN_MARGIN, InventoryWindow.WINDOW_TOP)
	add_child(_frames)
	_prompt = ConfirmationDialog.new()
	_prompt.ok_button_text = "Aceptar"
	_prompt.cancel_button_text = "Rechazar"
	_prompt.confirmed.connect(func() -> void: _respond(true))
	_prompt.canceled.connect(func() -> void: _respond(false))
	add_child(_prompt)
	_duel_label = Label.new()
	_duel_label.custom_minimum_size = Vector2(240, 20)
	_duel_label.position = Vector2((UiTheme.base_size().x - 240) / 2.0, 90)
	_duel_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_duel_label.theme_type_variation = "HeadlineLabel"
	_duel_label.add_theme_color_override("font_color", Color(1, 0.6, 0.2))
	add_child(_duel_label)
	_build_trade()
	_build_class_window()
	_build_online_window()
	GameState.party_changed.connect(_refresh_party)
	GameState.party_invited.connect(_on_party_invited)
	GameState.duel_changed.connect(_on_duel)
	GameState.duel_zone_changed.connect(_on_duel_zone)
	GameState.trade_changed.connect(_on_trade)
	GameState.online_list_received.connect(_show_online_list)
	_refresh_party()


func _process(_delta: float) -> void:
	if _zone_warning:
		var left := maxi(0, GameState.duel_outside_until_ms - Time.get_ticks_msec())
		_duel_label.text = "¡Vuelve a la zona del duelo! %d" % ceili(left / 1000.0)
	elif _duel_until > 0.0 and Time.get_ticks_msec() / 1000.0 >= _duel_until:
		_duel_until = 0.0
		_duel_label.text = ""


## HU-101: fuera de la zona, el rótulo del duelo pasa a rojo y cuenta atrás; al volver se borra.
func _on_duel_zone() -> void:
	var outside := GameState.duel_outside_until_ms >= 0 and GameState.duel_state == "active"
	if outside == _zone_warning:
		return
	_zone_warning = outside
	_duel_label.add_theme_color_override("font_color", DuelZoneRing.OUTSIDE if outside else Color(1, 0.6, 0.2))
	if not outside:
		_duel_label.text = ""
		_duel_until = 0.0


func _unhandled_input(event: InputEvent) -> void:
	# HU-062 CA5: F1 = yo, F2–F5 = miembros 1–4.
	if event is InputEventKey and event.is_pressed() and not event.is_echo():
		var k := event as InputEventKey
		if k.keycode >= KEY_F1 and k.keycode <= KEY_F5:
			var idx := k.keycode - KEY_F1
			if idx == 0:
				party_member_selected.emit(GameState.self_id)
			else:
				var others := _other_members()
				if idx - 1 < others.size():
					var ent: Variant = (others[idx - 1] as Dictionary).get("entityId")
					if ent != null:
						party_member_selected.emit(int(ent))
			get_viewport().set_input_as_handled()


## El mundo avisa al enviar DuelRequest/TradeRequest para no mostrarnos el diálogo de aceptar a nosotros mismos.
func mark_outgoing(kind: String) -> void:
	_outgoing[kind] = Time.get_ticks_msec()


func _is_outgoing(kind: String) -> bool:
	return _outgoing.has(kind) and Time.get_ticks_msec() - int(_outgoing[kind]) < 35_000


# --- Grupo ----------------------------------------------------------------------------------------------------------------

func _other_members() -> Array:
	var out: Array = []
	for m: Variant in GameState.party.get("members", []):
		if str((m as Dictionary).get("name", "")) != GameState.character_name:
			out.append(m)
	return out


func _refresh_party() -> void:
	for c: Node in _frames.get_children():
		c.queue_free()
	_frame_buttons.clear()
	for m: Variant in _other_members():
		var md: Dictionary = m
		var b := Button.new()
		b.theme_type_variation = "SmallButton"
		b.custom_minimum_size = Vector2(110, 16)
		b.icon = UiTheme.icon("classes/" + str(md.get("classId", "")))
		b.alignment = HORIZONTAL_ALIGNMENT_LEFT
		var online := bool(md.get("online", true))
		b.text = party_frame_text(md, GameState.map_id)
		b.tooltip_text = "Mapa: %s" % UiText.map_name(str(md.get("mapId", "?")))
		b.modulate = Color.WHITE if online else Color(0.6, 0.6, 0.6)
		var ent: Variant = md.get("entityId")
		if ent != null:
			b.pressed.connect(func() -> void: party_member_selected.emit(int(ent)))
		_frames.add_child(b)
		_frame_buttons.append(b)


## Texto del marco de un compañero (HU-062 CA1): nombre, clase, nivel, vida y recurso en %; su mapa si no es el mío (HU-027 CA5).
static func party_frame_text(md: Dictionary, my_map_id: String) -> String:
	var text := "%s %s nv%d  %d%%" % [str(md.get("name", "")), UiText.class_name_of(str(md.get("classId", ""))), int(md.get("level", 0)), int(md.get("hpPct", 0))]
	var res: Variant = md.get("resPct")
	if res != null:
		text += " · %d%% %s" % [int(res), UiText.resource(str(Content.character_class(str(md.get("classId", ""))).get("resource", ""))).to_lower()]
	var map_id := str(md.get("mapId", ""))
	if not map_id.is_empty() and map_id != my_map_id:
		text += " · %s" % UiText.map_name(map_id)
	if not bool(md.get("online", true)):
		text += " (desc.)"
	return text


func _on_party_invited(leader: String) -> void:
	_prompt_kind = "party"
	_prompt.title = "Invitación de grupo"
	_prompt.dialog_text = "%s te invita a su grupo" % leader
	_prompt.popup_centered()


# --- Duelos -----------------------------------------------------------------------------------------------------------------

func _on_duel(state: String, opponent_id: int, winner_id: int, starts_in_ms: int) -> void:
	match state:
		"requested":
			if _prompt.visible or _is_outgoing("duel"):
				_outgoing.erase("duel")
				GameState.notice.emit("Duelo solicitado…")
				return
			_prompt_kind = "duel"
			_prompt.title = "Duelo"
			_prompt.dialog_text = "%s te reta a un duelo" % _name_of(opponent_id)
			_prompt.popup_centered()
		"countdown":
			_duel_label.text = "Duelo en %d…" % ceili(starts_in_ms / 1000.0)
			_duel_until = Time.get_ticks_msec() / 1000.0 + starts_in_ms / 1000.0 + 0.5
		"active":
			_duel_label.text = "¡Luchad!"
			_duel_until = Time.get_ticks_msec() / 1000.0 + 1.5
		"ended":
			_duel_label.text = "¡Has ganado el duelo!" if winner_id == GameState.self_id else "Has perdido el duelo"
			if winner_id != GameState.self_id and GameState.duel_reason == "zone":
				_duel_label.text = "Has perdido el duelo: saliste de la zona"
			_duel_until = Time.get_ticks_msec() / 1000.0 + 3.0
		"declined":
			_duel_label.text = "Duelo rechazado"
			_duel_until = Time.get_ticks_msec() / 1000.0 + 2.0


func _respond(accept: bool) -> void:
	match _prompt_kind:
		"party": Net.send("PartyRespond", {"accept": accept})
		"duel": Net.send("DuelRespond", {"accept": accept})
		"trade": Net.send("TradeRespond", {"accept": accept})
	_prompt_kind = ""


# --- Intercambio -------------------------------------------------------------------------------------------------------------

func _build_trade() -> void:
	_trade = TradeWindow.new()
	_trade.entity_name = entity_name
	add_child(_trade)


## Añade un item de la bolsa a mi oferta (clic derecho en la bolsa, HU-059 CA2).
func offer_item(item: Dictionary) -> void:
	_trade.offer_item(item)


func _on_trade(d: Dictionary) -> void:
	var state := str(d.get("state", ""))
	match state:
		"requested":
			if _is_outgoing("trade"):
				_outgoing.erase("trade")
				GameState.notice.emit("Intercambio solicitado…")
			elif not _prompt.visible:
				_prompt_kind = "trade"
				_prompt.title = "Intercambio"
				_prompt.dialog_text = "%s quiere intercambiar contigo" % _name_of(int(d.get("partnerId", -1)))
				_prompt.popup_centered()
		"open":
			_trade.show_update(d)
		"completed", "cancelled":
			_trade.close()
			var reason := str(d.get("reason", "")) if d.get("reason") != null else ""
			GameState.notice.emit("Intercambio completado" if state == "completed" else ("Intercambio cancelado" + ((": %s" % ApiMessages.text_for(reason)) if reason == "duel_busy" else "")))


func _name_of(entity_id: int) -> String:
	var name := str(entity_name.call(entity_id)) if entity_name.is_valid() else ""
	return name if not name.is_empty() else "#%d" % entity_id


# --- Cambio de clase ----------------------------------------------------------------------------------------------------------

func _build_class_window() -> void:
	_class_window = PanelContainer.new()
	_class_window.visible = false
	add_child(_class_window)


## HU-044 CA4: el maestro de clases solo cambia de clase hasta `progression.classChange.npcUntilPhase` (el servidor responde
## `forbidden` después; el cliente ni siquiera ofrece la ventana).
static func class_change_available() -> bool:
	var until: Variant = (Content.rule("progression", "classChange", {}) as Dictionary).get("npcUntilPhase")
	return until == null or int(Content.rule("world", "currentPhase", 1)) <= int(until)


## HU-044 CA1: las otras 3 clases con rol, recurso y descripción.
func open_class_change(npc_id: int) -> void:
	if not class_change_available():
		GameState.notice.emit("El maestro de clases ya no enseña otras clases")
		return
	_class_npc_id = npc_id
	for c: Node in _class_window.get_children():
		_class_window.remove_child(c)
		c.queue_free()
	var v := VBoxContainer.new()
	v.add_theme_constant_override("separation", 3)
	_class_window.add_child(v)
	v.add_child(InventoryWindow.title_row("Maestro de clases"))
	var intro := Label.new()
	intro.text = "Cambia de clase: conservas nivel, objetos y oro."
	intro.theme_type_variation = "SmallLabel"
	intro.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	intro.custom_minimum_size = Vector2(CLASS_WINDOW_WIDTH, 0)
	v.add_child(intro)
	for c: Variant in Content.classes():
		var cd: Dictionary = c
		var id := str(cd.get("id", ""))
		if id == GameState.class_id:
			continue
		var b := Button.new()
		b.theme_type_variation = "SmallButton"
		b.alignment = HORIZONTAL_ALIGNMENT_LEFT
		b.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		b.icon = UiTheme.icon("classes/" + id)
		b.custom_minimum_size = Vector2(CLASS_WINDOW_WIDTH, 0)
		b.text = "%s · %s · %s\n%s" % [str(cd.get("name", id)), UiText.role(str(cd.get("role", ""))), UiText.resource(str(cd.get("resource", ""))), str(cd.get("description", ""))]
		b.pressed.connect(func() -> void:
			Net.send("ChangeClass", {"npcId": _class_npc_id, "classId": id, "reqId": Net.next_req_id()})
			_class_window.visible = false)
		v.add_child(b)
	var close := Button.new()
	close.text = "Cerrar"
	close.size_flags_horizontal = Control.SIZE_SHRINK_END
	close.pressed.connect(func() -> void: _class_window.visible = false)
	v.add_child(close)
	_class_window.visible = true
	_dock_class_window()
	UiTheme.bring_to_front(_class_window)
	# El tamaño mínimo de los textos con salto de línea se conoce un cuadro después: se vuelve a centrar entonces.
	_dock_class_window.call_deferred()


## Centrado con su tamaño real y siempre dentro de los 480×270 (antes quedaba fuera por arriba a la izquierda).
func _dock_class_window() -> void:
	if not is_instance_valid(_class_window):
		return
	_class_window.reset_size()
	UiTheme.dock(_class_window, Control.PRESET_CENTER)
	UiTheme.clamp_to_screen(_class_window)


# --- Jugadores en línea (HU-063) --------------------------------------------------------------------------------------------

func _build_online_window() -> void:
	_online_window = PanelContainer.new()
	_online_window.visible = false
	var v := VBoxContainer.new()
	v.add_theme_constant_override("separation", 3)
	_online_window.add_child(v)
	v.add_child(InventoryWindow.title_row("En línea"))
	_online_rows = VBoxContainer.new()
	_online_rows.add_theme_constant_override("separation", 1)
	v.add_child(_online_rows)
	var close := Button.new()
	close.text = "Cerrar"
	close.pressed.connect(close_online_list)
	v.add_child(close)
	add_child(_online_window)


## Tecla O (HU-063 CA1): abre la lista pidiéndola al servidor, o la cierra si ya está abierta.
func toggle_online_list() -> void:
	if _online_window.visible:
		close_online_list()
		return
	Net.send("OnlineListRequest")


func is_online_list_open() -> bool:
	return _online_window.visible


func close_online_list() -> void:
	_online_window.visible = false


## Una fila de la lista: "Ana · Mago · nv 4 · Campos".
static func online_row_text(p: Dictionary) -> String:
	return "%s · %s · nv %d · %s" % [str(p.get("name", "")), UiText.class_name_of(str(p.get("classId", ""))), int(p.get("level", 0)), str(p.get("zone", ""))]


func _show_online_list(players: Array) -> void:
	for c: Node in _online_rows.get_children():
		c.queue_free()
	for p: Variant in players:
		var pd: Dictionary = p
		var player_name := str(pd.get("name", ""))
		var b := Button.new()
		b.theme_type_variation = "SmallButton"
		b.alignment = HORIZONTAL_ALIGNMENT_LEFT
		b.icon = UiTheme.icon("classes/" + str(pd.get("classId", "")))
		b.custom_minimum_size = Vector2(ONLINE_WINDOW_WIDTH, 0)
		b.text = online_row_text(pd)
		if player_name != GameState.character_name:
			# HU-063 CA2: clic (o clic derecho) sobre otro jugador → susurrar o invitar al grupo, aunque esté en otro mapa.
			b.tooltip_text = "Clic: susurrar o invitar"
			b.button_mask = MOUSE_BUTTON_MASK_LEFT | MOUSE_BUTTON_MASK_RIGHT
			b.pressed.connect(func() -> void: _open_online_menu(player_name))
		_online_rows.add_child(b)
	if players.is_empty():
		var empty := Label.new()
		empty.text = "Nadie conectado"
		_online_rows.add_child(empty)
	_online_window.visible = true
	UiTheme.dock(_online_window, Control.PRESET_CENTER)
	UiTheme.bring_to_front(_online_window)


func _open_online_menu(player_name: String) -> void:
	var menu := PopupMenu.new()
	menu.add_item("Susurrar", 0)
	menu.add_item("Invitar al grupo", 1)
	menu.id_pressed.connect(func(id: int) -> void:
		match id:
			0:
				whisper_requested.emit(player_name)
				close_online_list()
			1: Net.send("PartyInvite", {"name": player_name})
		menu.queue_free())
	add_child(menu)
	menu.position = Vector2i(get_viewport().get_mouse_position())
	menu.popup()

