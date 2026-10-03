class_name CharacterPanel
extends PanelContainer
## Panel de personaje (HU-042): tecla C; muñeco de equipo (9 slots alrededor del sprite, arrastrar desde la bolsa para
## equipar, clic derecho desequipa) y stats agrupados (Recursos, Atributos, Combate) con la etiqueta a la izquierda y el
## valor alineado a la derecha; tooltip de procedencia (base + equipo×afinidad + auras). Cabe entre los marcos y la barra.

const SLOT_NAMES := ["Cabeza", "Cuello", "Pecho", "Manos", "Piernas", "Pies", "Anillo", "Mano ppal.", "Mano sec."]
const SLOT_KEYS := ["head", "neck", "chest", "hands", "legs", "feet", "ring", "main_hand", "off_hand"]
const STAT_KEYS := ["str", "agi", "int", "spi", "sta"]
## Posición de cada hueco en el muñeco (columna, fila): cabeza, cuello y pecho a la izquierda; manos, piernas y pies a la
## derecha; anillo y armas abajo. El sprite ocupa el centro.
const DOLL := [Vector2i(0, 0), Vector2i(0, 1), Vector2i(0, 2), Vector2i(2, 0), Vector2i(2, 1), Vector2i(2, 2), Vector2i(0, 3), Vector2i(1, 3), Vector2i(2, 3)]
const STATS_WIDTH := 96

var _slots: Array[ItemSlot] = []
var _header: Label
var _stats: GridContainer
var _figure: TextureRect


func _ready() -> void:
	visible = false
	var v := VBoxContainer.new()
	v.add_theme_constant_override("separation", 3)
	add_child(v)
	v.add_child(InventoryWindow.title_row("Personaje", "C"))
	_header = Label.new()
	_header.theme_type_variation = "SmallLabel"
	v.add_child(_header)
	var h := HBoxContainer.new()
	h.add_theme_constant_override("separation", 6)
	v.add_child(h)
	var doll := Control.new()
	var cell := ItemSlot.SIZE.x + 1
	doll.custom_minimum_size = Vector2(cell * 3 - 1, cell * 4 - 1)
	h.add_child(doll)
	var stage := PanelContainer.new()
	stage.theme_type_variation = "SlotPanel"
	stage.position = Vector2(cell, 0)
	stage.size = Vector2(cell - 1, cell * 3 - 1)
	doll.add_child(stage)
	_figure = TextureRect.new()
	_figure.stretch_mode = TextureRect.STRETCH_KEEP_CENTERED
	_figure.mouse_filter = Control.MOUSE_FILTER_IGNORE
	stage.add_child(_figure)
	for i: int in 9:
		var s := ItemSlot.new()
		s.container = "equip"
		s.index = i
		s.tooltip_text = SLOT_NAMES[i]
		s.position = Vector2(DOLL[i].x * cell, DOLL[i].y * cell)
		s.dropped.connect(_on_dropped)
		s.right_clicked.connect(_on_right_click)
		doll.add_child(s)
		_slots.append(s)
	_stats = GridContainer.new()
	_stats.columns = 2
	_stats.custom_minimum_size = Vector2(STATS_WIDTH, 0)
	_stats.add_theme_constant_override("v_separation", 0)
	_stats.add_theme_constant_override("h_separation", 4)
	_stats.mouse_filter = Control.MOUSE_FILTER_PASS
	h.add_child(_stats)
	GameState.inventory_changed.connect(refresh)
	GameState.stats_changed.connect(refresh)
	GameState.vitals_changed.connect(refresh)
	UiTheme.dock(self, Control.PRESET_TOP_LEFT, UiTheme.SCREEN_MARGIN, InventoryWindow.WINDOW_TOP - UiTheme.SCREEN_MARGIN)


func toggle() -> void:
	visible = not visible
	if visible:
		refresh()
		UiTheme.bring_to_front(self)


func refresh() -> void:
	for i: int in _slots.size():
		var it: Variant = GameState.equipment[i] if i < GameState.equipment.size() else null
		_slots[i].set_placeholder(SLOT_NAMES[i], SLOT_KEYS[i])
		_slots[i].set_item(it if it is Dictionary else {})
	_header.text = "%s · %s · Nv %d" % [GameState.character_name, UiText.class_name_of(GameState.class_id), GameState.level]
	_figure.texture = EntitySprites.portrait(EntitySprites.ref_for("player", GameState.class_id, GameState.class_id), false)
	var d: Dictionary = GameState.stats.get("derived", {}) if not GameState.stats.is_empty() else {}
	var primary: Dictionary = GameState.stats.get("stats", {}) if not GameState.stats.is_empty() else {}
	var rows: Array = []
	rows.append(["Recursos", ""])
	rows.append(["Vida", "%d/%d" % [GameState.hp, GameState.max_hp]])
	rows.append([UiText.resource(GameState.resource_kind), "%d/%d" % [GameState.resource, GameState.max_resource]])
	rows.append(["Atributos", ""])
	for k: String in STAT_KEYS:
		var total := int(primary.get(k, 0))
		rows.append([str(TooltipBuilder.STAT_NAMES.get(k, k)), "%d" % total, stat_origin(k, GameState.class_id, GameState.level, GameState.equipment, total)])
	if not d.is_empty():
		rows.append(["Combate", ""])
		rows.append(["P. ataque", "%.1f" % float(d.get("attackPower", 0))])
		rows.append(["P. hechizo", "%.1f" % float(d.get("spellPower", 0))])
		rows.append(["Crítico", "%.1f%%" % (float(d.get("critChance", 0)) * 100.0)])
		rows.append(["Esquiva", "%.1f%%" % (float(d.get("dodgeChance", 0)) * 100.0)])
		rows.append(["Armadura", "%.0f (%.0f%%)" % [float(d.get("armor", 0)), float(d.get("mitigation", 0)) * 100.0]])
		rows.append(["Vel. ataque", "×%.2f" % float(d.get("haste", 1.0))])
	_fill_stats(rows)


func _fill_stats(rows: Array) -> void:
	for c: Node in _stats.get_children():
		_stats.remove_child(c)
		c.queue_free()
	for r: Variant in rows:
		var row: Array = r
		var name := Label.new()
		name.text = str(row[0])
		name.mouse_filter = Control.MOUSE_FILTER_PASS
		var value := Label.new()
		value.text = str(row[1])
		value.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		value.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		value.mouse_filter = Control.MOUSE_FILTER_PASS
		if row.size() > 2:
			name.tooltip_text = str(row[2])
			value.tooltip_text = str(row[2])
		if str(row[1]).is_empty():
			name.theme_type_variation = "TitleLabel"  # cabecera de grupo
		else:
			name.add_theme_color_override("font_color", UiTheme.TEXT_MUTED)
		_stats.add_child(name)
		_stats.add_child(value)


## Texto de los stats ("Vida 80/100\nFuerza 12…"), para los tests y lectores.
func stats_text() -> String:
	var parts: Array[String] = []
	var kids := _stats.get_children()
	for i: int in range(0, kids.size() - 1, 2):
		parts.append(("%s %s" % [(kids[i] as Label).text, (kids[i + 1] as Label).text]).strip_edges())
	return "\n".join(parts)


## HU-042 CA2: de dónde sale un stat, para su tooltip: "Base 14 + Equipo 3 (Espada corta: afinidad media ×0.85) + Auras 0".
## Cada pieza que aporta el stat nombra su afinidad; lo que no explican base y equipo lo ponen las auras (el servidor no
## manda el desglose, solo el total).
static func stat_origin(stat: String, class_id: String, level: int, equipment: Array, total: int) -> String:
	var cls := Content.character_class(class_id)
	if cls.is_empty():
		return ""
	var base: Dictionary = cls.get("baseStats", {})
	var per: Dictionary = cls.get("statsPerLevel", {})
	var b := int(base.get(stat, 0)) + int(per.get(stat, 0)) * (level - 1)
	var equip := 0.0
	var pieces: Array[String] = []
	for e: Variant in equipment:
		if not (e is Dictionary):
			continue
		var tpl := Content.item(str((e as Dictionary).get("templateId", "")))
		var st: Dictionary = tpl.get("stats", {}) if tpl.get("stats") != null else {}
		if float(st.get(stat, 0)) == 0.0:
			continue
		var affinity := TooltipBuilder.affinity_of(class_id, tpl)
		var mult := TooltipBuilder.affinity_mult(affinity)
		equip += float(st.get(stat, 0)) * mult
		pieces.append("%s: afinidad %s ×%s" % [str(tpl.get("name", "")), affinity, TooltipBuilder._mult(mult)] if not affinity.is_empty() else str(tpl.get("name", "")))
	var equip_text := TooltipBuilder._num(equip)
	if not pieces.is_empty():
		equip_text += " (%s)" % ", ".join(pieces)
	return "Base %d + Equipo %s + Auras %s" % [b, equip_text, TooltipBuilder._num(float(total) - b - equip)]


func _on_dropped(from: Dictionary, to: Dictionary, _qty: int) -> void:
	Net.send("InventoryMove", {"from": from, "to": to, "reqId": Net.next_req_id()})


func _on_right_click(slot: ItemSlot) -> void:
	# Desequipar al primer hueco libre.
	for i: int in 24:
		var it: Variant = GameState.inventory[i] if i < GameState.inventory.size() else null
		if it == null:
			Net.send("InventoryMove", {"from": {"c": "equip", "i": slot.index}, "to": {"c": "bag", "i": i}, "reqId": Net.next_req_id()})
			return
	GameState.notice.emit("Bolsa llena")
