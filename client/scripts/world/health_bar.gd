class_name HealthBar
extends Node2D
## Barra de vida fina sobre una entidad (jugadores y monstruos): fondo oscuro y relleno por porcentaje.
## Verde por encima de la mitad, amarillo hasta un cuarto y rojo por debajo.

const WIDTH := 14.0
const HEIGHT := 2.0
const BACKGROUND := Color(0.08, 0.08, 0.08, 0.85)
const HIGH := Color(0.3, 0.85, 0.3)
const MID := Color(0.95, 0.8, 0.2)
const LOW := Color(0.9, 0.2, 0.2)

var pct: int = 100:
	set(value):
		var clamped := clampi(value, 0, 100)
		if clamped != pct:
			pct = clamped
			queue_redraw()


func _draw() -> void:
	var left := -WIDTH / 2.0
	draw_rect(Rect2(left - 1.0, -1.0, WIDTH + 2.0, HEIGHT + 2.0), BACKGROUND)
	if pct > 0:
		draw_rect(Rect2(left, 0.0, WIDTH * pct / 100.0, HEIGHT), fill_color(pct))


static func fill_color(value: int) -> Color:
	if value > 50:
		return HIGH
	if value > 25:
		return MID
	return LOW
