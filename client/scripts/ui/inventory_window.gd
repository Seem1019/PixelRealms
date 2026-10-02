class_name InventoryWindow
extends PanelContainer
## Bolsa de 6×4 (HU-051 CA1) con oro; arrastrar → InventoryMove; clic derecho → equipar o usar (HU-052 CA1, HU-054);
## Shift+clic → diálogo de cantidad (HU-056 CA1); arrastrar fuera → "¿Destruir X?" (HU-056 CA2). Con el vendedor abierto el
## clic derecho vende (HU-055 CA3). Redibuja entera con cada InventoryUpdate (barato).

signal sell_requested(item: Dictionary)
signal offer_requested(item: Dictionary)

## Las ventanas empiezan debajo de los marcos de vida (UiTheme).
const WINDOW_TOP := 52

var vendor_mode: bool = false

var _slots: Array[ItemSlot] = []
var _gold: Label
var _split_dialog: ConfirmationDialog
var _split_spin: SpinBox
var _split_slot: ItemSlot
var _destroy_dialog: ConfirmationDialog
var _destroy_item: Dictionary = {}


func _ready() -> void:
	visible = false
	var v := VBoxContainer.new()
	add_child(v)
	var title := Label.new()
	title.text = "Bolsa"
	title.theme_type_variation = "TitleLabel"
	v.add_child(title)
	var grid := GridContainer.new()
	grid.columns = 6
	v.add_child(grid)
	for i: int in 24:
		var s := ItemSlot.new()
		s.container = "bag"
		s.index = i
		s.dropped.connect(_on_dropped)
		s.right_clicked.connect(_on_right_click)
		s.split_requested.connect(_on_split)
		grid.add_child(s)
		_slots.append(s)
	_gold = Label.new()
	v.add_child(_gold)

	_split_dialog = ConfirmationDialog.new()
	_split_dialog.title = "Dividir"
	_split_spin = SpinBox.new()
	_split_spin.min_value = 1
	_split_dialog.add_child(_split_spin)
	_split_dialog.confirmed.connect(_on_split_confirmed)
	add_child(_split_dialog)
	_destroy_dialog = ConfirmationDialog.new()
	_destroy_dialog.confirmed.connect(_on_destroy_confirmed)
	add_child(_destroy_dialog)

	GameState.inventory_changed.connect(refresh)
	refresh()
	UiTheme.dock(self, Control.PRESET_TOP_RIGHT, UiTheme.SCREEN_MARGIN, WINDOW_TOP - UiTheme.SCREEN_MARGIN)


func refresh() -> void:
	for i: int in _slots.size():
		var it: Variant = GameState.inventory[i] if i < GameState.inventory.size() else null
		_slots[i].set_item(it if it is Dictionary else {})
	_gold.text = "Oro: %s" % MoneyFormat.format(GameState.gold)


func toggle() -> void:
	visible = not visible
	if visible:
		refresh()
		UiTheme.bring_to_front(self)


func _on_dropped(from: Dictionary, to: Dictionary, qty: int) -> void:
	var payload := {"from": from, "to": to, "reqId": Net.next_req_id()}
	if qty > 0:
		payload["qty"] = qty
	Net.send("InventoryMove", payload)


func _on_right_click(slot: ItemSlot) -> void:
	var tpl := Content.item(str(slot.item.get("templateId", "")))
	if vendor_mode:
		sell_requested.emit(slot.item)
		return
	if not GameState.trade.is_empty() and str(GameState.trade.get("state", "")) == "open":
		offer_requested.emit(slot.item)  # HU-059 CA2
		return
	if tpl.has("slot"):
		var idx := ItemSlot.slot_index(str(tpl["slot"]))
		Net.send("InventoryMove", {"from": {"c": "bag", "i": slot.index}, "to": {"c": "equip", "i": idx}, "reqId": Net.next_req_id()})
	elif str(tpl.get("type", "")) == "consumable":
		Net.send("UseItem", {"itemId": str(slot.item["id"]), "reqId": Net.next_req_id()})


func _on_split(slot: ItemSlot) -> void:
	_split_slot = slot
	_split_spin.max_value = int(slot.item.get("qty", 1)) - 1
	_split_spin.value = 1
	_split_dialog.popup_centered()


func _on_split_confirmed() -> void:
	if _split_slot != null:
		_split_slot.pending_split_qty = int(_split_spin.value)


## Soltar un item fuera de la ventana (lo llama el mundo): confirmación de destrucción.
func request_destroy(item: Dictionary) -> void:
	_destroy_item = item
	var tpl := Content.item(str(item.get("templateId", "")))
	_destroy_dialog.dialog_text = "¿Destruir %s%s?" % [str(tpl.get("name", "?")), (" ×%d" % int(item.get("qty", 1))) if int(item.get("qty", 1)) > 1 else ""]
	_destroy_dialog.popup_centered()


func _on_destroy_confirmed() -> void:
	if _destroy_item.is_empty():
		return
	Net.send("DestroyItem", {"itemId": str(_destroy_item["id"]), "qty": int(_destroy_item.get("qty", 1)), "reqId": Net.next_req_id()})
	_destroy_item = {}


## Vende todos los items de rareza junk (HU-055 CA4).
func sell_junk() -> void:
	for it: Variant in GameState.inventory:
		if it is Dictionary and str(Content.item(str((it as Dictionary).get("templateId", ""))).get("rarity", "")) == "junk":
			sell_requested.emit(it)
