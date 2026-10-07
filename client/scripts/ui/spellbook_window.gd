class_name SpellbookWindow
extends PanelContainer
## Libro de hechizos (HU-043): tecla P; todos los hechizos de mi clase en filas de ícono + nombre completo + nivel
## requerido (los no aprendidos atenuados); arrastrar a una casilla de hechizo (1–4) → SetHotbar; Shift+arrastrar fuera de
## la barra quita. Los consumibles se arrastran desde la bolsa a las casillas 5–8 (la casilla muestra la cantidad en bolsa).
## La lista tiene alto máximo (no tapa la barra rápida) y el detalle del hechizo sale al lado de la ventana, no encima.
## HU-103: con más hechizos que casillas, la cabecera cuenta los equipados, los equipados llevan su tecla sobre el ícono,
## los aprendidos sin equipar van en gris claro y los que no cupieron al aprenderlos salen en dorado hasta cerrar el libro.
## HU-105: desde `spellUpgradeLevel`, bajo cada hechizo aprendido con mejoras van sus dos mejoras (la elegida marcada; su detalle
## dice qué números cambian); pulsar la otra la cambia gratis (ChooseSpellUpgrade). En combate están desactivadas. Un "!" sobre el
## ícono señala los que aún no tienen mejora elegida, y el detalle del hechizo enseña sus números con la mejora.

const WIDTH := 168
const ROW_H := 18

var _list: VBoxContainer
var _count: Label
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
	_count = Label.new()
	_count.theme_type_variation = "SmallLabel"
	v.add_child(_count)
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
	GameState.hotbar_changed.connect(_on_hotbar_changed)
	GameState.spell_upgrades_changed.connect(_on_hotbar_changed)
	visibility_changed.connect(_on_visibility_changed)


func _on_hotbar_changed() -> void:
	if visible:
		refresh()


## Al cerrar se dan por vistos los hechizos nuevos (HU-103 CA1).
func _on_visibility_changed() -> void:
	_tip.visible = false
	if not visible:
		GameState.unseen_spells.clear()


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
	var known := 0
	var equipped := 0
	var rows := 0
	var can_upgrade := GameState.level >= GameState.spell_upgrade_level()
	var in_combat := GameState.is_in_combat()
	for s: Variant in spells:
		var sd: Dictionary = s
		var id := str(sd.get("id", ""))
		var entry := SpellEntry.new()
		entry.spell_id = id
		entry.known = GameState.known_spells.has(id)
		entry.slot = GameState.spell_slot_of(id) if entry.known else -1
		entry.is_new = entry.known and GameState.unseen_spells.has(id)
		entry.pending = GameState.upgrade_pending(id)
		entry.setup(GameState.effective_spell(id) if entry.known else sd)
		if entry.known:
			known += 1
			if entry.slot >= 0:
				equipped += 1
		entry.hovered.connect(func(e: SpellEntry) -> void: _show_tip(e, e.tooltip_bbcode))
		entry.unhovered.connect(func() -> void: _tip.visible = false)
		_list.add_child(entry)
		rows += 1
		var upgrades: Array = sd.get("upgrades", [])
		if entry.known and can_upgrade and not upgrades.is_empty():
			_list.add_child(_upgrade_row(sd, in_combat))
			rows += 1
	_count.text = "Equipados %d/%d · arrastra a 1–%d" % [equipped, known, int(Content.rule("loadout", "spellSlots", 4))]
	# Alto en filas enteras: nunca se ve media fila cortada abajo.
	rows = mini(rows, floori((_max_list_height() + 1.0) / (ROW_H + 1)))
	_scroll.custom_minimum_size = Vector2(WIDTH - 12, rows * (ROW_H + 1) - 1)
	UiTheme.dock(self, Control.PRESET_TOP_LEFT, UiTheme.SCREEN_MARGIN, InventoryWindow.WINDOW_TOP - UiTheme.SCREEN_MARGIN)
	reset_size()


## HU-105: las dos mejoras de un hechizo; la elegida va pulsada. Su detalle: nombre, descripción y "antes → después".
func _upgrade_row(sd: Dictionary, in_combat: bool) -> HBoxContainer:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 2)
	row.custom_minimum_size = Vector2(0, ROW_H)
	var spell_id := str(sd.get("id", ""))
	var chosen := str(GameState.spell_upgrades.get(spell_id, ""))
	for u: Variant in sd.get("upgrades", []):
		var ud: Dictionary = u
		var up_id := str(ud.get("id", ""))
		var b := UpgradeButton.new()
		b.spell_id = spell_id
		b.upgrade_id = up_id
		b.text = str(ud.get("name", up_id))
		b.toggle_mode = true
		b.button_pressed = up_id == chosen
		b.disabled = in_combat
		b.focus_mode = Control.FOCUS_NONE
		b.clip_text = true
		b.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
		b.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		b.theme_type_variation = "SmallButton"
		var lines: Array[String] = ["[color=#ffdb6b][b]%s[/b][/color]" % b.text, str(ud.get("description", ""))]
		for line: String in SpellUpgrades.diff_lines(sd, SpellUpgrades.apply(sd, up_id)):
			lines.append("[color=#44dd44]%s[/color]" % line)
		if up_id == chosen:
			lines.append("[color=%s]Elegida[/color]" % TooltipBuilder.MUTED)
		if in_combat:
			lines.append("[color=#ff5555]No puedes cambiar mejoras en combate[/color]")
		b.tooltip_bbcode = "\n".join(lines)
		b.mouse_entered.connect(func() -> void: _show_tip(b, b.tooltip_bbcode))
		b.mouse_exited.connect(func() -> void: _tip.visible = false)
		b.pressed.connect(func() -> void: _on_upgrade_pressed(b))
		row.add_child(b)
	return row


## Pulsar la elegida no hace nada; la otra la cambia (gratis, fuera de combate). La marca la pone el SpellUpgradesUpdate.
func _on_upgrade_pressed(b: UpgradeButton) -> void:
	b.set_pressed_no_signal(str(GameState.spell_upgrades.get(b.spell_id, "")) == b.upgrade_id)
	if b.button_pressed:
		return
	GameState.choose_upgrade(b.spell_id, b.upgrade_id)


## Detalle a la derecha de la ventana (o a la izquierda si no cabe), a la altura de la fila y dentro de la pantalla.
func _show_tip(anchor: Control, bbcode: String) -> void:
	_tip_label.text = bbcode
	_tip_label.custom_minimum_size.x = UiTheme.TOOLTIP_MAX_WIDTH
	_tip.visible = true
	_tip.reset_size()
	var base := UiTheme.base_size()
	var size := _tip.get_combined_minimum_size()
	var rect := get_global_rect()
	var x := rect.end.x + 2.0
	if x + size.x > base.x - UiTheme.SCREEN_MARGIN:
		x = rect.position.x - size.x - 2.0
	var y := clampf(anchor.get_global_rect().position.y, UiTheme.SCREEN_MARGIN, base.y - UiTheme.SCREEN_MARGIN - size.y)
	_tip.global_position = Vector2(x, y).round()


## Botón de una mejora (HU-105).
class UpgradeButton extends Button:
	var spell_id: String = ""
	var upgrade_id: String = ""
	var tooltip_bbcode: String = ""


## Entrada arrastrable del libro: ícono, nombre completo y "Nv X" alineado a la derecha; dato {"kind": "spell", "ref": id}.
class SpellEntry extends Button:
	signal hovered(entry: SpellEntry)
	signal unhovered

	var spell_id: String = ""
	var known: bool = false
	var slot: int = -1  ## casilla de la barra (0–3) si está equipado
	var is_new: bool = false  ## aprendido sin sitio en la barra y aún sin ver (HU-103)
	var pending: bool = false  ## con mejoras y ninguna elegida (HU-105)
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
		elif is_new:
			_name.add_theme_color_override("font_color", UiTheme.ACCENT)
		elif slot < 0:
			_name.add_theme_color_override("font_color", UiTheme.TEXT_MUTED)
		if pending:
			var mark := Label.new()
			mark.text = "!"
			mark.theme_type_variation = "SmallLabel"
			mark.add_theme_color_override("font_color", UiTheme.ACCENT)
			mark.add_theme_color_override("font_outline_color", UiTheme.OUTLINE)
			mark.add_theme_constant_override("outline_size", 2)
			mark.position = Vector2(0, -2)
			mark.mouse_filter = Control.MOUSE_FILTER_IGNORE
			add_child(mark)
		if slot >= 0:
			var key := Label.new()
			key.text = str(slot + 1)
			key.theme_type_variation = "SmallLabel"
			key.add_theme_color_override("font_color", UiTheme.ACCENT)
			key.add_theme_color_override("font_outline_color", UiTheme.OUTLINE)
			key.add_theme_constant_override("outline_size", 2)
			key.position = Vector2(12, 7)
			key.mouse_filter = Control.MOUSE_FILTER_IGNORE
			add_child(key)
		mouse_entered.connect(func() -> void: hovered.emit(self))
		mouse_exited.connect(func() -> void: unhovered.emit())

	## Texto visible de la fila (nombre completo y nivel), para los tests.
	func row_text() -> String:
		return "%s %s" % [_name.text, _level.text]

	## Color del nombre: dice si está equipado, aprendido sin equipar, nuevo o sin aprender (para los tests).
	func name_color() -> Color:
		return _name.get_theme_color("font_color")

	func _get_drag_data(_at: Vector2) -> Variant:
		if not known:
			return null
		set_drag_preview(ItemSlot.drag_preview(icon_tex))
		return {"kind": "spell", "ref": spell_id}
