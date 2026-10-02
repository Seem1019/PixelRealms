class_name CombatHud
extends Control
## HUD de combate (HU-038): marco propio, marco de objetivo, barra de casteo, barra 4+4 (rules.loadout), errores del servidor
## 2 s, iconos de auras (gris si no mandan) y pantalla "Has muerto" (HU-037). Solo lee GameState/Content; nunca calcula resultados.
## Los marcos llevan retrato (ícono de clase o cara del sprite), barras con marco y el valor en texto ("80/100").

signal respawn_requested
signal hotbar_pressed(slot: int)
signal menu_requested  ## engranaje de arriba a la derecha (HU-015)

const ERROR_SECONDS := 2.0
const ERROR_FADE := 0.4
const FRAME_WIDTH := 132
## Marco propio: retrato de 22×22 y barras a su derecha; la XP va debajo de todo el marco.
const PORTRAIT := 22
const BAR_X := PORTRAIT + 2
const HOTBAR_SLOT := 34
const HOTBAR_HEIGHT := HOTBAR_SLOT + 6
## Alto que ocupan los marcos de arriba: las ventanas empiezan debajo.
const FRAMES_BOTTOM := 40

var _self_name: Label
var _self_portrait: TextureRect
var _self_hp: ProgressBar
var _self_hp_text: Label
var _self_res: ProgressBar
var _self_res_text: Label
var _self_auras: HBoxContainer
var _xp_bar: ProgressBar
var _xp_frame: Control
var _notice_label: Label
var _notice_until: float = 0.0
var _target_frame: Control
var _target_name: Label
var _target_portrait: TextureRect
var _target_hp: ProgressBar
var _target_hp_text: Label
var _target_auras: HBoxContainer
var _cast_frame: Control
var _cast_bar: ProgressBar
var _cast_label: Label
var _hotbar_panel: PanelContainer
var _hotbar: HBoxContainer
var _slots: Array[HotSlot] = []
var _toast: PanelContainer
var _error_label: Label
var _death_panel: PanelContainer
var _death_killer: Label
var _menu_button: Button
## Capa propia por encima de las ventanas para los avisos (errores, nivel), siempre en el mismo sitio.
var _top_layer: CanvasLayer

var _error_until: float = 0.0
var _cast_end_text_until: float = 0.0
var _target_hp_pct: int = 100
var _target_entity_name: String = ""
var _target_level: int = 0
var _target_portrait_ref: Texture2D

## El mundo rellena esto para saber el alcance y la selección: callable(slot) → Dictionary {spellId|itemId}.
var slot_resolver: Callable = Callable()
## callable(spell: Dictionary) → bool: ¿el objetivo está en alcance? (solo visual)
var in_range_check: Callable = Callable()


func _ready() -> void:
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)  # tamaño de la pantalla: los hijos se centran respecto a él
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	_build()
	GameState.vitals_changed.connect(_refresh_self)
	GameState.stats_changed.connect(_refresh_self)
	GameState.target_changed.connect(_on_target_changed)
	GameState.auras_changed.connect(_on_auras_changed)
	GameState.cast_changed.connect(_on_cast_changed)
	GameState.cooldowns_changed.connect(_refresh_hotbar)
	GameState.inventory_changed.connect(_refresh_hotbar)
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
	var m := UiTheme.SCREEN_MARGIN
	var base := UiTheme.base_size()
	_top_layer = CanvasLayer.new()
	_top_layer.layer = 5
	add_child(_top_layer)

	# Marco propio (arriba izquierda): retrato, nombre, vida, recurso y XP.
	var self_frame := Control.new()
	self_frame.position = Vector2(m, m)
	self_frame.size = Vector2(FRAME_WIDTH, FRAMES_BOTTOM - m)
	self_frame.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(self_frame)
	_self_portrait = _portrait(self_frame, Vector2.ZERO)
	_self_name = _outlined_label(self_frame, Vector2(BAR_X + 1, -2), FRAME_WIDTH - BAR_X)
	var hp := _bar_with_text(self_frame, "hp", Vector2(BAR_X, 7), FRAME_WIDTH - BAR_X, 7)
	_self_hp = hp[0]
	_self_hp_text = hp[1]
	var res := _bar_with_text(self_frame, "mana", Vector2(BAR_X, 18), FRAME_WIDTH - BAR_X, 5)
	_self_res = res[0]
	_self_res_text = res[1]
	_xp_frame = UiTheme.framed_bar("xp", 1)
	_xp_frame.mouse_filter = Control.MOUSE_FILTER_PASS
	self_frame.add_child(_xp_frame)
	_place(_xp_frame, Vector2(0, 28), Vector2(FRAME_WIDTH, 5))
	_xp_bar = _xp_frame.get_child(0) as ProgressBar
	_self_auras = HBoxContainer.new()
	_self_auras.add_theme_constant_override("separation", 1)
	_self_auras.position = Vector2(0, 35)
	self_frame.add_child(_self_auras)

	# Marco de objetivo (a la derecha del propio): retrato con la cara del sprite y vida en %.
	_target_frame = Control.new()
	_target_frame.position = Vector2(m + FRAME_WIDTH + 3 * m, m)
	_target_frame.size = Vector2(FRAME_WIDTH, FRAMES_BOTTOM - m)
	_target_frame.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_target_frame.visible = false
	add_child(_target_frame)
	_target_portrait = _portrait(_target_frame, Vector2.ZERO)
	_target_name = _outlined_label(_target_frame, Vector2(BAR_X + 1, -2), FRAME_WIDTH - BAR_X)
	var thp := _bar_with_text(_target_frame, "enemy", Vector2(BAR_X, 7), FRAME_WIDTH - BAR_X, 7)
	_target_hp = thp[0]
	_target_hp_text = thp[1]
	_target_auras = HBoxContainer.new()
	_target_auras.add_theme_constant_override("separation", 1)
	_target_auras.position = Vector2(BAR_X, 20)
	_target_frame.add_child(_target_auras)

	# Barra rápida 4 + 4 (abajo centro) sobre su propio panel.
	var spell_slots := int(Content.rule("loadout", "spellSlots", 4))
	var usable_slots := int(Content.rule("loadout", "usableSlots", 4))
	var total := spell_slots + usable_slots
	_hotbar_panel = PanelContainer.new()
	_hotbar_panel.theme_type_variation = "LightPanel"
	_hotbar_panel.add_theme_stylebox_override("panel", UiTheme.nine("panel_light", 7, 3))
	add_child(_hotbar_panel)
	_hotbar = HBoxContainer.new()
	_hotbar.add_theme_constant_override("separation", UiTheme.GAP)
	_hotbar_panel.add_child(_hotbar)
	for i: int in total:
		var b := HotSlot.new()
		b.slot = i
		b.is_spell_slot = i < spell_slots
		b.pressed.connect(_on_slot_pressed.bind(i))
		b.assign_requested.connect(_assign_slot)
		_slots.append(b)
		_hotbar.add_child(b)
		if i == spell_slots - 1:
			var sep := Control.new()
			sep.custom_minimum_size = Vector2(4, 0)
			_hotbar.add_child(sep)
	var bar_width := total * HOTBAR_SLOT + total * UiTheme.GAP + 4 + 6
	_hotbar_panel.position = Vector2(roundf((base.x - bar_width) / 2.0), base.y - m - HOTBAR_HEIGHT)

	# Barra de casteo (centrada, justo encima de la barra rápida).
	var cast_w := 150.0
	var cast_top := base.y - m - HOTBAR_HEIGHT - 13
	_cast_frame = UiTheme.framed_bar("cast", 5)
	_cast_frame.visible = false
	add_child(_cast_frame)
	_place(_cast_frame, Vector2(roundf((base.x - cast_w) / 2.0), cast_top), Vector2(cast_w, 9))
	_cast_bar = _cast_frame.get_child(0) as ProgressBar
	_cast_bar.max_value = 1.0
	_cast_label = Label.new()
	_cast_label.theme_type_variation = "OutlinedLabel"
	_cast_label.position = Vector2(roundf((base.x - cast_w) / 2.0), cast_top - 9)
	_cast_label.custom_minimum_size = Vector2(cast_w, 9)
	_cast_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_cast_label.visible = false
	add_child(_cast_label)

	# Error del servidor: aviso rojo con fondo, arriba al centro, que se desvanece solo.
	_toast = PanelContainer.new()
	_toast.theme_type_variation = "ToastPanel"
	_toast.visible = false
	_toast.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_top_layer.add_child(_toast)
	_error_label = Label.new()
	_error_label.theme_type_variation = "OutlinedLabel"
	_error_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_toast.add_child(_error_label)

	# Avisos (nivel, hechizo nuevo): dorado con contorno, debajo del aviso de error.
	_notice_label = Label.new()
	_notice_label.theme_type_variation = "TitleLabel"
	_notice_label.position = Vector2(roundf((base.x - 240) / 2.0), FRAMES_BOTTOM + 22)
	_notice_label.custom_minimum_size = Vector2(240, 10)
	_notice_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_notice_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_top_layer.add_child(_notice_label)

	# Botón de menú (arriba a la derecha): engranaje sobre el mismo tablón que el resto de botones.
	_menu_button = Button.new()
	_menu_button.icon = UiTheme.texture("icon_menu.png")
	_menu_button.tooltip_text = "Menú (Esc)"
	_menu_button.focus_mode = Control.FOCUS_NONE
	_menu_button.add_theme_constant_override("h_separation", 0)
	_menu_button.custom_minimum_size = Vector2(18, 18)
	_menu_button.size = Vector2(18, 18)
	_menu_button.position = Vector2(base.x - m - 18, m)
	_menu_button.pressed.connect(func() -> void: menu_requested.emit())
	add_child(_menu_button)

	# Pantalla de muerte (centro).
	_death_panel = PanelContainer.new()
	_death_panel.custom_minimum_size = Vector2(150, 0)
	_death_panel.visible = false
	add_child(_death_panel)
	var dv := VBoxContainer.new()
	dv.add_theme_constant_override("separation", 4)
	_death_panel.add_child(dv)
	var title := Label.new()
	title.text = "Has muerto"
	title.theme_type_variation = "HeadlineLabel"
	title.add_theme_color_override("font_color", UiTheme.ERROR)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	dv.add_child(title)
	_death_killer = Label.new()
	_death_killer.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	dv.add_child(_death_killer)
	var respawn := Button.new()
	respawn.text = "Reaparecer"
	respawn.pressed.connect(func() -> void: respawn_requested.emit())
	dv.add_child(respawn)
	UiTheme.dock(_death_panel, Control.PRESET_CENTER)


func _portrait(parent: Control, at: Vector2) -> TextureRect:
	var frame := TextureRect.new()
	frame.texture = UiTheme.texture("portrait_frame.png")
	frame.position = at
	frame.size = Vector2(PORTRAIT, PORTRAIT)
	frame.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var pic := TextureRect.new()
	pic.position = Vector2(3, 3)
	pic.size = Vector2(16, 16)
	pic.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	pic.stretch_mode = TextureRect.STRETCH_KEEP_CENTERED
	pic.mouse_filter = Control.MOUSE_FILTER_IGNORE
	parent.add_child(pic)
	parent.add_child(frame)
	pic.position += at
	return pic


## Posición y tamaño fijos de una barra con marco. Fuera del árbol el tamaño mínimo aún no tiene el tema y sale mayor:
## se vuelve a fijar en el siguiente cuadro.
func _place(c: Control, at: Vector2, s: Vector2) -> void:
	c.position = at
	c.custom_minimum_size = s
	c.size = s
	c.set_deferred("size", s)


func _outlined_label(parent: Control, at: Vector2, width: float) -> Label:
	var l := Label.new()
	l.theme_type_variation = "OutlinedLabel"
	l.position = at
	l.size = Vector2(width, 9)
	l.clip_text = true
	l.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	parent.add_child(l)
	return l


## Barra con marco y su valor centrado encima ("80/100"). Devuelve [ProgressBar, Label].
func _bar_with_text(parent: Control, fill: String, at: Vector2, width: float, inner_h: int) -> Array:
	var frame := UiTheme.framed_bar(fill, inner_h)
	parent.add_child(frame)
	_place(frame, at, Vector2(width, inner_h + 4))
	var text := Label.new()
	text.theme_type_variation = "OutlinedLabel"
	text.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	text.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	text.position = at + Vector2(0, roundf((inner_h + 4 - 10) / 2.0))
	text.size = Vector2(width, 10)
	text.mouse_filter = Control.MOUSE_FILTER_IGNORE
	parent.add_child(text)
	return [frame.get_child(0) as ProgressBar, text]


func _process(_delta: float) -> void:
	var now := Time.get_ticks_msec() / 1000.0
	if _error_until > 0.0:
		var left := _error_until - now
		_toast.modulate.a = clampf(left / ERROR_FADE, 0.0, 1.0)
		if left <= 0.0:
			_error_until = 0.0
			_error_label.text = ""
			_toast.visible = false
	if _notice_until > 0.0 and now >= _notice_until:
		_notice_until = 0.0
		_notice_label.text = ""
	if _cast_end_text_until > 0.0 and now >= _cast_end_text_until:
		_cast_end_text_until = 0.0
		_cast_label.visible = false
		_cast_frame.visible = false
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
	_self_name.text = "%s  Nv %d" % [GameState.character_name, GameState.level]
	_self_portrait.texture = UiTheme.icon("classes/" + GameState.class_id) if not GameState.class_id.is_empty() else null
	_refresh_xp()
	_self_hp.max_value = maxi(1, GameState.max_hp)
	_self_hp.value = GameState.hp
	_self_hp_text.text = "%d/%d" % [GameState.hp, GameState.max_hp]
	_self_res.max_value = maxi(1, GameState.max_resource)
	_self_res.value = GameState.resource
	_self_res_text.text = "%d/%d" % [GameState.resource, GameState.max_resource]
	var kind := GameState.resource_kind if GameState.resource_kind in ["mana", "rage", "energy"] else "mana"
	_self_res.add_theme_stylebox_override("fill", UiTheme.bar_fill_style(kind))
	_refresh_hotbar()


## HU-040 CA2: barra de XP con `xp / xpNext` y tooltip; "Nivel máximo" en el tope de la fase (CA4).
func _refresh_xp() -> void:
	if _xp_bar == null:
		return
	if GameState.at_level_cap():
		_xp_bar.max_value = 1
		_xp_bar.value = 1
		_xp_frame.tooltip_text = "Nivel máximo"
		_self_name.text = "%s  Nv %d · Máx." % [GameState.character_name, GameState.level]
	else:
		_xp_bar.max_value = maxi(1, GameState.xp_next)
		_xp_bar.value = GameState.xp
		_xp_frame.tooltip_text = "XP %d / %d" % [GameState.xp, GameState.xp_next]


func show_notice(text: String) -> void:
	_notice_label.text = text
	_notice_until = Time.get_ticks_msec() / 1000.0 + 3.0


# --- Objetivo -------------------------------------------------------------------------------------------------------------

func _on_target_changed(entity_id: int) -> void:
	_target_frame.visible = entity_id > 0
	if entity_id > 0:
		_target_hp.max_value = 100
		_target_hp.value = _target_hp_pct
		_target_hp_text.text = "%d%%" % _target_hp_pct
		_target_name.text = _target_entity_name if not _target_entity_name.is_empty() else "#%d" % entity_id
		_rebuild_auras(_target_auras, entity_id)


## El mundo informa del nombre/nivel/vida del objetivo (datos de EntitySpawn + Snapshot) y, si la hay, la cara del sprite.
func set_target_info(display_name: String, level: int, hp_pct: int, portrait: Texture2D = null) -> void:
	_target_entity_name = display_name
	_target_level = level
	_target_hp_pct = hp_pct
	if portrait != null:
		_target_portrait_ref = portrait
	if _target_frame.visible:
		_target_name.text = "%s  Nv %d" % [display_name, level] if level > 0 else display_name
		_target_hp.value = hp_pct
		_target_hp_text.text = "%d%%" % hp_pct
		_target_portrait.texture = _target_portrait_ref


# --- Casteo ----------------------------------------------------------------------------------------------------------------

func _on_cast_changed() -> void:
	if GameState.own_cast.is_empty():
		return
	var spell := Content.spell(str(GameState.own_cast["spellId"]))
	_cast_label.text = str(spell.get("name", GameState.own_cast["spellId"]))
	_cast_label.add_theme_color_override("font_color", UiTheme.TEXT)
	_cast_label.visible = true
	_cast_frame.visible = true
	_cast_bar.value = 0.0
	_cast_end_text_until = 0.0


## Fin del casteo propio con resultado (lo llama el mundo con CastEnded): "Interrumpido" rojo, "Fuera de alcance" gris, nada si cancela.
func show_cast_result(result: String, reason: String) -> void:
	match result:
		"interrupted":
			_cast_label.text = "Interrumpido"
			_cast_label.add_theme_color_override("font_color", UiTheme.ERROR)
			_cast_end_text_until = Time.get_ticks_msec() / 1000.0 + 1.0
		"failed":
			_cast_label.text = "Fuera de alcance" if reason == "out_of_range" else "Sin línea de visión"
			_cast_label.add_theme_color_override("font_color", UiTheme.TEXT_MUTED)
			_cast_end_text_until = Time.get_ticks_msec() / 1000.0 + 1.0
		_:
			_cast_label.visible = false
			_cast_frame.visible = false


# --- Hotbar ------------------------------------------------------------------------------------------------------------------

func _on_slot_pressed(slot: int) -> void:
	hotbar_pressed.emit(slot)


func _refresh_hotbar() -> void:
	for i: int in _slots.size():
		var b := _slots[i]
		var entry := _slot_entry(i)
		if entry.is_empty():
			b.show_entry(null, "", -1, "")
			b.disabled = true
			b.set_state(HotSlot.STATE_READY)
			continue
		b.disabled = false
		var ref := str(entry.get("ref", ""))
		if str(entry.get("kind", "spell")) == "spell":
			var spell := Content.spell(ref)
			b.show_entry(UiTheme.icon(str(spell.get("icon", ""))), str(spell.get("name", ref)), -1, TooltipBuilder.build_spell(spell))
			var cost: Dictionary = spell.get("cost", {}) if spell.get("cost") != null else {}
			var lacks := not cost.is_empty() and int(cost.get("amount", 0)) > GameState.resource
			var out_of_range := in_range_check.is_valid() and not bool(in_range_check.call(spell))
			b.set_state(HotSlot.STATE_NO_RESOURCE if lacks else (HotSlot.STATE_OUT_OF_RANGE if out_of_range else HotSlot.STATE_READY))
		else:
			var item := Content.item(ref)
			var count := GameState.bag_count(ref)  # HU-043 CA3: cantidad total en bolsa
			b.show_entry(UiTheme.icon(str(item.get("icon", ""))), str(item.get("name", ref)), count, TooltipBuilder.build(item, count, GameState.class_id, GameState.level))
			b.set_state(HotSlot.STATE_READY if count > 0 else HotSlot.STATE_EMPTY)


## HU-043 CA2/CA4: asigna (SetHotbar) o vacía (kind vacío) una casilla; la copia local se corrige con el próximo Welcome.
func _assign_slot(slot: int, kind: String, ref: String) -> void:
	for i: int in range(GameState.hotbar.size() - 1, -1, -1):
		var hd: Dictionary = GameState.hotbar[i]
		if int(hd.get("slot", -1)) == slot or (not kind.is_empty() and str(hd.get("kind", "")) == kind and str(hd.get("ref", "")) == ref):
			GameState.hotbar.remove_at(i)
	if kind.is_empty():
		Net.send("SetHotbar", {"slot": slot})
	else:
		GameState.hotbar.append({"slot": slot, "kind": kind, "ref": ref})
		Net.send("SetHotbar", {"slot": slot, "kind": kind, "ref": ref})
	_refresh_hotbar()


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
		var remaining_ms := 0
		if not entry.is_empty() and str(entry.get("kind", "spell")) == "spell":
			var spell_id := str(entry.get("ref", ""))
			var cd := GameState.cooldown_remaining_ms(spell_id)
			var spell := Content.spell(spell_id)
			var total := int(spell.get("cooldownMs", 0))
			if cd > 0 and total > 0:
				frac = float(cd) / float(total)
				remaining_ms = cd
			elif gcd > 0 and bool(spell.get("triggersGcd", true)):
				frac = float(gcd) / float(gcd_total)
		elif not entry.is_empty():
			var template_id := str(entry.get("ref", ""))
			var item_cd := GameState.item_cooldown_remaining_ms(template_id)
			var item_total := int(Content.item(template_id).get("useCooldownMs", 0))
			if item_cd > 0 and item_total > 0:
				frac = float(item_cd) / float(item_total)
				remaining_ms = item_cd
		_slots[i].set_cooldown(frac, remaining_ms)


# --- Auras ---------------------------------------------------------------------------------------------------------------------

func _on_auras_changed(entity_id: int) -> void:
	if entity_id == GameState.self_id:
		_rebuild_auras(_self_auras, entity_id)
	elif entity_id == GameState.target_id:
		_rebuild_auras(_target_auras, entity_id)


## Ícono de 16×16 del aura con el tiempo restante debajo (y las cargas); marco verde si beneficia y rojo si perjudica, e
## insignia de tipo arriba a la izquierda (aturdido, inmovilizado, ralentizado, daño o curación en el tiempo, escudo…).
func _rebuild_auras(box: HBoxContainer, entity_id: int) -> void:
	for c: Node in box.get_children():
		c.queue_free()
	var list := GameState.auras_of(entity_id)
	var dominant := _dominant_ids(list)
	for a: Variant in list:
		var ad: Dictionary = a
		var def := Content.aura(str(ad["auraId"]))
		var cell := Control.new()
		cell.custom_minimum_size = Vector2(16, 16)
		cell.set_meta("aura", ad)
		cell.mouse_filter = Control.MOUSE_FILTER_PASS
		var cat := AuraStyle.category(def)
		cell.tooltip_text = "%s (%s)\n%s" % [str(def.get("name", ad["auraId"])), AuraStyle.CATEGORY_NAMES.get(cat, ""), str(def.get("description", ""))]
		var pic := TextureRect.new()
		pic.texture = UiTheme.icon(str(def.get("icon", "")))
		pic.size = Vector2(16, 16)
		pic.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		pic.mouse_filter = Control.MOUSE_FILTER_IGNORE
		cell.add_child(pic)
		var ring := NinePatchRect.new()
		ring.texture = UiTheme.texture("slot_frame.png")
		ring.patch_margin_left = 2
		ring.patch_margin_top = 2
		ring.patch_margin_right = 2
		ring.patch_margin_bottom = 2
		ring.size = Vector2(16, 16)
		ring.self_modulate = AuraStyle.frame_color(def)
		ring.mouse_filter = Control.MOUSE_FILTER_IGNORE
		cell.add_child(ring)
		var badge := Control.new()
		badge.name = "Badge"
		badge.size = Vector2(7, 7)
		badge.mouse_filter = Control.MOUSE_FILTER_IGNORE
		badge.draw.connect(func() -> void: AuraStyle.draw_badge(badge, Vector2(1, 1), cat))
		badge.set_meta("category", cat)
		cell.add_child(badge)
		var time := Label.new()
		time.name = "Time"
		time.theme_type_variation = "OutlinedLabel"
		time.position = Vector2(0, 9)
		time.size = Vector2(16, 9)
		time.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		time.mouse_filter = Control.MOUSE_FILTER_IGNORE
		cell.add_child(time)
		var is_dominant: bool = dominant.has(str(ad["auraId"]))
		cell.modulate = Color.WHITE if is_dominant else Color(0.55, 0.55, 0.55)
		box.add_child(cell)
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
		var cell := c as Control
		if cell == null or not cell.has_meta("aura"):
			continue
		var ad: Dictionary = cell.get_meta("aura")
		var remaining := maxi(0, int(ad["endsMs"]) - now)
		var stacks := int(ad.get("stacks", 1))
		var time := cell.get_node_or_null("Time") as Label
		if time != null:
			time.text = ("x%d" % stacks) if stacks > 1 else ("%d" % ceili(remaining / 1000.0))


# --- Errores y muerte -----------------------------------------------------------------------------------------------------------

func _on_ui_error(code: String, _req_id: int) -> void:
	var text := Net.last_error_message if not Net.last_error_message.is_empty() else ApiMessages.text_for(code)
	show_error(text)


## Aviso de error fijo arriba al centro (bajo los marcos), con fondo y contorno; se desvanece solo a los ERROR_SECONDS.
func show_error(text: String) -> void:
	_error_label.text = text
	_toast.visible = true
	_toast.modulate.a = 1.0
	_toast.reset_size()
	var base := UiTheme.base_size()
	_toast.position = Vector2(roundf((base.x - _toast.get_combined_minimum_size().x) / 2.0), FRAMES_BOTTOM + 6)
	_error_until = Time.get_ticks_msec() / 1000.0 + ERROR_SECONDS


func _on_died(killer_id: int) -> void:
	_death_panel.visible = true
	_death_killer.text = "Te mató #%d" % killer_id if killer_id > 0 else ""
	UiTheme.dock(_death_panel, Control.PRESET_CENTER)


func _on_respawned() -> void:
	_death_panel.visible = false


## Casilla de la barra con arrastrar/soltar: hechizos (del libro) en 0–3, consumibles (de la bolsa) en 4–7; Shift+arrastrar fuera quita.
## Ícono a ×2, tecla pequeña arriba a la izquierda, cantidad abajo a la derecha, sombra de recarga que baja con los segundos
## restantes y velos: rojo fuera de alcance, azul sin recurso, gris con "0" si no queda en la bolsa.
class HotSlot extends Button:
	signal assign_requested(slot: int, kind: String, ref: String)

	const STATE_READY := 0
	const STATE_OUT_OF_RANGE := 1
	const STATE_NO_RESOURCE := 2
	const STATE_EMPTY := 3

	var slot: int = 0
	var is_spell_slot: bool = true
	## Tooltip propio (RichTooltip): nombre completo, coste, recarga, descripción.
	var tooltip_bbcode: String = ""
	var state: int = STATE_READY
	var entry_name: String = ""
	var _icon: TextureRect
	var _key: Label
	var _count: Label
	var _sweep: ColorRect
	var _cd_text: Label
	var _veil: ColorRect

	func _ready() -> void:
		custom_minimum_size = Vector2(CombatHud.HOTBAR_SLOT, CombatHud.HOTBAR_SLOT)
		focus_mode = Control.FOCUS_NONE
		theme_type_variation = "SlotButton"
		_icon = TextureRect.new()
		_icon.position = Vector2(1, 1)
		_icon.size = ItemSlot.ICON_SIZE
		_icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		_icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
		add_child(_icon)
		_veil = ColorRect.new()
		_veil.position = Vector2(1, 1)
		_veil.size = ItemSlot.ICON_SIZE
		_veil.mouse_filter = Control.MOUSE_FILTER_IGNORE
		_veil.visible = false
		add_child(_veil)
		_sweep = ColorRect.new()
		_sweep.color = Color(UiTheme.OUTLINE, 0.7)
		_sweep.position = Vector2(1, 1)
		_sweep.size = Vector2(32, 0)
		_sweep.mouse_filter = Control.MOUSE_FILTER_IGNORE
		add_child(_sweep)
		_cd_text = Label.new()
		_cd_text.theme_type_variation = "OutlinedLabel"
		_cd_text.set_anchors_preset(Control.PRESET_FULL_RECT)
		_cd_text.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		_cd_text.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		_cd_text.mouse_filter = Control.MOUSE_FILTER_IGNORE
		add_child(_cd_text)
		_key = Label.new()
		UiTheme.outlined(_key, UiTheme.TEXT_MUTED)
		_key.text = str(slot + 1)
		_key.position = Vector2(2, -1)
		_key.mouse_filter = Control.MOUSE_FILTER_IGNORE
		add_child(_key)
		_count = Label.new()
		UiTheme.outlined(_count)
		_count.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_RIGHT)
		_count.grow_horizontal = Control.GROW_DIRECTION_BEGIN
		_count.grow_vertical = Control.GROW_DIRECTION_BEGIN
		_count.offset_right = -2
		_count.offset_bottom = 0
		_count.mouse_filter = Control.MOUSE_FILTER_IGNORE
		add_child(_count)

	## Ícono, nombre (para el tooltip nativo), cantidad abajo a la derecha (-1 = sin cantidad) y tooltip.
	func show_entry(icon: Texture2D, name_text: String, count: int, bbcode: String) -> void:
		entry_name = name_text
		if _icon != null:
			_icon.texture = icon
		if _count != null:
			_count.text = str(count) if count >= 0 else ""
		tooltip_bbcode = bbcode
		tooltip_text = name_text  # Godot solo pide el tooltip propio si hay texto

	func icon_texture() -> Texture2D:
		return _icon.texture if _icon != null else null

	func set_state(new_state: int) -> void:
		state = new_state
		if _veil == null:
			return
		match state:
			STATE_OUT_OF_RANGE:
				_veil.color = Color(UiTheme.ERROR, 0.4)
			STATE_NO_RESOURCE:
				_veil.color = Color(0.2, 0.25, 0.6, 0.5)
			STATE_EMPTY:
				_veil.color = Color(UiTheme.OUTLINE, 0.65)
		_veil.visible = state != STATE_READY
		_count.add_theme_color_override("font_color", UiTheme.ERROR if state == STATE_EMPTY else UiTheme.TEXT)

	## Recarga: velo oscuro que cubre la fracción restante (de abajo arriba) y los segundos si quedan más de 1.5 s.
	func set_cooldown(frac: float, remaining_ms: int) -> void:
		if _sweep == null:
			return
		var h := roundf(32.0 * clampf(frac, 0.0, 1.0))
		_sweep.size = Vector2(32, h)
		_sweep.position = Vector2(1, 1 + 32 - h)
		_cd_text.text = str(ceili(remaining_ms / 1000.0)) if remaining_ms > 1500 else ""

	func _make_custom_tooltip(_for_text: String) -> Object:
		return RichTooltip.make(tooltip_bbcode) if not tooltip_bbcode.is_empty() else null

	func _can_drop_data(_at: Vector2, data: Variant) -> bool:
		if not (data is Dictionary):
			return false
		var d: Dictionary = data
		if d.has("kind") and str(d["kind"]) == "spell":
			return is_spell_slot
		if d.has("itemId"):
			var item := GameState.bag_item(str(d["itemId"]))
			return not is_spell_slot and str(Content.item(str(item.get("templateId", ""))).get("type", "")) == "consumable"
		return false

	func _drop_data(_at: Vector2, data: Variant) -> void:
		var d: Dictionary = data
		if d.has("kind") and str(d["kind"]) == "spell":
			assign_requested.emit(slot, "spell", str(d["ref"]))
		elif d.has("itemId"):
			var item := GameState.bag_item(str(d["itemId"]))
			assign_requested.emit(slot, "item", str(item.get("templateId", "")))

	func _get_drag_data(_at: Vector2) -> Variant:
		if not Input.is_key_pressed(KEY_SHIFT):
			return null
		set_drag_preview(ItemSlot.drag_preview(_icon.texture))
		return {"hotbarSlot": slot}
