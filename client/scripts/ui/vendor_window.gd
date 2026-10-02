class_name VendorWindow
extends PanelContainer
## Ventana del vendedor (HU-055): items de vendors.json con precio; comprar (1 o 5); "Vender basura"; se cierra al alejarse > 3 casillas.

signal sell_junk_requested

var npc_id: int = -1

var _list: VBoxContainer
var _title: Label


func _ready() -> void:
	visible = false
	custom_minimum_size = Vector2(200, 0)
	var v := VBoxContainer.new()
	v.add_theme_constant_override("separation", 3)
	add_child(v)
	_title = Label.new()
	_title.theme_type_variation = "TitleLabel"
	v.add_child(_title)
	var hint := Label.new()
	hint.text = "Clic derecho en la bolsa para vender"
	hint.theme_type_variation = "SmallLabel"
	v.add_child(hint)
	_list = VBoxContainer.new()
	_list.add_theme_constant_override("separation", 1)
	v.add_child(_list)
	var buttons := HBoxContainer.new()
	buttons.alignment = BoxContainer.ALIGNMENT_END
	var junk := Button.new()
	junk.text = "Vender basura"
	junk.pressed.connect(func() -> void: sell_junk_requested.emit())
	buttons.add_child(junk)
	var close := Button.new()
	close.text = "Cerrar"
	close.pressed.connect(close_window)
	buttons.add_child(close)
	v.add_child(buttons)


func show_window(d: Dictionary, vendor_name: String) -> void:
	npc_id = int(d.get("npcId", -1))
	_title.text = vendor_name
	for c: Node in _list.get_children():
		c.queue_free()
	for it: Variant in d.get("items", []):
		var e: Dictionary = it
		var tpl := Content.item(str(e.get("templateId", "")))
		var row := HBoxContainer.new()
		var name := ItemRowLabel.new()
		name.setup(tpl, int(e.get("price", 0)))
		name.tooltip_bbcode = TooltipBuilder.build(tpl, 1, GameState.class_id, GameState.level, _equipped_for(tpl), int(e.get("price", 0)))
		name.tooltip_text = ItemSlot._strip_bbcode(name.tooltip_bbcode)
		row.add_child(name)
		var template_id := str(e.get("templateId", ""))
		for qty: int in [1, 5]:
			var b := Button.new()
			b.text = "x%d" % qty
			b.theme_type_variation = "SmallButton"
			b.pressed.connect(func() -> void: Net.send("VendorBuy", {"npcId": npc_id, "templateId": template_id, "qty": qty}))
			row.add_child(b)
		_list.add_child(row)
	visible = true
	UiTheme.dock(self, Control.PRESET_TOP_LEFT, UiTheme.SCREEN_MARGIN, InventoryWindow.WINDOW_TOP - UiTheme.SCREEN_MARGIN)
	UiTheme.bring_to_front(self)


func close_window() -> void:
	visible = false
	npc_id = -1


## Lo equipado en el hueco del item, para la comparación ▲/▼ del tooltip (igual que en la bolsa).
static func _equipped_for(tpl: Dictionary) -> Dictionary:
	if not tpl.has("slot"):
		return {}
	var index := ItemSlot.slot_index(str(tpl["slot"]))
	if index < 0 or index >= GameState.equipment.size() or not (GameState.equipment[index] is Dictionary):
		return {}
	return Content.item(str((GameState.equipment[index] as Dictionary).get("templateId", "")))


## Ícono, nombre (color de rareza) y precio de un item de la tienda, con el tooltip del item al pasar el ratón (HU-053).
class ItemRowLabel extends HBoxContainer:
	var tooltip_bbcode: String = ""
	var text: String = ""

	func _init() -> void:
		mouse_filter = Control.MOUSE_FILTER_PASS
		size_flags_horizontal = Control.SIZE_EXPAND_FILL
		add_theme_constant_override("separation", 3)

	func setup(tpl: Dictionary, price: int) -> void:
		var pic := PanelContainer.new()
		pic.theme_type_variation = "SlotPanel"
		pic.mouse_filter = Control.MOUSE_FILTER_IGNORE
		var tex := TextureRect.new()
		tex.texture = UiTheme.icon(str(tpl.get("icon", "")))
		tex.custom_minimum_size = Vector2(16, 16)
		tex.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		tex.mouse_filter = Control.MOUSE_FILTER_IGNORE
		pic.add_child(tex)
		add_child(pic)
		var name := Label.new()
		name.text = str(tpl.get("name", "?"))
		name.add_theme_color_override("font_color", ItemSlot.RARITY_COLORS.get(str(tpl.get("rarity", "common")), Color.WHITE))
		name.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		name.mouse_filter = Control.MOUSE_FILTER_IGNORE
		add_child(name)
		var cost := Label.new()
		cost.text = MoneyFormat.format(price)
		cost.add_theme_color_override("font_color", UiTheme.ACCENT)
		cost.mouse_filter = Control.MOUSE_FILTER_IGNORE
		add_child(cost)
		text = "%s · %s" % [name.text, cost.text]

	func _make_custom_tooltip(_for_text: String) -> Object:
		return RichTooltip.make(tooltip_bbcode) if not tooltip_bbcode.is_empty() else null
