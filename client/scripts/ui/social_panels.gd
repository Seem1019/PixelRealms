class_name SocialPanels
extends Control
## Marcos de grupo (HU-062 CA1/CA5), diálogos de invitación/duelo/intercambio (Aceptar/Rechazar), cuenta atrás y resultado del
## duelo (HU-064), ventana de intercambio (HU-059) y ventana de cambio de clase (HU-044). Todo construido por código.

signal party_member_selected(entity_id: int)

var _frames: VBoxContainer
var _frame_buttons: Array[Button] = []
var _prompt: ConfirmationDialog
var _prompt_kind: String = ""
var _duel_label: Label
var _duel_until: float = 0.0
var _trade: PanelContainer
var _trade_mine: Label
var _trade_theirs: Label
var _trade_gold: SpinBox
var _trade_confirm: Button
var _trade_status: Label
var _trade_offer_items: Array[Dictionary] = []  # [{itemId, qty}]
var _class_window: PanelContainer
var _class_npc_id: int = -1
## Solicitudes que envié yo (el protocolo no distingue quién pidió): no mostrar el diálogo de aceptar.
var _outgoing: Dictionary = {}  # "duel" | "trade" → ms de envío


func _ready() -> void:
	set_anchors_preset(Control.PRESET_FULL_RECT)
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
	GameState.party_changed.connect(_refresh_party)
	GameState.party_invited.connect(_on_party_invited)
	GameState.duel_changed.connect(_on_duel)
	GameState.trade_changed.connect(_on_trade)
	GameState.inventory_changed.connect(_refresh_trade_offer_text)
	_refresh_party()


func _process(_delta: float) -> void:
	if _duel_until > 0.0 and Time.get_ticks_msec() / 1000.0 >= _duel_until:
		_duel_until = 0.0
		_duel_label.text = ""


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
		b.custom_minimum_size = Vector2(110, 22)
		b.alignment = HORIZONTAL_ALIGNMENT_LEFT
		var online := bool(md.get("online", true))
		b.text = "%s %s nv%d  %d%%%s" % [str(md.get("name", "")), UiText.class_name_of(str(md.get("classId", ""))), int(md.get("level", 0)), int(md.get("hpPct", 0)), "" if online else " (desc.)"]
		b.tooltip_text = "Mapa: %s" % str(md.get("mapId", "?"))
		b.modulate = Color.WHITE if online else Color(0.6, 0.6, 0.6)
		var ent: Variant = md.get("entityId")
		if ent != null:
			b.pressed.connect(func() -> void: party_member_selected.emit(int(ent)))
		_frames.add_child(b)
		_frame_buttons.append(b)


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
			_prompt.dialog_text = "#%d te reta a un duelo" % opponent_id
			_prompt.popup_centered()
		"countdown":
			_duel_label.text = "Duelo en %d…" % ceili(starts_in_ms / 1000.0)
			_duel_until = Time.get_ticks_msec() / 1000.0 + starts_in_ms / 1000.0 + 0.5
		"active":
			_duel_label.text = "¡Luchad!"
			_duel_until = Time.get_ticks_msec() / 1000.0 + 1.5
		"ended":
			_duel_label.text = "¡Has ganado el duelo!" if winner_id == GameState.self_id else "Has perdido el duelo"
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
	_trade = PanelContainer.new()
	_trade.visible = false
	add_child(_trade)
	var v := VBoxContainer.new()
	_trade.add_child(v)
	var title := Label.new()
	title.text = "Intercambio (clic derecho en la bolsa para ofrecer)"
	title.theme_type_variation = "TitleLabel"
	v.add_child(title)
	_trade_mine = Label.new()
	v.add_child(_trade_mine)
	_trade_theirs = Label.new()
	v.add_child(_trade_theirs)
	var gold_row := HBoxContainer.new()
	var gl := Label.new()
	gl.text = "Oro:"
	gold_row.add_child(gl)
	_trade_gold = SpinBox.new()
	_trade_gold.min_value = 0
	_trade_gold.max_value = 1000000000
	_trade_gold.value_changed.connect(func(_v: float) -> void: _send_offer())
	gold_row.add_child(_trade_gold)
	v.add_child(gold_row)
	_trade_status = Label.new()
	v.add_child(_trade_status)
	var buttons := HBoxContainer.new()
	_trade_confirm = Button.new()
	_trade_confirm.text = "Confirmar"
	_trade_confirm.pressed.connect(func() -> void: Net.send("TradeConfirm", {"version": int(GameState.trade.get("version", 0))}))
	buttons.add_child(_trade_confirm)
	var cancel := Button.new()
	cancel.text = "Cancelar"
	cancel.pressed.connect(func() -> void: Net.send("TradeCancel"))
	buttons.add_child(cancel)
	v.add_child(buttons)


## Añade un item de la bolsa a mi oferta (hasta 6) y la reenvía.
func offer_item(item: Dictionary) -> void:
	if not _trade.visible or _trade_offer_items.size() >= 6:
		return
	var id := str(item.get("id", ""))
	for o: Dictionary in _trade_offer_items:
		if str(o["itemId"]) == id:
			return
	_trade_offer_items.append({"itemId": id, "qty": int(item.get("qty", 1))})
	_send_offer()


func _send_offer() -> void:
	if not _trade.visible:
		return
	Net.send("TradeOffer", {"items": _trade_offer_items, "gold": int(_trade_gold.value)})


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
				_prompt.dialog_text = "#%d quiere intercambiar contigo" % int(d.get("partnerId", -1))
				_prompt.popup_centered()
		"open":
			if not _trade.visible:
				_trade_offer_items.clear()
				_trade_gold.set_value_no_signal(0)
			_trade.visible = true
			UiTheme.dock(_trade, Control.PRESET_CENTER)
			UiTheme.bring_to_front(_trade)
			_refresh_trade_offer_text()
			var mine_ok := bool(d.get("confirmedMine", false))
			var theirs_ok := bool(d.get("confirmedTheirs", false))
			var reason := str(d.get("reason", "")) if d.get("reason") != null else ""
			_trade_status.text = "Tú: %s · Él: %s%s" % ["✔" if mine_ok else "…", "✔" if theirs_ok else "…", ("  (%s)" % ApiMessages.text_for(reason)) if not reason.is_empty() else ""]
			_trade_confirm.disabled = mine_ok
		"completed", "cancelled":
			_trade.visible = false
			_trade_offer_items.clear()
			GameState.notice.emit("Intercambio completado" if state == "completed" else "Intercambio cancelado")


func _refresh_trade_offer_text() -> void:
	if not _trade.visible:
		return
	var d := GameState.trade
	_trade_mine.text = "Ofreces: " + _offer_text(d.get("mine", {}))
	_trade_theirs.text = "Recibes: " + _offer_text(d.get("theirs", {}))


static func _offer_text(offer: Dictionary) -> String:
	var parts: Array[String] = []
	for it: Variant in offer.get("items", []):
		var itd: Dictionary = it
		var bag_item := GameState.bag_item(str(itd.get("itemId", "")))
		var name := str(Content.item(str(bag_item.get("templateId", ""))).get("name", "item")) if not bag_item.is_empty() else "item"
		parts.append("%s ×%d" % [name, int(itd.get("qty", 1))])
	parts.append(MoneyFormat.format(int(offer.get("gold", 0))))
	return ", ".join(parts)


# --- Cambio de clase ----------------------------------------------------------------------------------------------------------

func _build_class_window() -> void:
	_class_window = PanelContainer.new()
	_class_window.visible = false
	add_child(_class_window)


## HU-044 CA1: las otras 3 clases con rol, recurso y descripción.
func open_class_change(npc_id: int) -> void:
	_class_npc_id = npc_id
	for c: Node in _class_window.get_children():
		c.queue_free()
	var v := VBoxContainer.new()
	_class_window.add_child(v)
	var title := Label.new()
	title.text = "Cambiar de clase (conservas nivel, objetos y oro)"
	title.theme_type_variation = "TitleLabel"
	v.add_child(title)
	for c: Variant in Content.classes():
		var cd: Dictionary = c
		var id := str(cd.get("id", ""))
		if id == GameState.class_id:
			continue
		var b := Button.new()
		b.theme_type_variation = "SmallButton"
		b.alignment = HORIZONTAL_ALIGNMENT_LEFT
		b.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		b.custom_minimum_size = Vector2(220, 0)
		b.text = "%s · %s · %s\n%s" % [str(cd.get("name", id)), UiText.role(str(cd.get("role", ""))), UiText.resource(str(cd.get("resource", ""))), str(cd.get("description", ""))]
		b.pressed.connect(func() -> void:
			Net.send("ChangeClass", {"npcId": _class_npc_id, "classId": id, "reqId": Net.next_req_id()})
			_class_window.visible = false)
		v.add_child(b)
	var close := Button.new()
	close.text = "Cerrar"
	close.pressed.connect(func() -> void: _class_window.visible = false)
	v.add_child(close)
	_class_window.visible = true
	UiTheme.dock(_class_window, Control.PRESET_CENTER)
	UiTheme.bring_to_front(_class_window)
