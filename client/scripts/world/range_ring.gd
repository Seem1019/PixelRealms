class_name RangeRing
extends Node2D
## Círculo punteado del alcance alrededor del personaje propio mientras se mantiene Espacio o una tecla de hechizo
## (HU-096). Verde si el objetivo está dentro, rojo si no, crema sin objetivo. Puntos de 1 px como el resto del pixel art.

const IN_RANGE := Color("91db69")
const OUT_OF_RANGE := Color("ea4f36")
const NO_TARGET := Color("fbf3e0")
## Separación entre puntos a lo largo del círculo, en píxeles.
const DOT_SPACING_PX := 4.0

var radius_px: float = 0.0:
	set(v):
		if not is_equal_approx(v, radius_px):
			radius_px = v
			queue_redraw()
## 1 dentro, 0 fuera, −1 sin objetivo.
var target_state: int = -1:
	set(v):
		if v != target_state:
			target_state = v
			queue_redraw()


func _ready() -> void:
	z_index = -3  # bajo los cuerpos y sobre el suelo
	visible = false


func show_range(reach_px: float, state: int) -> void:
	radius_px = reach_px
	target_state = state
	visible = reach_px > 0.0


func hide_range() -> void:
	visible = false


func color() -> Color:
	return IN_RANGE if target_state == 1 else (OUT_OF_RANGE if target_state == 0 else NO_TARGET)


func _draw() -> void:
	if radius_px <= 0.0:
		return
	var c := color()
	var dots := maxi(12, int(TAU * radius_px / DOT_SPACING_PX))
	for i: int in dots:
		var p := Vector2.from_angle(TAU * i / dots) * radius_px
		draw_rect(Rect2(p.round(), Vector2.ONE), c)
	# Sombra tenue para leerse sobre la hierba clara.
	draw_arc(Vector2.ZERO, radius_px, 0.0, TAU, dots, Color(c, 0.18), 1.0)
