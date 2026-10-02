class_name ItemSlot
extends Button
## Casilla de item con arrastrar/soltar (HU-051 CA2): el dato arrastrado es {"c": "bag"|"equip", "i": índice, "itemId"}.
## La ventana dueña recibe `dropped(from, to, qty)` y `right_clicked(slot)`; Shift+clic abre división (HU-056 CA1).
## Muestra el ícono de 16×16 a ×2, la cantidad abajo a la derecha, la rareza en el color del marco y, si está vacía y es de
## equipo, una silueta tenue del hueco (cabeza, cuello…). El nombre completo va en el tooltip.

signal dropped(from: Dictionary, to: Dictionary, qty: int)
signal right_clicked(slot: ItemSlot)
signal split_requested(slot: ItemSlot)

## Ícono de 32×32 (16 a ×2) dentro del fondo hundido de 1 px.
const SIZE := Vector2(34, 34)
const ICON_SIZE := Vector2(32, 32)
const RARITY_COLORS := {"junk": Color("9babb2"), "common": Color("c7dcd0"), "uncommon": Color("1ebc73"), "rare": Color("4d9be6"), "epic": Color("a884f3")}

var container: String = "bag"
var index: int = 0
var item: Dictionary = {}  # {id, templateId, qty} o vacío
var accepts_drops: bool = true
## Cantidad fijada por Shift+clic para el próximo arrastre (0 = todo).
var pending_split_qty: int = 0
## Tooltip propio (RichTooltip) con colores de rareza y comparación.
var tooltip_bbcode: String = ""
var _icon: TextureRect
var _frame: NinePatchRect
var _qty: Label
var _placeholder: String = ""
var _silhouette: Texture2D


func _ready() -> void:
	custom_minimum_size = SIZE
	focus_mode = Control.FOCUS_NONE
	theme_type_variation = "SlotButton"
	_icon = TextureRect.new()
	_icon.custom_minimum_size = ICON_SIZE
	_icon.size = ICON_SIZE
	_icon.position = (SIZE - ICON_SIZE) / 2.0
	_icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_icon.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	_icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_icon)
	_frame = NinePatchRect.new()
	_frame.texture = UiTheme.texture("slot_frame.png")
	_frame.patch_margin_left = 2
	_frame.patch_margin_top = 2
	_frame.patch_margin_right = 2
	_frame.patch_margin_bottom = 2
	_frame.set_anchors_preset(Control.PRESET_FULL_RECT)
	_frame.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_frame.visible = false
	add_child(_frame)
	_qty = Label.new()
	UiTheme.outlined(_qty)
	_qty.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_RIGHT)
	_qty.grow_horizontal = Control.GROW_DIRECTION_BEGIN
	_qty.grow_vertical = Control.GROW_DIRECTION_BEGIN
	_qty.offset_right = -2
	_qty.offset_bottom = 0
	_qty.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_qty)
	refresh()


## Hueco de equipo vacío: nombre para el tooltip ("Cabeza") y silueta tenue (`slot_key`: head, neck…).
func set_placeholder(placeholder: String, slot_key: String = "") -> void:
	_placeholder = placeholder
	if not slot_key.is_empty():
		_silhouette = UiTheme.icon("slots/" + slot_key)
	refresh()


func _make_custom_tooltip(_for_text: String) -> Object:
	return RichTooltip.make(tooltip_bbcode) if not tooltip_bbcode.is_empty() else null


func set_item(new_item: Dictionary) -> void:
	item = new_item
	refresh()


func icon_texture() -> Texture2D:
	return _icon.texture if _icon != null else null


func refresh() -> void:
	if _icon == null:
		return
	if item.is_empty():
		_icon.texture = _silhouette
		_icon.modulate = Color(1, 1, 1, 0.55)
		_frame.visible = false
		_qty.text = ""
		tooltip_text = _placeholder
		tooltip_bbcode = ""
		return
	var tpl := Content.item(str(item.get("templateId", "")))
	var qty := int(item.get("qty", 1))
	_icon.texture = UiTheme.icon(str(tpl.get("icon", "")))
	_icon.modulate = Color.WHITE
	_qty.text = str(qty) if qty > 1 else ""
	var rarity := str(tpl.get("rarity", "common"))
	_frame.visible = rarity != "common"
	_frame.self_modulate = RARITY_COLORS.get(rarity, Color.WHITE)
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


## Ícono fantasma para arrastrar (también lo usan la barra y el libro de hechizos).
static func drag_preview(tex: Texture2D) -> Control:
	var preview := TextureRect.new()
	preview.texture = tex
	preview.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	preview.size = ICON_SIZE
	preview.position = -ICON_SIZE / 2.0
	preview.modulate = Color(1, 1, 1, 0.8)
	var holder := Control.new()
	holder.add_child(preview)
	return holder


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
	set_drag_preview(ItemSlot.drag_preview(_icon.texture))
	var qty := pending_split_qty if pending_split_qty > 0 else 0
	pending_split_qty = 0
	return {"c": container, "i": index, "itemId": str(item.get("id", "")), "qty": qty}


func _can_drop_data(_at: Vector2, data: Variant) -> bool:
	return accepts_drops and data is Dictionary and (data as Dictionary).has("c")


func _drop_data(_at: Vector2, data: Variant) -> void:
	var from: Dictionary = data
	dropped.emit({"c": from["c"], "i": int(from["i"])}, {"c": container, "i": index}, int(from.get("qty", 0)))
