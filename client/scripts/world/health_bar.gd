class_name HealthBar
extends Node2D
## Barra de vida pequeña de la placa de nombre (jugadores y monstruos): marco oscuro de 1 px, fondo hundido y relleno con
## una fila de brillo arriba. Verde por encima de la mitad, amarillo hasta un cuarto y rojo por debajo. Origen = centro
## del borde superior.

const WIDTH := 20.0
const HEIGHT := 2.0
const BACKGROUND := Color("3e3546")
const FRAME := Color("2e222f")
const HIGH := Color("1ebc73")
const HIGH_LIGHT := Color("91db69")
const MID := Color("f9c22b")
const MID_LIGHT := Color("fbff86")
const LOW := Color("e83b3b")
const LOW_LIGHT := Color("f68181")

var pct: int = 100:
	set(value):
		var clamped := clampi(value, 0, 100)
		if clamped != pct:
			pct = clamped
			queue_redraw()


func _draw() -> void:
	var left := -WIDTH / 2.0
	draw_rect(Rect2(left - 1.0, -1.0, WIDTH + 2.0, HEIGHT + 2.0), FRAME)
	draw_rect(Rect2(left, 0.0, WIDTH, HEIGHT), BACKGROUND)
	if pct > 0:
		var w := maxf(1.0, roundf(WIDTH * pct / 100.0))
		draw_rect(Rect2(left, 0.0, w, HEIGHT), fill_color(pct))
		draw_rect(Rect2(left, 0.0, w, 1.0), _light(pct))


static func fill_color(value: int) -> Color:
	if value > 50:
		return HIGH
	if value > 25:
		return MID
	return LOW


static func _light(value: int) -> Color:
	if value > 50:
		return HIGH_LIGHT
	if value > 25:
		return MID_LIGHT
	return LOW_LIGHT
