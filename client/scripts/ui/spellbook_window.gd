class_name SpellbookWindow
extends PanelContainer
## Libro de hechizos (HU-043): tecla P; todos los hechizos de mi clase en filas de ícono + nombre completo + nivel
## requerido (los no aprendidos atenuados); arrastrar a una casilla de hechizo (1–4) → SetHotbar; Shift+arrastrar fuera de
## la barra quita. Los consumibles se arrastran desde la bolsa a las casillas 5–8 (la casilla muestra la cantidad en bolsa).
## La lista tiene alto máximo (no tapa la barra rápida) y el detalle del hechizo sale al lado de la ventana, no encima.

const WIDTH := 168
const ROW_H := 18

var _list: VBoxContainer
var _scroll: ScrollContainer
var _tip: PanelContainer
var _tip_label: RichTextLabel


func _ready() -> void:
	visible = false
	custom_minimum_size = Vector2(WIDTH, 0)
	var v := VBoxContainer.new()
	v.add_theme_constant_override("separation", 3)
	add_child(v)
	v.add_child(InventoryWindow.title_row("Hechizos", "P"))
	var hint := Label.new()
	hint.text = "Arrastra a las casillas 1–4"
	hint.theme_type_variation = "SmallLabel"
	v.add_child(hint)
	_scroll = ScrollContainer.new()
	_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	v.add_child(_scroll)
	_list = VBoxContainer.new()
	_list.add_theme_constant_override("separation", 1)
	_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_scroll.add_child(_list)
	# Detalle del hechizo: panel propio al lado de la ventana (top_level para salir de su rectángulo).
	_tip = PanelContainer.new()
	_tip.top_level = true
	_tip.visible = false
	_tip.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_tip.add_theme_stylebox_override("panel", UiTheme.nine("tooltip", 4, 4, UiTheme.TOOLTIP_BG))
	add_child(_tip)
	_tip_label = RichTooltip.make("") as RichTextLabel
	_tip.add_child(_tip_label)
	GameState.stats_changed.connect(refresh)
	GameState.leveled_up.connect(func(_l: int, _n: Array, _r: Array) -> void: refresh())
	visibility_changed.connect(func() -> void: _tip.visible = false)


func toggle() -> void:
	visible = not visible
	if visible:
		refresh()
		UiTheme.bring_to_front(self)


## Alto disponible entre los marcos y la barra rápida para la lista.
func _max_list_height() -> float:
	var base := UiTheme.base_size()
	return base.y - InventoryWindow.WINDOW_TOP - CombatHud.HOTBAR_HEIGHT - 2 * UiTheme.SCREEN_MARGIN - 40.0


func refresh() -> void:
	for c: Node in _list.get_children():
		_list.remove_child(c)
		c.queue_free()
	var spells: Array = []
	for s: Variant in Content._by_id.get("spells", {}).values():
		var sd: Dictionary = s
		if str(sd.get("classId", "")) == GameState.class_id:
			spells.append(sd)
	spells.sort_custom(func(a: Dictionary, b: Dictionary) -> bool: return int(a.get("levelReq", 1)) < int(b.get("levelReq", 1)))
	for s: Variant in spells:
		var sd: Dictionary = s
		var id := str(sd.get("id", ""))
		var entry := SpellEntry.new()
		entry.spell_id = id
		entry.known = GameState.known_spells.has(id)
		entry.setup(sd)
		entry.hovered.connect(_show_tip)
		entry.unhovered.connect(func() -> void: _tip.visible = false)
		_list.add_child(entry)
	# Alto en filas enteras: nunca se ve media fila cortada abajo.
	var rows := mini(spells.size(), floori((_max_list_height() + 1.0) / (ROW_H + 1)))
	_scroll.custom_minimum_size = Vector2(WIDTH - 12, rows * (ROW_H + 1) - 1)
	UiTheme.dock(self, Control.PRESET_TOP_LEFT, UiTheme.SCREEN_MARGIN, InventoryWindow.WINDOW_TOP - UiTheme.SCREEN_MARGIN)
	reset_size()


## Detalle a la derecha de la ventana (o a la izquierda si no cabe), a la altura de la fila y dentro de la pantalla.
func _show_tip(entry: SpellEntry) -> void:
	_tip_label.text = entry.tooltip_bbcode
	_tip_label.custom_minimum_size.x = UiTheme.TOOLTIP_MAX_WIDTH
	_tip.visible = true
	_tip.reset_size()
	var base := UiTheme.base_size()
	var size := _tip.get_combined_minimum_size()
	var rect := get_global_rect()
	var x := rect.end.x + 2.0
	if x + size.x > base.x - UiTheme.SCREEN_MARGIN:
		x = rect.position.x - size.x - 2.0
	var y := clampf(entry.get_global_rect().position.y, UiTheme.SCREEN_MARGIN, base.y - UiTheme.SCREEN_MARGIN - size.y)
	_tip.global_position = Vector2(x, y).round()


## Entrada arrastrable del libro: ícono, nombre completo y "Nv X" alineado a la derecha; dato {"kind": "spell", "ref": id}.
class SpellEntry extends Button:
	signal hovered(entry: SpellEntry)
	signal unhovered

	var spell_id: String = ""
	var known: bool = false
	var tooltip_bbcode: String = ""
	var icon_tex: Texture2D
	var _name: Label
	var _level: Label

	func setup(sd: Dictionary) -> void:
		custom_minimum_size = Vector2(0, SpellbookWindow.ROW_H)
		focus_mode = Control.FOCUS_NONE
		flat = true
		tooltip_bbcode = TooltipBuilder.build_spell(sd)
		icon_tex = UiTheme.icon(str(sd.get("icon", "")))
		var pic := TextureRect.new()
		pic.texture = icon_tex
		pic.position = Vector2(1, 1)
		pic.size = Vector2(16, 16)
		pic.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		pic.mouse_filter = Control.MOUSE_FILTER_IGNORE
		add_child(pic)
		_name = Label.new()
		_name.text = str(sd.get("name", spell_id))
		_name.position = Vector2(21, 4)
		_name.mouse_filter = Control.MOUSE_FILTER_IGNORE
		add_child(_name)
		_level = Label.new()
		_level.text = "Nv %d" % int(sd.get("levelReq", 1))
		_level.theme_type_variation = "SmallLabel"
		_level.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		_level.set_anchors_and_offsets_preset(Control.PRESET_CENTER_RIGHT)
		_level.grow_horizontal = Control.GROW_DIRECTION_BEGIN
		_level.offset_right = -3
		_level.mouse_filter = Control.MOUSE_FILTER_IGNORE
		add_child(_level)
		if not known:
			pic.modulate = Color(0.45, 0.42, 0.5)
			_name.add_theme_color_override("font_color", UiTheme.TEXT_DISABLED)
			_level.add_theme_color_override("font_color", UiTheme.ERROR)
		mouse_entered.connect(func() -> void: hovered.emit(self))
		mouse_exited.connect(func() -> void: unhovered.emit())

	## Texto visible de la fila (nombre completo y nivel), para los tests.
	func row_text() -> String:
		return "%s %s" % [_name.text, _level.text]

	func _get_drag_data(_at: Vector2) -> Variant:
		if not known:
			return null
		set_drag_preview(ItemSlot.drag_preview(icon_tex))
		return {"kind": "spell", "ref": spell_id}
