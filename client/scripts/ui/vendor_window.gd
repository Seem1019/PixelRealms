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
		var name := Label.new()
		name.text = "%s · %s" % [str(tpl.get("name", "?")), MoneyFormat.format(int(e.get("price", 0)))]
		name.tooltip_text = ItemSlot._strip_bbcode(TooltipBuilder.build(tpl, 1, GameState.class_id, GameState.level))
		name.modulate = ItemSlot.RARITY_COLORS.get(str(tpl.get("rarity", "common")), Color.WHITE)
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
