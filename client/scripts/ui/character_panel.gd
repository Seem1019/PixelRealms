class_name CharacterPanel
extends PanelContainer
## Panel de personaje (HU-042): tecla C; muñeco de equipo (9 slots, arrastrar desde la bolsa para equipar, clic derecho
## desequipa), stats primarios y derivados (StatsUpdate) con tooltip de procedencia (base + equipo×afinidad + auras).

const SLOT_NAMES := ["Cabeza", "Cuello", "Pecho", "Manos", "Piernas", "Pies", "Anillo", "Mano ppal.", "Mano sec."]
const STAT_KEYS := ["str", "agi", "int", "spi", "sta"]

var _slots: Array[ItemSlot] = []
var _stats: Label


func _ready() -> void:
	visible = false
	var h := HBoxContainer.new()
	add_child(h)
	var grid := GridContainer.new()
	grid.columns = 3
	h.add_child(grid)
	for i: int in 9:
		var s := ItemSlot.new()
		s.container = "equip"
		s.index = i
		s.tooltip_text = SLOT_NAMES[i]
		s.dropped.connect(_on_dropped)
		s.right_clicked.connect(_on_right_click)
		grid.add_child(s)
		_slots.append(s)
	_stats = Label.new()
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
		_slots[i].set_placeholder(SLOT_NAMES[i])
		_slots[i].set_item(it if it is Dictionary else {})
	var d: Dictionary = GameState.stats.get("derived", {}) if not GameState.stats.is_empty() else {}
	var primary: Dictionary = GameState.stats.get("stats", {}) if not GameState.stats.is_empty() else {}
	var lines: Array[String] = ["%s · %s · nv %d" % [GameState.character_name, UiText.class_name_of(GameState.class_id), GameState.level]]
	lines.append("Vida %d / %d" % [GameState.hp, GameState.max_hp])
	lines.append("%s %d / %d" % [UiText.resource(GameState.resource_kind), GameState.resource, GameState.max_resource])
	for k: String in STAT_KEYS:
		lines.append("%s %d" % [str(TooltipBuilder.STAT_NAMES.get(k, k)), int(primary.get(k, 0))])
	if not d.is_empty():
		lines.append("Poder de ataque %.1f" % float(d.get("attackPower", 0)))
		lines.append("Poder de hechizo %.1f" % float(d.get("spellPower", 0)))
		lines.append("Crítico %.1f %%" % (float(d.get("critChance", 0)) * 100.0))
		lines.append("Esquiva %.1f %%" % (float(d.get("dodgeChance", 0)) * 100.0))
		lines.append("Armadura %.0f (%.0f %% mitigación)" % [float(d.get("armor", 0)), float(d.get("mitigation", 0)) * 100.0])
		lines.append("Velocidad de ataque ×%.2f" % float(d.get("haste", 1.0)))
	_stats.text = "\n".join(lines)
	_stats.tooltip_text = _origin_tooltip(primary)


## HU-042 CA2: de dónde sale cada stat: base de clase + nivel, equipo × afinidad, auras (texto).
func _origin_tooltip(primary: Dictionary) -> String:
	var cls := Content.character_class(GameState.class_id)
	if cls.is_empty():
		return ""
	var base: Dictionary = cls.get("baseStats", {})
	var per: Dictionary = cls.get("statsPerLevel", {})
	var out: Array[String] = []
	for k: String in STAT_KEYS:
		var b := int(base.get(k, 0)) + int(per.get(k, 0)) * (GameState.level - 1)
		var equip := 0.0
		for e: Variant in GameState.equipment:
			if e is Dictionary:
				var tpl := Content.item(str((e as Dictionary).get("templateId", "")))
				var st: Dictionary = tpl.get("stats", {}) if tpl.get("stats") != null else {}
				equip += float(st.get(k, 0)) * TooltipBuilder.affinity_mult(TooltipBuilder.affinity_of(GameState.class_id, tpl))
		var auras := float(primary.get(k, 0)) - b - equip
		out.append("%s: Base %d + Equipo %.1f + Auras %.1f" % [str(TooltipBuilder.STAT_NAMES.get(k, k)), b, equip, auras])
	return "\n".join(out)


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
