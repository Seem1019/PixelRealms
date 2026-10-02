class_name LootWindow
extends PanelContainer
## Ventana de botín (HU-050 CA2/CA3): oro e items con color de rareza y nombre del dueño; los ajenos atenuados; "Tomar todo".

var loot_id: int = -1

var _list: VBoxContainer
var _gold: Label
var _names: Dictionary = {}  # entity_id → nombre (lo rellena el mundo)


func _ready() -> void:
	visible = false
	var v := VBoxContainer.new()
	add_child(v)
	_gold = Label.new()
	v.add_child(_gold)
	_list = VBoxContainer.new()
	v.add_child(_list)
	var all := Button.new()
	all.text = "Tomar todo"
	all.pressed.connect(func() -> void: Net.send("LootTakeAll", {"lootId": loot_id}))
	v.add_child(all)
	var close := Button.new()
	close.text = "Cerrar"
	close.pressed.connect(func() -> void: visible = false)
	v.add_child(close)


func show_window(d: Dictionary, names: Dictionary) -> void:
	_names = names
	loot_id = int(d.get("lootId", -1))
	for c: Node in _list.get_children():
		c.queue_free()
	var gold := int(d.get("gold", 0))
	_gold.text = "Oro: %s" % MoneyFormat.format(gold) if gold > 0 else "Sin oro para ti"
	var items: Array = d.get("items", [])
	for it: Variant in items:
		var e: Dictionary = it
		var tpl := Content.item(str(e.get("templateId", "")))
		var b := LootButton.new()
		var owner_id := int(e.get("ownerId", 0))
		var mine := owner_id == GameState.self_id
		var free := int(e.get("freeInMs", 0)) <= 0
		var owner_name: String = str(_names.get(owner_id, "#%d" % owner_id)) if owner_id > 0 else "—"
		b.text = "%s ×%d  (%s)" % [str(tpl.get("name", "?")), int(e.get("qty", 1)), "tuyo" if mine else owner_name]
		b.add_theme_color_override("font_color", ItemSlot.RARITY_COLORS.get(str(tpl.get("rarity", "common")), Color.WHITE))
		if not mine and not free:
			b.modulate.a = 0.45
		b.tooltip_bbcode = TooltipBuilder.build(tpl, int(e.get("qty", 1)), GameState.class_id, GameState.level)
		b.tooltip_text = ItemSlot._strip_bbcode(b.tooltip_bbcode)
		var index := int(e.get("index", 0))
		b.pressed.connect(func() -> void: Net.send("LootTake", {"lootId": loot_id, "index": index}))
		_list.add_child(b)
	visible = true
	if items.is_empty() and gold <= 0:
		visible = false
	UiTheme.dock(self, Control.PRESET_CENTER)
	UiTheme.bring_to_front(self)


## Fila del botín con el tooltip propio del item.
class LootButton extends Button:
	var tooltip_bbcode: String = ""

	func _make_custom_tooltip(_for_text: String) -> Object:
		return RichTooltip.make(tooltip_bbcode) if not tooltip_bbcode.is_empty() else null
