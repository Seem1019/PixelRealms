class_name CombatHud
extends Control
## HUD de combate (HU-038): marco propio, marco de objetivo, barra de casteo, barra 4+4 (rules.loadout), errores del servidor
## 2 s, iconos de auras (gris si no mandan) y pantalla "Has muerto" (HU-037). Solo lee GameState/Content; nunca calcula resultados.

signal respawn_requested
signal hotbar_pressed(slot: int)

const ERROR_SECONDS := 2.0
const RESOURCE_COLORS := {"mana": Color(0.25, 0.45, 1.0), "rage": Color(0.55, 0.05, 0.05), "energy": Color(1.0, 0.85, 0.2)}
const HP_COLOR := Color(0.85, 0.15, 0.15)

var _self_name: Label
var _self_hp: ProgressBar
var _self_res: ProgressBar
var _self_auras: HBoxContainer
var _xp_bar: ProgressBar
var _notice_label: Label
var _notice_until: float = 0.0
var _target_frame: PanelContainer
var _target_name: Label
var _target_hp: ProgressBar
var _target_auras: HBoxContainer
var _cast_bar: ProgressBar
var _cast_label: Label
var _hotbar: HBoxContainer
var _slots: Array[Button] = []
var _slot_sweeps: Array[ProgressBar] = []
var _error_label: Label
var _death_panel: PanelContainer
var _death_killer: Label

var _error_until: float = 0.0
var _cast_end_text_until: float = 0.0
var _target_hp_pct: int = 100
var _target_entity_name: String = ""
var _target_level: int = 0

## El mundo rellena esto para saber el alcance y la selección: callable(slot) → Dictionary {spellId|itemId}.
var slot_resolver: Callable = Callable()
## callable(spell: Dictionary) → bool: ¿el objetivo está en alcance? (solo visual)
var in_range_check: Callable = Callable()


func _ready() -> void:
	set_anchors_preset(Control.PRESET_FULL_RECT)
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	_build()
	GameState.vitals_changed.connect(_refresh_self)
	GameState.stats_changed.connect(_refresh_self)
	GameState.target_changed.connect(_on_target_changed)
	GameState.auras_changed.connect(_on_auras_changed)
	GameState.cast_changed.connect(_on_cast_changed)
	GameState.cooldowns_changed.connect(_refresh_hotbar)
	GameState.died.connect(_on_died)
	GameState.respawned.connect(_on_respawned)
	GameState.xp_changed.connect(_refresh_xp)
	GameState.notice.connect(show_notice)
	GameState.leveled_up.connect(func(_l: int, _n: Array, _r: Array) -> void: _refresh_hotbar())
	EventBus.ui_error.connect(_on_ui_error)
	_refresh_self()
	_refresh_hotbar()
	_on_target_changed(GameState.target_id)


func _build() -> void:
	# Marco propio (arriba izquierda)
	var self_frame := PanelContainer.new()
	self_frame.position = Vector2(4, 4)
	self_frame.custom_minimum_size = Vector2(120, 0)
	add_child(self_frame)
	var sv := VBoxContainer.new()
	sv.add_theme_constant_override("separation", 1)
	self_frame.add_child(sv)
	_self_name = _label("", 8)
	sv.add_child(_self_name)
	_self_hp = _bar(HP_COLOR)
	sv.add_child(_self_hp)
	_self_res = _bar(RESOURCE_COLORS["mana"])
	sv.add_child(_self_res)
	_xp_bar = _bar(Color(0.6, 0.3, 0.9))
	_xp_bar.custom_minimum_size = Vector2(0, 3)
	sv.add_child(_xp_bar)
	_self_auras = HBoxContainer.new()
	_self_auras.add_theme_constant_override("separation", 1)
	sv.add_child(_self_auras)

	# Marco de objetivo (arriba centro-derecha)
	_target_frame = PanelContainer.new()
	_target_frame.position = Vector2(300, 4)
	_target_frame.custom_minimum_size = Vector2(120, 0)
	_target_frame.visible = false
	add_child(_target_frame)
	var tv := VBoxContainer.new()
	tv.add_theme_constant_override("separation", 1)
	_target_frame.add_child(tv)
	_target_name = _label("", 8)
	tv.add_child(_target_name)
	_target_hp = _bar(HP_COLOR)
	tv.add_child(_target_hp)
	_target_auras = HBoxContainer.new()
	_target_auras.add_theme_constant_override("separation", 1)
	tv.add_child(_target_auras)

	# Barra de casteo (centro bajo)
	_cast_bar = _bar(Color(0.9, 0.7, 0.2))
	_cast_bar.position = Vector2(160, 200)
	_cast_bar.custom_minimum_size = Vector2(160, 10)
	_cast_bar.visible = false
	add_child(_cast_bar)
	_cast_label = _label("", 8)
	_cast_label.position = Vector2(160, 188)
	_cast_label.custom_minimum_size = Vector2(160, 10)
	_cast_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_cast_label.visible = false
	add_child(_cast_label)

	# Hotbar 4 + 4 (abajo centro)
	_hotbar = HBoxContainer.new()
	_hotbar.add_theme_constant_override("separation", 2)
	var spell_slots := int(Content.rule("loadout", "spellSlots", 4))
	var usable_slots := int(Content.rule("loadout", "usableSlots", 4))
	var total := spell_slots + usable_slots
	_hotbar.position = Vector2(240 - total * 13, 240)
	add_child(_hotbar)
	for i: int in total:
		var b := Button.new()
		b.custom_minimum_size = Vector2(24, 24)
		b.focus_mode = Control.FOCUS_NONE
		b.add_theme_font_size_override("font_size", 8)
		b.text = str(i + 1)
		b.pressed.connect(_on_slot_pressed.bind(i))
		var sweep := ProgressBar.new()
		sweep.show_percentage = false
		sweep.fill_mode = ProgressBar.FILL_BOTTOM_TO_TOP
		sweep.set_anchors_preset(Control.PRESET_FULL_RECT)
		sweep.modulate = Color(0, 0, 0, 0.6)
		sweep.mouse_filter = Control.MOUSE_FILTER_IGNORE
		sweep.max_value = 1.0
		sweep.value = 0.0
		b.add_child(sweep)
		_slots.append(b)
		_slot_sweeps.append(sweep)
		_hotbar.add_child(b)
		if i == spell_slots - 1:
			var sep := Control.new()
			sep.custom_minimum_size = Vector2(6, 0)
			_hotbar.add_child(sep)

	# Error del servidor (centro arriba, rojo)
	_error_label = _label("", 8)
	_error_label.position = Vector2(120, 30)
	_error_label.custom_minimum_size = Vector2(240, 12)
	_error_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_error_label.add_theme_color_override("font_color", Color(1, 0.3, 0.3))
	add_child(_error_label)

	# Avisos (nivel, hechizo nuevo)
	_notice_label = _label("", 10)
	_notice_label.position = Vector2(120, 60)
	_notice_label.custom_minimum_size = Vector2(240, 14)
	_notice_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_notice_label.add_theme_color_override("font_color", Color(1, 0.95, 0.5))
	add_child(_notice_label)

	# Pantalla de muerte
	_death_panel = PanelContainer.new()
	_death_panel.position = Vector2(170, 100)
	_death_panel.custom_minimum_size = Vector2(140, 60)
	_death_panel.visible = false
	add_child(_death_panel)
	var dv := VBoxContainer.new()
	_death_panel.add_child(dv)
	var title := _label("Has muerto", 16)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	dv.add_child(title)
	_death_killer = _label("", 8)
	_death_killer.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	dv.add_child(_death_killer)
	var respawn := Button.new()
	respawn.text = "Reaparecer"
	respawn.add_theme_font_size_override("font_size", 8)
	respawn.pressed.connect(func() -> void: respawn_requested.emit())
	dv.add_child(respawn)


func _label(text: String, size: int) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_font_size_override("font_size", size)
	return l


func _bar(color: Color) -> ProgressBar:
	var b := ProgressBar.new()
	b.show_percentage = false
	b.custom_minimum_size = Vector2(0, 6)
	var style := StyleBoxFlat.new()
	style.bg_color = color
	b.add_theme_stylebox_override("fill", style)
	var bg := StyleBoxFlat.new()
	bg.bg_color = Color(0.1, 0.1, 0.1, 0.8)
	b.add_theme_stylebox_override("background", bg)
	return b


func _process(_delta: float) -> void:
	var now := Time.get_ticks_msec() / 1000.0
	if _error_until > 0.0 and now >= _error_until:
		_error_until = 0.0
		_error_label.text = ""
	if _notice_until > 0.0 and now >= _notice_until:
		_notice_until = 0.0
		_notice_label.text = ""
	if _cast_end_text_until > 0.0 and now >= _cast_end_text_until:
		_cast_end_text_until = 0.0
		_cast_label.visible = false
		_cast_bar.visible = false
	if not GameState.own_cast.is_empty():
		var c := GameState.own_cast
		var elapsed := Time.get_ticks_msec() - int(c["startedMs"])
		var dur := maxi(1, int(c["durationMs"]))
		_cast_bar.value = clampf(float(elapsed) / float(dur), 0.0, 1.0)
	_refresh_sweeps()
	_refresh_aura_times(_self_auras, GameState.self_id)
	if GameState.target_id > 0:
		_refresh_aura_times(_target_auras, GameState.target_id)


# --- Marco propio --------------------------------------------------------------------------------------------------------

func _refresh_self() -> void:
	_self_name.text = "%s  nv %d" % [GameState.character_name, GameState.level]
	_refresh_xp()
	_self_hp.max_value = maxi(1, GameState.max_hp)
	_self_hp.value = GameState.hp
	_self_res.max_value = maxi(1, GameState.max_resource)
	_self_res.value = GameState.resource
	var style: StyleBoxFlat = _self_res.get_theme_stylebox("fill")
	style.bg_color = RESOURCE_COLORS.get(GameState.resource_kind, RESOURCE_COLORS["mana"])
	_refresh_hotbar()


## HU-040 CA2: barra de XP con `xp / xpNext` y tooltip; "Nivel máximo" en el tope de la fase (CA4).
func _refresh_xp() -> void:
	if _xp_bar == null:
		return
	if GameState.at_level_cap():
		_xp_bar.max_value = 1
		_xp_bar.value = 1
		_xp_bar.tooltip_text = "Nivel máximo"
		_self_name.text = "%s  nv %d · Nivel máximo" % [GameState.character_name, GameState.level]
	else:
		_xp_bar.max_value = maxi(1, GameState.xp_next)
		_xp_bar.value = GameState.xp
		_xp_bar.tooltip_text = "XP %d / %d" % [GameState.xp, GameState.xp_next]


func show_notice(text: String) -> void:
	_notice_label.text = text
	_notice_until = Time.get_ticks_msec() / 1000.0 + 3.0


# --- Objetivo -------------------------------------------------------------------------------------------------------------

func _on_target_changed(entity_id: int) -> void:
	_target_frame.visible = entity_id > 0
	if entity_id > 0:
		_target_hp.max_value = 100
		_target_hp.value = _target_hp_pct
		_target_name.text = _target_entity_name if not _target_entity_name.is_empty() else "#%d" % entity_id
		_rebuild_auras(_target_auras, entity_id)


## El mundo informa del nombre/nivel/vida del objetivo (datos de EntitySpawn + Snapshot).
func set_target_info(display_name: String, level: int, hp_pct: int) -> void:
	_target_entity_name = display_name
	_target_level = level
	_target_hp_pct = hp_pct
	if _target_frame.visible:
		_target_name.text = "%s  nv %d" % [display_name, level] if level > 0 else display_name
		_target_hp.value = hp_pct


# --- Casteo ----------------------------------------------------------------------------------------------------------------

func _on_cast_changed() -> void:
	if GameState.own_cast.is_empty():
		return
	var spell := Content.spell(str(GameState.own_cast["spellId"]))
	_cast_label.text = str(spell.get("name", GameState.own_cast["spellId"]))
	_cast_label.modulate = Color.WHITE
	_cast_label.visible = true
	_cast_bar.visible = true
	_cast_bar.value = 0.0
	_cast_end_text_until = 0.0


## Fin del casteo propio con resultado (lo llama el mundo con CastEnded): "Interrumpido" rojo, "Fuera de alcance" gris, nada si cancela.
func show_cast_result(result: String, reason: String) -> void:
	match result:
		"interrupted":
			_cast_label.text = "Interrumpido"
			_cast_label.modulate = Color(1, 0.3, 0.3)
			_cast_end_text_until = Time.get_ticks_msec() / 1000.0 + 1.0
		"failed":
			_cast_label.text = "Fuera de alcance" if reason == "out_of_range" else "Sin línea de visión"
			_cast_label.modulate = Color(0.6, 0.6, 0.6)
			_cast_end_text_until = Time.get_ticks_msec() / 1000.0 + 1.0
		_:
			_cast_label.visible = false
			_cast_bar.visible = false


# --- Hotbar ------------------------------------------------------------------------------------------------------------------

func _on_slot_pressed(slot: int) -> void:
	hotbar_pressed.emit(slot)


func _refresh_hotbar() -> void:
	for i: int in _slots.size():
		var b := _slots[i]
		var entry := _slot_entry(i)
		if entry.is_empty():
			b.text = str(i + 1)
			b.disabled = true
			b.tooltip_text = ""
			continue
		b.disabled = false
		var ref := str(entry.get("ref", ""))
		if str(entry.get("kind", "spell")) == "spell":
			var spell := Content.spell(ref)
			b.text = "%d\n%s" % [i + 1, _short(str(spell.get("name", ref)))]
			b.tooltip_text = str(spell.get("description", ""))
			var cost: Dictionary = spell.get("cost", {})
			var lacks := not cost.is_empty() and int(cost.get("amount", 0)) > GameState.resource
			var out_of_range := in_range_check.is_valid() and not bool(in_range_check.call(spell))
			b.modulate = Color(0.45, 0.45, 0.45) if lacks or out_of_range else Color.WHITE
		else:
			var item := Content.item(ref)
			b.text = "%d\n%s" % [i + 1, _short(str(item.get("name", ref)))]
			b.modulate = Color.WHITE


func _slot_entry(slot: int) -> Dictionary:
	for h: Variant in GameState.hotbar:
		var hd: Dictionary = h
		if int(hd.get("slot", -1)) == slot:
			return hd
	return {}


func _refresh_sweeps() -> void:
	var gcd := GameState.gcd_remaining_ms()
	var gcd_total := int(Content.rule("combat", "gcdMs", 1000))
	for i: int in _slots.size():
		var entry := _slot_entry(i)
		var frac := 0.0
		if not entry.is_empty() and str(entry.get("kind", "spell")) == "spell":
			var spell_id := str(entry.get("ref", ""))
			var cd := GameState.cooldown_remaining_ms(spell_id)
			var spell := Content.spell(spell_id)
			var total := int(spell.get("cooldownMs", 0))
			if cd > 0 and total > 0:
				frac = float(cd) / float(total)
			elif gcd > 0 and bool(spell.get("triggersGcd", true)):
				frac = float(gcd) / float(gcd_total)
		_slot_sweeps[i].value = frac


static func _short(name: String) -> String:
	return name.substr(0, 6)


# --- Auras ---------------------------------------------------------------------------------------------------------------------

func _on_auras_changed(entity_id: int) -> void:
	if entity_id == GameState.self_id:
		_rebuild_auras(_self_auras, entity_id)
	elif entity_id == GameState.target_id:
		_rebuild_auras(_target_auras, entity_id)


func _rebuild_auras(box: HBoxContainer, entity_id: int) -> void:
	for c: Node in box.get_children():
		c.queue_free()
	var list := GameState.auras_of(entity_id)
	var dominant := _dominant_ids(list)
	for a: Variant in list:
		var ad: Dictionary = a
		var def := Content.aura(str(ad["auraId"]))
		var l := _label("", 7)
		l.custom_minimum_size = Vector2(26, 10)
		l.set_meta("aura", ad)
		var is_dominant: bool = dominant.has(str(ad["auraId"]))
		l.modulate = Color.WHITE if is_dominant else Color(0.55, 0.55, 0.55)
		if bool(def.get("isDebuff", false)):
			l.add_theme_color_override("font_color", Color(1, 0.5, 0.5))
		box.add_child(l)
	_refresh_aura_times(box, entity_id)


## Entre varias ralentizaciones manda la más fuerte y entre bonos de velocidad el mayor (ADR-022): las demás en gris.
static func _dominant_ids(list: Array) -> Dictionary:
	var best_slow := -1.0
	var best_slow_id := ""
	var best_speed := -1.0
	var best_speed_id := ""
	var result := {}
	for a: Variant in list:
		var ad: Dictionary = a
		var def := Content.aura(str(ad["auraId"]))
		var kind := str(def.get("kind", ""))
		var mods: Dictionary = def.get("mods", {}) if def.get("mods") != null else {}
		if kind == "slow":
			var pct := float(def.get("pct", 0.0))
			if pct > best_slow:
				best_slow = pct
				best_slow_id = str(ad["auraId"])
		elif float(mods.get("speedPct", 0.0)) > 0.0:
			var sp := float(mods.get("speedPct", 0.0))
			if sp > best_speed:
				best_speed = sp
				best_speed_id = str(ad["auraId"])
		else:
			result[str(ad["auraId"])] = true
	if not best_slow_id.is_empty():
		result[best_slow_id] = true
	if not best_speed_id.is_empty():
		result[best_speed_id] = true
	return result


func _refresh_aura_times(box: HBoxContainer, _entity_id: int) -> void:
	var now := Time.get_ticks_msec()
	for c: Node in box.get_children():
		var l := c as Label
		if l == null or not l.has_meta("aura"):
			continue
		var ad: Dictionary = l.get_meta("aura")
		var def := Content.aura(str(ad["auraId"]))
		var remaining := maxi(0, int(ad["endsMs"]) - now)
		var stacks := int(ad.get("stacks", 1))
		l.text = "%s %ds%s" % [_short(str(def.get("name", ad["auraId"]))).substr(0, 4), ceili(remaining / 1000.0), "x%d" % stacks if stacks > 1 else ""]


# --- Errores y muerte -----------------------------------------------------------------------------------------------------------

func _on_ui_error(code: String, _req_id: int) -> void:
	var text := Net.last_error_message if not Net.last_error_message.is_empty() else ApiMessages.text_for(code)
	show_error(text)


func show_error(text: String) -> void:
	_error_label.text = text
	_error_until = Time.get_ticks_msec() / 1000.0 + ERROR_SECONDS


func _on_died(killer_id: int) -> void:
	_death_panel.visible = true
	_death_killer.text = "Te mató #%d" % killer_id if killer_id > 0 else ""


func _on_respawned() -> void:
	_death_panel.visible = false
