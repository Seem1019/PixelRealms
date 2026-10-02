class_name Nameplate
extends Node2D
## Placa sobre una entidad: una fila con el nivel (color según la diferencia con el mío, solo monstruos) y el nombre con
## contorno, y debajo la barra de vida y, si castea, la barra de casteo. Origen = centro del borde inferior de la placa.

const ROW_H := 9
const BAR_W := 20
const BAR_H := 4

var display_name: String = "":
	set(v):
		display_name = v
		_layout()
var level_text: String = "":
	set(v):
		level_text = v
		_layout()
var name_color: Color = UiTheme.TEXT:
	set(v):
		name_color = v
		if _name != null:
			_name.add_theme_color_override("font_color", v)
var level_color: Color = Color.WHITE:
	set(v):
		level_color = v
		if _level != null:
			_level.add_theme_color_override("font_color", v)

var health_bar: HealthBar
var cast_frac: float = -1.0:
	set(v):
		cast_frac = v
		queue_redraw()
var cast_color: Color = UiTheme.ACCENT
var _name: Label
var _level: Label


func _init() -> void:
	_level = Label.new()
	UiTheme.outlined(_level, level_color)
	add_child(_level)
	_name = Label.new()
	UiTheme.outlined(_name, name_color)
	add_child(_name)
	health_bar = HealthBar.new()
	add_child(health_bar)
	_layout()


func plate_size() -> Vector2:
	var w := _row_width()
	var h := ROW_H + (BAR_H + 1 if health_bar.visible else 0) + (3 if cast_frac >= 0.0 else 0)
	return Vector2(maxf(w, BAR_W + 2), h)


func _text_width(l: Label) -> float:
	if l.text.is_empty():
		return 0.0
	var f := l.get_theme_font("font")
	return f.get_string_size(l.text, HORIZONTAL_ALIGNMENT_LEFT, -1, l.get_theme_font_size("font_size")).x if f != null else l.text.length() * 5.0


func _row_width() -> float:
	var lw := _text_width(_level)
	return _text_width(_name) + (lw + 3.0 if lw > 0.0 else 0.0)


## Nombre y nivel centrados en una fila; la barra (y el casteo) debajo, todo hacia arriba desde el origen.
func _layout() -> void:
	if _name == null:
		return
	_name.text = display_name
	_level.text = level_text
	_level.visible = not level_text.is_empty()
	var bars := (BAR_H + 1 if health_bar != null and health_bar.visible else 0) + (3 if cast_frac >= 0.0 else 0)
	var row_y := -ROW_H - bars - 1
	var w := _row_width()
	var x := roundf(-w / 2.0)
	if _level.visible:
		_level.position = Vector2(x, row_y)
		x += _text_width(_level) + 3.0
	_name.position = Vector2(x, row_y)
	if health_bar != null:
		health_bar.position = Vector2(0, -bars + 1)
	queue_redraw()


func refresh_layout() -> void:
	_layout()


func _draw() -> void:
	if cast_frac < 0.0:
		return
	var y := -2.0
	var left := -BAR_W / 2.0
	draw_rect(Rect2(left - 1, y - 1, BAR_W + 2, 3), UiTheme.OUTLINE)
	draw_rect(Rect2(left, y, roundf(BAR_W * clampf(cast_frac, 0.0, 1.0)), 1), cast_color)
