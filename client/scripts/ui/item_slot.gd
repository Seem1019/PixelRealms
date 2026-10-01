class_name ItemSlot
extends Button
## Casilla de item con arrastrar/soltar (HU-051 CA2): el dato arrastrado es {"c": "bag"|"equip", "i": índice, "itemId"}.
## La ventana dueña recibe `dropped(from, to, qty)` y `right_clicked(slot)`; Shift+clic abre división (HU-056 CA1).

signal dropped(from: Dictionary, to: Dictionary, qty: int)
signal right_clicked(slot: ItemSlot)
signal split_requested(slot: ItemSlot)

const RARITY_COLORS := {"junk": Color(0.6, 0.6, 0.6), "common": Color(1, 1, 1), "uncommon": Color(0.12, 1, 0), "rare": Color(0, 0.44, 0.87), "epic": Color(0.64, 0.21, 0.93)}

var container: String = "bag"
var index: int = 0
var item: Dictionary = {}  # {id, templateId, qty} o vacío
var accepts_drops: bool = true
## Cantidad fijada por Shift+clic para el próximo arrastre (0 = todo).
var pending_split_qty: int = 0


func _ready() -> void:
	custom_minimum_size = Vector2(22, 22)
	focus_mode = Control.FOCUS_NONE
	add_theme_font_size_override("font_size", 7)
	clip_text = true
	refresh()


func set_item(new_item: Dictionary) -> void:
	item = new_item
	refresh()


func refresh() -> void:
	if item.is_empty():
		text = ""
		tooltip_text = ""
		modulate = Color(1, 1, 1, 0.6)
		return
	var tpl := Content.item(str(item.get("templateId", "")))
	var qty := int(item.get("qty", 1))
	text = ("%s\n%d" % [str(tpl.get("name", "?")).substr(0, 4), qty]) if qty > 1 else str(tpl.get("name", "?")).substr(0, 5)
	modulate = RARITY_COLORS.get(str(tpl.get("rarity", "common")), Color.WHITE)
	var equipped := _equipped_for(tpl)
	tooltip_text = _strip_bbcode(TooltipBuilder.build(tpl, qty, GameState.class_id, GameState.level, equipped))


func _equipped_for(tpl: Dictionary) -> Dictionary:
	if not tpl.has("slot") or container == "equip":
		return {}
	var slot_index := ItemSlot.slot_index(str(tpl["slot"]))
	if slot_index < 0 or slot_index >= GameState.equipment.size():
		return {}
	var eq: Variant = GameState.equipment[slot_index]
	return Content.item(str((eq as Dictionary).get("templateId", ""))) if eq is Dictionary else {}


static func slot_index(slot_name: String) -> int:
	const ORDER := ["head", "neck", "chest", "hands", "legs", "feet", "ring", "main_hand", "off_hand"]
	return ORDER.find(slot_name)


static func _strip_bbcode(text: String) -> String:
	var re := RegEx.new()
	re.compile("\\[/?[a-z]+[^\\]]*\\]")
	return re.sub(text, "", true)


func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.is_pressed():
		var mb := event as InputEventMouseButton
		if mb.button_index == MOUSE_BUTTON_RIGHT and not item.is_empty():
			right_clicked.emit(self)
			accept_event()
		elif mb.button_index == MOUSE_BUTTON_LEFT and mb.shift_pressed and not item.is_empty() and int(item.get("qty", 1)) > 1:
			split_requested.emit(self)
			accept_event()


func _get_drag_data(_at: Vector2) -> Variant:
	if item.is_empty():
		return null
	var preview := Label.new()
	preview.text = text
	set_drag_preview(preview)
	var qty := pending_split_qty if pending_split_qty > 0 else 0
	pending_split_qty = 0
	return {"c": container, "i": index, "itemId": str(item.get("id", "")), "qty": qty}


func _can_drop_data(_at: Vector2, data: Variant) -> bool:
	return accepts_drops and data is Dictionary and (data as Dictionary).has("c")


func _drop_data(_at: Vector2, data: Variant) -> void:
	var from: Dictionary = data
	dropped.emit({"c": from["c"], "i": int(from["i"])}, {"c": container, "i": index}, int(from.get("qty", 0)))
