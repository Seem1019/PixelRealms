class_name AuraIndicator
extends Node2D
## Efectos de control visibles sobre una entidad sin seleccionarla (HU-098 CA2), con los pies en el origen:
## aturdido → tres estrellas girando alrededor de la cabeza; inmovilizado → anillo de hielo con púas en los pies; escudo →
## burbuja alrededor del cuerpo. (Ralentizado tiñe el sprite de azul: lo aplica EntityVisual.) Píxeles de 1 px.

const STAR := Color("f9c22b")
const ICE := Color("8fd3ff")
const ICE_DARK := Color("4d9be6")
const BUBBLE := Color("fbf3e0")
const STAR_PERIOD_MS := 900.0

var stunned: bool = false
var rooted: bool = false
var shielded: bool = false
## Alto del cuerpo sobre los pies (lo fija EntityVisual al elegir la hoja).
var body_height: float = 24.0


func _ready() -> void:
	z_index = 1


func apply(summary: Dictionary) -> void:
	stunned = bool(summary.get("stunned", false))
	rooted = bool(summary.get("rooted", false))
	shielded = bool(summary.get("shielded", false))
	visible = stunned or rooted or shielded
	set_process(stunned)
	queue_redraw()


func _process(_delta: float) -> void:
	queue_redraw()  # las estrellas giran


func _draw() -> void:
	if rooted:
		_draw_ice()
	if shielded:
		_draw_bubble()
	if stunned:
		_draw_stars(Time.get_ticks_msec())


## Elipse de hielo bajo los pies con cuatro púas.
func _draw_ice() -> void:
	for i: int in 28:
		var a := TAU * i / 28.0
		var p := Vector2(cos(a) * 8.0, sin(a) * 3.0 + 1.0).round()
		draw_rect(Rect2(p, Vector2.ONE), ICE if i % 2 == 0 else ICE_DARK)
	for x: float in [-8.0, -3.0, 3.0, 8.0]:
		var base := Vector2(x, 1.0 + (0.0 if absf(x) > 5.0 else 2.0))
		draw_rect(Rect2(base + Vector2(0, -3), Vector2(1, 3)), ICE)
		draw_rect(Rect2(base + Vector2(0, -4), Vector2.ONE), BUBBLE)


## Burbuja tenue alrededor del cuerpo.
func _draw_bubble() -> void:
	var center := Vector2(0, -body_height / 2.0)
	var r := body_height / 2.0 + 3.0
	draw_circle(center, r, Color(BUBBLE, 0.12))
	for i: int in 40:
		var a := TAU * i / 40.0
		if i % 3 != 0:
			draw_rect(Rect2((center + Vector2(cos(a), sin(a)) * r).round(), Vector2.ONE), Color(BUBBLE, 0.7))


## Tres estrellas de 3×3 girando en una elipse alrededor de la cabeza (más arriba las tapaba la barra de la placa).
func _draw_stars(now_ms: int) -> void:
	var phase := TAU * fmod(float(now_ms), STAR_PERIOD_MS) / STAR_PERIOD_MS
	var center := Vector2(0, -body_height + 5.0)
	for i: int in 3:
		var a := phase + TAU * i / 3.0
		var p := (center + Vector2(cos(a) * 7.0, sin(a) * 2.0)).round()
		# Contorno oscuro para leerse sobre la hierba y luego la cruz amarilla.
		draw_rect(Rect2(p + Vector2(-2, -1), Vector2(5, 3)), UiTheme.OUTLINE)
		draw_rect(Rect2(p + Vector2(-1, -2), Vector2(3, 5)), UiTheme.OUTLINE)
		draw_rect(Rect2(p + Vector2(-1, 0), Vector2(3, 1)), STAR)
		draw_rect(Rect2(p + Vector2(0, -1), Vector2(1, 3)), STAR)
