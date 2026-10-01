class_name AoeReticle
extends Node2D
## Marca de área (HU-086 CA1/CA3): círculo de `aoeRadius` bajo el cursor mientras se apunta (rojo si está fuera de alcance) y
## marcas en el suelo de casteos ajenos (CastStarted{targetPos}) hasta que terminan. La forma y el tamaño salen del contenido.

const POOL_SIZE := 32
const MAX_VISIBLE := 24

var aiming: bool = false
var aim_radius_px: float = 0.0
var aim_in_range: bool = true
var aim_pos: Vector2 = Vector2.ZERO

var _marks: Dictionary = {}  # caster_id → {pos, radius, enemy}


func set_mark(caster_id: int, pos_px: Vector2, radius_px: float, enemy: bool) -> void:
	if _marks.size() >= MAX_VISIBLE and not _marks.has(caster_id) and not enemy:
		return  # las enemigas nunca se ocultan (ADR-018)
	_marks[caster_id] = {"pos": pos_px, "radius": radius_px, "enemy": enemy}
	queue_redraw()


func clear_mark(caster_id: int) -> void:
	if _marks.erase(caster_id):
		queue_redraw()


func clear_all() -> void:
	_marks.clear()
	queue_redraw()


func _process(_delta: float) -> void:
	if aiming or not _marks.is_empty():
		queue_redraw()


func _draw() -> void:
	for m: Variant in _marks.values():
		var md: Dictionary = m
		var color := Color(1, 0.3, 0.2, 0.35) if bool(md["enemy"]) else Color(0.3, 0.6, 1, 0.3)
		draw_circle(md["pos"], float(md["radius"]), color)
		draw_arc(md["pos"], float(md["radius"]), 0, TAU, 32, color.lightened(0.3), 1.0)
	if aiming:
		var c := Color(0.3, 1, 0.4, 0.3) if aim_in_range else Color(1, 0.2, 0.2, 0.3)
		draw_circle(aim_pos, aim_radius_px, c)
		draw_arc(aim_pos, aim_radius_px, 0, TAU, 32, c.lightened(0.4), 1.0)
