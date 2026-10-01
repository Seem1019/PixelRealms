class_name SpellbookWindow
extends PanelContainer
## Libro de hechizos (HU-043): tecla P; todos los hechizos de mi clase, los no aprendidos en gris con "Nivel X"; arrastrar a
## una casilla de hechizo (1–4) → SetHotbar; Shift+arrastrar fuera de la barra quita. Los consumibles se arrastran desde la bolsa
## a las casillas 5–8 (la casilla muestra la cantidad total en bolsa).

var _list: VBoxContainer


func _ready() -> void:
	visible = false
	position = Vector2(280, 40)
	var v := VBoxContainer.new()
	add_child(v)
	var title := Label.new()
	title.text = "Libro de hechizos (arrastra a 1–4)"
	title.add_theme_font_size_override("font_size", 8)
	v.add_child(title)
	_list = VBoxContainer.new()
	v.add_child(_list)
	GameState.stats_changed.connect(refresh)
	GameState.leveled_up.connect(func(_l: int, _n: Array, _r: Array) -> void: refresh())


func toggle() -> void:
	visible = not visible
	if visible:
		refresh()


func refresh() -> void:
	for c: Node in _list.get_children():
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
		var known := GameState.known_spells.has(id)
		var entry := SpellEntry.new()
		entry.spell_id = id
		entry.known = known
		entry.text = "%s%s" % [str(sd.get("name", id)), "" if known else "  (Nivel %d)" % int(sd.get("levelReq", 1))]
		entry.tooltip_text = str(sd.get("description", ""))
		entry.modulate = Color.WHITE if known else Color(0.5, 0.5, 0.5)
		_list.add_child(entry)


## Entrada arrastrable del libro: dato {"kind": "spell", "ref": id}.
class SpellEntry extends Button:
	var spell_id: String = ""
	var known: bool = false

	func _ready() -> void:
		add_theme_font_size_override("font_size", 7)
		alignment = HORIZONTAL_ALIGNMENT_LEFT
		focus_mode = Control.FOCUS_NONE

	func _get_drag_data(_at: Vector2) -> Variant:
		if not known:
			return null
		var preview := Label.new()
		preview.text = text
		set_drag_preview(preview)
		return {"kind": "spell", "ref": spell_id}
