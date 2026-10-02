class_name VendorWindow
extends PanelContainer
## Ventana del vendedor (HU-055): items de vendors.json con precio; comprar (1 o 5); "Vender basura"; se cierra al alejarse > 3 casillas.

signal sell_junk_requested

var npc_id: int = -1

var _list: VBoxContainer
var _title: Label


func _ready() -> void:
	visible = false
	var v := VBoxContainer.new()
	add_child(v)
	_title = Label.new()
	_title.theme_type_variation = "TitleLabel"
	v.add_child(_title)
	_list = VBoxContainer.new()
	v.add_child(_list)
	var junk := Button.new()
	junk.text = "Vender basura"
	junk.pressed.connect(func() -> void: sell_junk_requested.emit())
	v.add_child(junk)
	var close := Button.new()
	close.text = "Cerrar"
	close.pressed.connect(close_window)
	v.add_child(close)


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
		name.text = "%s · %s" % [str(tpl.get("name", "?")), MoneyFormat.format(int(e.get("price", 0)))]
		name.tooltip_bbcode = TooltipBuilder.build(tpl, 1, GameState.class_id, GameState.level, _equipped_for(tpl), int(e.get("price", 0)))
		name.tooltip_text = ItemSlot._strip_bbcode(name.tooltip_bbcode)
		name.add_theme_color_override("font_color", ItemSlot.RARITY_COLORS.get(str(tpl.get("rarity", "common")), Color.WHITE))
		row.add_child(name)
		var template_id := str(e.get("templateId", ""))
		for qty: int in [1, 5]:
			var b := Button.new()
			b.text = "x%d" % qty
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


## Nombre y precio de un item de la tienda con el tooltip del item al pasar el ratón (HU-053 en la tienda).
class ItemRowLabel extends Label:
	var tooltip_bbcode: String = ""

	func _init() -> void:
		mouse_filter = Control.MOUSE_FILTER_PASS
		size_flags_horizontal = Control.SIZE_EXPAND_FILL

	func _make_custom_tooltip(_for_text: String) -> Object:
		return RichTooltip.make(tooltip_bbcode) if not tooltip_bbcode.is_empty() else null
