class_name ItemSlot
extends Button
## Casilla de item con arrastrar/soltar (HU-051 CA2): el dato arrastrado es {"c": "bag"|"equip", "i": índice, "itemId"}.
## La ventana dueña recibe `dropped(from, to, qty)` y `right_clicked(slot)`; Shift+clic abre división (HU-056 CA1).

signal dropped(from: Dictionary, to: Dictionary, qty: int)
signal right_clicked(slot: ItemSlot)
signal split_requested(slot: ItemSlot)

const SIZE := Vector2(36, 26)
const RARITY_COLORS := {"junk": Color(0.6, 0.6, 0.6), "common": Color(1, 1, 1), "uncommon": Color(0.12, 1, 0), "rare": Color(0, 0.44, 0.87), "epic": Color(0.64, 0.21, 0.93)}

var container: String = "bag"
var index: int = 0
var item: Dictionary = {}  # {id, templateId, qty} o vacío
var accepts_drops: bool = true
## Cantidad fijada por Shift+clic para el próximo arrastre (0 = todo).
var pending_split_qty: int = 0
## Tooltip propio (RichTooltip) con colores de rareza y comparación.
var tooltip_bbcode: String = ""
## Nombre (una línea, nunca cortado a media palabra) y cantidad abajo a la derecha: el botón no crece con el texto.
var _name: Label
var _qty: Label
var _placeholder: String = ""


func _ready() -> void:
	custom_minimum_size = SIZE
	focus_mode = Control.FOCUS_NONE
	_name = Label.new()
	_name.theme_type_variation = "SmallLabel"
	_name.set_anchors_preset(Control.PRESET_FULL_RECT)
	_name.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_name.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_name.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	_name.clip_text = true
	_name.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_name)
	_qty = Label.new()
	_qty.theme_type_variation = "SmallLabel"
	_qty.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_RIGHT)
	_qty.grow_horizontal = Control.GROW_DIRECTION_BEGIN
	_qty.grow_vertical = Control.GROW_DIRECTION_BEGIN
	_qty.offset_right = -2
	_qty.offset_bottom = 1
	_qty.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_qty)
	refresh()


## Texto gris para una casilla de equipo vacía ("Cabeza", "Mano ppal.").
func set_placeholder(placeholder: String) -> void:
	_placeholder = placeholder
	refresh()


func _make_custom_tooltip(_for_text: String) -> Object:
	return RichTooltip.make(tooltip_bbcode) if not tooltip_bbcode.is_empty() else null


func set_item(new_item: Dictionary) -> void:
	item = new_item
	refresh()


func refresh() -> void:
	if _name == null:
		return
	if item.is_empty():
		_name.text = _placeholder
		_name.remove_theme_color_override("font_color")
		_qty.text = ""
		tooltip_text = _placeholder
		tooltip_bbcode = ""
		modulate = Color(1, 1, 1, 0.6)
		return
	var tpl := Content.item(str(item.get("templateId", "")))
	var qty := int(item.get("qty", 1))
	# Primera palabra completa (nunca cortada a media palabra) y cantidad; el nombre entero va en el tooltip.
	_name.text = UiText.short_name(str(tpl.get("name", "?")))
	_qty.text = str(qty) if qty > 1 else ""
	modulate = Color.WHITE
	_name.add_theme_color_override("font_color", RARITY_COLORS.get(str(tpl.get("rarity", "common")), Color.WHITE))
	var equipped := _equipped_for(tpl)
	tooltip_bbcode = TooltipBuilder.build(tpl, qty, GameState.class_id, GameState.level, equipped)
	tooltip_text = _strip_bbcode(tooltip_bbcode)


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
	preview.text = _name.text
	set_drag_preview(preview)
	var qty := pending_split_qty if pending_split_qty > 0 else 0
	pending_split_qty = 0
	return {"c": container, "i": index, "itemId": str(item.get("id", "")), "qty": qty}


func _can_drop_data(_at: Vector2, data: Variant) -> bool:
	return accepts_drops and data is Dictionary and (data as Dictionary).has("c")


func _drop_data(_at: Vector2, data: Variant) -> void:
	var from: Dictionary = data
	dropped.emit({"c": from["c"], "i": int(from["i"])}, {"c": container, "i": index}, int(from.get("qty", 0)))
