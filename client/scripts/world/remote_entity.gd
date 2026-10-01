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

var _buffer: InterpolationBuffer = InterpolationBuffer.new()
var _body: ColorRect
var _label: Label


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


## Aplica el estado de un Snapshot (campos frecuentes).
func apply_state(e: Dictionary, now_ms: float) -> void:
	_buffer.push(now_ms, Vector2(float(e.get("x", position.x)), float(e.get("y", position.y))))
	dir = str(e.get("dir", dir))
	hp_pct = int(e.get("hpPct", hp_pct))
	anim = str(e.get("anim", anim))


func _process(_delta: float) -> void:
	if _buffer.size() > 0:
		position = _buffer.sample(float(Time.get_ticks_msec()))


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
