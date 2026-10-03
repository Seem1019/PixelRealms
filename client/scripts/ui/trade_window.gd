class_name TradeWindow
extends PanelContainer
## Ventana de intercambio (HU-059): dos rejillas de casillas como las de la bolsa, "Ofreces" y "Recibes", con el objeto,
## la cantidad y su tooltip, el oro de cada lado y si cada uno ha confirmado. Se ofrece arrastrando desde la bolsa (o con
## clic derecho en la bolsa) y se retira con clic derecho en la propia casilla. Lo que se muestra es siempre lo que manda
## el servidor (`TradeUpdate`); la oferta local solo es lo que se envía en `TradeOffer`.

## TradeSession.MaxItems del servidor.
## Casillas de oferta: `rules.social.tradeMaxItems` (el servidor lo valida igual).
var MAX_ITEMS: int = int(Content.rule("social", "tradeMaxItems", 6))
const COLUMNS := 3

## Nombre de una entidad por id (lo rellena el mundo; "" si no se ve).
var entity_name: Callable = Callable()

var _title: Label
var _mine: Array[ItemSlot] = []
var _theirs: Array[ItemSlot] = []
var _gold: SpinBox
var _their_gold: Label
var _mine_state: Label
var _theirs_state: Label
var _theirs_heading: Label
var _confirm: Button
var _offer: Array[Dictionary] = []  # [{itemId, qty}] lo que envío en TradeOffer
var _partner_id: int = -1


func _ready() -> void:
	visible = false
	var v := VBoxContainer.new()
	add_child(v)
	_title = Label.new()
	_title.theme_type_variation = "TitleLabel"
	v.add_child(_title)
	# Dos líneas cortas (sin autoajuste, que en un contenedor sin ancho fijo estira el panel): tiene que caber junto a la bolsa.
	for line: String in ["Arrastra desde la bolsa para ofrecer", "Clic derecho en tu casilla para retirar"]:
		var hint := Label.new()
		hint.theme_type_variation = "SmallLabel"
		hint.text = line
		v.add_child(hint)
	var sides := HBoxContainer.new()
	sides.add_theme_constant_override("separation", 3 * UiTheme.GAP)
	v.add_child(sides)

	var mine_col := VBoxContainer.new()
	sides.add_child(mine_col)
	var mine_heading := Label.new()
	mine_heading.text = "Ofreces"
	mine_col.add_child(mine_heading)
	mine_col.add_child(_grid(_mine, true))
	var gold_row := HBoxContainer.new()
	var gl := Label.new()
	gl.text = "Oro:"
	gold_row.add_child(gl)
	_gold = SpinBox.new()
	_gold.min_value = 0
	_gold.max_value = 1000000000
	_gold.value_changed.connect(func(_v: float) -> void: _send_offer())
	gold_row.add_child(_gold)
	mine_col.add_child(gold_row)
	_mine_state = Label.new()
	_mine_state.theme_type_variation = "SmallLabel"
	mine_col.add_child(_mine_state)

	var theirs_col := VBoxContainer.new()
	sides.add_child(theirs_col)
	_theirs_heading = Label.new()
	theirs_col.add_child(_theirs_heading)
	theirs_col.add_child(_grid(_theirs, false))
	_their_gold = Label.new()
	theirs_col.add_child(_their_gold)
	_theirs_state = Label.new()
	_theirs_state.theme_type_variation = "SmallLabel"
	theirs_col.add_child(_theirs_state)

	var buttons := HBoxContainer.new()
	_confirm = Button.new()
	_confirm.text = "Confirmar"
	_confirm.pressed.connect(func() -> void: Net.send("TradeConfirm", {"version": int(GameState.trade.get("version", 0))}))
	buttons.add_child(_confirm)
	var cancel := Button.new()
	cancel.text = "Cancelar"
	cancel.pressed.connect(func() -> void: Net.send("TradeCancel"))
	buttons.add_child(cancel)
	v.add_child(buttons)


func _grid(into: Array[ItemSlot], mine: bool) -> GridContainer:
	var grid := GridContainer.new()
	grid.columns = COLUMNS
	for i: int in MAX_ITEMS:
		var s := ItemSlot.new()
		s.container = "trade"
		s.index = i
		s.draggable = false
		s.accepts_drops = mine
		if mine:
			s.dropped.connect(_on_mine_dropped)
			s.right_clicked.connect(_on_mine_right_clicked)
		grid.add_child(s)
		into.append(s)
	return grid


## TradeUpdate{open}: refleja las dos ofertas y las confirmaciones.
func show_update(d: Dictionary) -> void:
	if not visible:
		_offer.clear()
		_gold.set_value_no_signal(0)
	_partner_id = int(d.get("partnerId", -1))
	var partner := _partner_name()
	_title.text = "Intercambio con %s" % partner
	_theirs_heading.text = "Recibes de %s" % partner
	var mine: Dictionary = d.get("mine", {})
	var theirs: Dictionary = d.get("theirs", {})
	_fill(_mine, mine.get("items", []))
	_fill(_theirs, theirs.get("items", []))
	_their_gold.text = "Oro: %s" % MoneyFormat.format(int(theirs.get("gold", 0)))
	var mine_ok := bool(d.get("confirmedMine", false))
	var theirs_ok := bool(d.get("confirmedTheirs", false))
	var reason := str(d.get("reason", "")) if d.get("reason") != null else ""
	_mine_state.text = ("Tú: confirmado" if mine_ok else "Tú: sin confirmar") + (("  (%s)" % ApiMessages.text_for(reason)) if not reason.is_empty() else "")
	_theirs_state.text = "%s: %s" % [partner, "confirmado" if theirs_ok else "sin confirmar"]
	_confirm.disabled = mine_ok
	visible = true
	# A la izquierda: la bolsa vive arriba a la derecha y hay que verlas a la vez para arrastrar.
	UiTheme.dock(self, Control.PRESET_CENTER_LEFT)
	UiTheme.bring_to_front(self)


func close() -> void:
	visible = false
	_offer.clear()


func _fill(slots: Array[ItemSlot], items: Array) -> void:
	for i: int in slots.size():
		if i < items.size():
			var it: Dictionary = items[i]
			slots[i].set_item({"id": str(it.get("itemId", "")), "templateId": str(it.get("templateId", "")), "qty": int(it.get("qty", 1))})
		else:
			slots[i].set_item({})


## Añade un item de la bolsa a mi oferta (hasta MAX_ITEMS) y la reenvía.
func offer_item(item: Dictionary) -> void:
	if not visible or item.is_empty() or _offer.size() >= MAX_ITEMS:
		return
	var id := str(item.get("id", ""))
	for o: Dictionary in _offer:
		if str(o["itemId"]) == id:
			return
	_offer.append({"itemId": id, "qty": int(item.get("qty", 1))})
	_send_offer()


func _on_mine_dropped(from: Dictionary, _to: Dictionary, qty: int) -> void:
	if str(from.get("c", "")) != "bag":
		return
	var index := int(from.get("i", -1))
	if index < 0 or index >= GameState.inventory.size() or not (GameState.inventory[index] is Dictionary):
		return
	var item: Dictionary = (GameState.inventory[index] as Dictionary).duplicate()
	if qty > 0:
		item["qty"] = qty  # Shift+clic en la bolsa: solo parte del montón
	offer_item(item)


func _on_mine_right_clicked(slot: ItemSlot) -> void:
	var id := str(slot.item.get("id", ""))
	for i: int in range(_offer.size() - 1, -1, -1):
		if str(_offer[i]["itemId"]) == id:
			_offer.remove_at(i)
	_send_offer()


func _send_offer() -> void:
	if visible:
		Net.send("TradeOffer", {"items": _offer, "gold": int(_gold.value)})


func _partner_name() -> String:
	var name := str(entity_name.call(_partner_id)) if entity_name.is_valid() else ""
	return name if not name.is_empty() else "#%d" % _partner_id


# --- Para tests -------------------------------------------------------------------------------------------------------

func mine_slots() -> Array[ItemSlot]:
	return _mine


func theirs_slots() -> Array[ItemSlot]:
	return _theirs


func title_text() -> String:
	return _title.text


func status_text() -> String:
	return "%s · %s" % [_mine_state.text, _theirs_state.text]


func offered_ids() -> Array[String]:
	var out: Array[String] = []
	for o: Dictionary in _offer:
		out.append(str(o["itemId"]))
	return out
