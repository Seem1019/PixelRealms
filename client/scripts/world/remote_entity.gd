class_name RemoteEntity
extends Node2D
## Entidad remota (jugador, monstruo, NPC) dibujada a partir de EntitySpawn + Snapshot (HU-023, HU-024).
## Posición interpolada 100 ms atrás con InterpolationBuffer; nombre encima (blanco; azul para el grupo, HU-061).

const PLAYER_COLOR := Color(0.55, 0.75, 1.0)
const MONSTER_COLOR := Color(0.9, 0.35, 0.3)
const NPC_COLOR := Color(0.5, 0.9, 0.5)

var entity_id: int = -1
var kind: String = "player"
var template_id: String = ""
var display_name: String = ""
var level: int = 1
var class_id: String = ""
var hp_pct: int = 100
var dir: String = "down"
var anim: String = "idle"

var selected: bool = false
var hostile: bool = false
var cast_started_ms: int = -1
var cast_duration_ms: int = 0

var _buffer: InterpolationBuffer = InterpolationBuffer.new()
var _body: ColorRect
var _label: Label
var _level_label: Label
var _cast_bar: ColorRect


func _ready() -> void:
	y_sort_enabled = true
	_body = ColorRect.new()
	_body.offset_left = -6.0
	_body.offset_top = -12.0
	_body.offset_right = 6.0
	_body.offset_bottom = 4.0
	_body.color = _color_for_kind()
	add_child(_body)
	_label = Label.new()
	_label.offset_left = -32.0
	_label.offset_top = -24.0
	_label.offset_right = 32.0
	_label.offset_bottom = -14.0
	_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_label.add_theme_font_size_override("font_size", 8)
	_label.add_theme_color_override("font_color", Color.WHITE)
	_label.text = display_name
	add_child(_label)
	_level_label = Label.new()
	_level_label.offset_left = -32.0
	_level_label.offset_top = -32.0
	_level_label.offset_right = 32.0
	_level_label.offset_bottom = -24.0
	_level_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_level_label.add_theme_font_size_override("font_size", 7)
	add_child(_level_label)
	_cast_bar = ColorRect.new()
	_cast_bar.offset_left = -8.0
	_cast_bar.offset_top = 6.0
	_cast_bar.offset_right = 8.0
	_cast_bar.offset_bottom = 8.0
	_cast_bar.color = Color(0.9, 0.7, 0.2)
	_cast_bar.visible = false
	add_child(_cast_bar)
	refresh_level_color()


## Rellena desde un EntitySpawn (docs/protocol.md).
func setup(d: Dictionary) -> void:
	entity_id = int(d.get("id", -1))
	kind = str(d.get("kind", "player"))
	template_id = str(d.get("templateId", ""))
	display_name = str(d.get("name", ""))
	level = int(d.get("level", 1))
	class_id = str(d.get("classId", ""))
	hp_pct = int(d.get("hpPct", 100))
	dir = str(d.get("dir", "down"))
	var pos := Vector2(float(d.get("x", 0)), float(d.get("y", 0)))
	position = pos
	_buffer.clear()
	_buffer.push(float(Time.get_ticks_msec()), pos)
	if _label != null:
		_label.text = display_name
	if _body != null:
		_body.color = _color_for_kind()
	hostile = kind == "monster"
	refresh_level_color()


## Aplica el estado de un Snapshot (campos frecuentes).
func apply_state(e: Dictionary, now_ms: float) -> void:
	_buffer.push(now_ms, Vector2(float(e.get("x", position.x)), float(e.get("y", position.y))))
	dir = str(e.get("dir", dir))
	hp_pct = int(e.get("hpPct", hp_pct))
	anim = str(e.get("anim", anim))


func _process(_delta: float) -> void:
	if _buffer.size() > 0:
		position = _buffer.sample(float(Time.get_ticks_msec()))
	if cast_started_ms >= 0 and _cast_bar != null:
		var frac := clampf(float(Time.get_ticks_msec() - cast_started_ms) / float(maxi(1, cast_duration_ms)), 0.0, 1.0)
		_cast_bar.offset_right = -8.0 + 16.0 * frac
	if _body != null:
		_body.modulate.a = 0.45 if anim == "dead" else 1.0
	queue_redraw()


## HU-030 CA1: círculo bajo los pies (rojo hostil, verde aliado) cuando está seleccionada.
func _draw() -> void:
	if selected:
		draw_arc(Vector2(0, 3), 7.0, 0, TAU, 24, Color(1, 0.2, 0.2) if hostile else Color(0.2, 1, 0.3), 1.0)


## HU-031 CA4: nivel coloreado según la diferencia con el mío (gris ≤ −5, verde −3..−4, amarillo ±2, naranja +3..+4, rojo ≥ +5).
func refresh_level_color() -> void:
	if _level_label == null:
		return
	if kind != "monster":
		_level_label.visible = false
		return
	_level_label.visible = true
	_level_label.text = "nv %d" % level
	_level_label.add_theme_color_override("font_color", level_color(level - GameState.level))


static func level_color(diff: int) -> Color:
	if diff <= -5:
		return Color(0.6, 0.6, 0.6)
	if diff <= -3:
		return Color(0.3, 0.9, 0.3)
	if diff <= 2:
		return Color(1, 0.9, 0.2)
	if diff <= 4:
		return Color(1, 0.55, 0.1)
	return Color(1, 0.2, 0.2)


## Mini barra de casteo sobre la entidad (CastStarted/CastEnded ajenos).
func begin_cast(duration_ms: int) -> void:
	cast_started_ms = Time.get_ticks_msec()
	cast_duration_ms = duration_ms
	_cast_bar.visible = duration_ms > 0
	_cast_bar.color = Color(0.9, 0.7, 0.2)


func end_cast(result: String) -> void:
	if result == "interrupted":
		_cast_bar.color = Color(1, 0.3, 0.3)
	cast_started_ms = -1
	_cast_bar.visible = false


func set_name_color(color: Color) -> void:
	if _label != null:
		_label.add_theme_color_override("font_color", color)


func _color_for_kind() -> Color:
	match kind:
		"monster":
			return MONSTER_COLOR
		"npc":
			return NPC_COLOR
		_:
			return PLAYER_COLOR
