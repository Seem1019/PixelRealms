class_name AoeReticle
extends Node2D
## Marca de área (HU-086 CA1/CA3): anillo pixelado con runas de `aoeRadius` bajo el cursor mientras se apunta (dorado; rojo
## si está fuera de alcance) y
## marcas en el suelo de casteos ajenos (CastStarted{targetPos}) hasta que terminan. La forma y el tamaño salen del contenido.
## HU-102: el cono y la línea salen del lanzador hacia el cursor (al apuntar) o desde `CastStarted.origin` (las marcas).
## Una marca se borra con su CastEnded, si el lanzador sale de la AOI (EntityDespawn) o, si nada de eso llega, al pasar la
## duración del casteo más EXPIRY_MARGIN_MS. Se dibuja en el suelo (debajo de las entidades) con un relleno muy tenue, para
## leerse sin tapar a nadie: borde de 1 px con contorno oscuro y un anillo interior discontinuo que gira despacio.

const POOL_SIZE := 32
const MAX_VISIBLE := 24
const EXPIRY_MARGIN_MS := 500
## Tramos del arco de un cono (más tramos, arco más redondo).
const CONE_ARC_STEPS := 12

## Al dejar de apuntar hace falta un último redibujado: `_process` solo redibuja mientras se apunta o hay marcas, y sin él el
## círculo se quedaba pintado donde estaba hasta volver a apuntar.
var aiming: bool = false:
	set(value):
		if value != aiming:
			aiming = value
			queue_redraw()
var aim_radius_px: float = 0.0
var aim_in_range: bool = true
var aim_pos: Vector2 = Vector2.ZERO
## Forma que se apunta (HU-102): "circle" en `aim_pos`; "cone" y "line" desde `aim_origin` hacia `aim_dir`.
var aim_shape: String = "circle"
var aim_origin: Vector2 = Vector2.ZERO
var aim_dir: Vector2 = Vector2.RIGHT
var aim_angle_deg: float = 90.0
var aim_length_px: float = 0.0
var aim_width_px: float = 0.0

var _marks: Dictionary = {}  # caster_id → {area, enemy, expires_ms}


## Área de un hechizo colocada (px): la entiende `BodyShape.shape_hits` y la dibuja `_draw_area`.
static func circle_area(center_px: Vector2, radius_px: float) -> Dictionary:
	return {"shape": "circle", "origin": center_px, "radius_px": radius_px}


func set_mark(caster_id: int, pos_px: Vector2, radius_px: float, enemy: bool, duration_ms: int = 0) -> void:
	set_area_mark(caster_id, circle_area(pos_px, radius_px), enemy, duration_ms)


func set_area_mark(caster_id: int, area: Dictionary, enemy: bool, duration_ms: int = 0) -> void:
	if _marks.size() >= MAX_VISIBLE and not _marks.has(caster_id) and not enemy:
		return  # las enemigas nunca se ocultan (ADR-018)
	_marks[caster_id] = {"area": area, "enemy": enemy, "expires_ms": Time.get_ticks_msec() + duration_ms + EXPIRY_MARGIN_MS}
	queue_redraw()


func clear_mark(caster_id: int) -> void:
	if _marks.erase(caster_id):
		queue_redraw()


## Quita las marcas cuyo casteo ya debería haber terminado (CastEnded perdido).
func prune(now_ms: int) -> void:
	for caster_id: Variant in _marks.keys():
		if now_ms > int((_marks[caster_id] as Dictionary)["expires_ms"]):
			_marks.erase(caster_id)
			queue_redraw()


func clear_all() -> void:
	_marks.clear()
	queue_redraw()


## Lo que se está apuntando, como área (para la vista previa de a quién alcanza).
func aim_area() -> Dictionary:
	if aim_shape == "circle":
		return circle_area(aim_pos, aim_radius_px)
	return {"shape": aim_shape, "origin": aim_origin, "dir": aim_dir, "radius_px": aim_radius_px, "angle_deg": aim_angle_deg,
		"length_px": aim_length_px, "width_px": aim_width_px}


func _process(_delta: float) -> void:
	if not _marks.is_empty():
		prune(Time.get_ticks_msec())
	if aiming or not _marks.is_empty():
		queue_redraw()


func _draw() -> void:
	var t := Time.get_ticks_msec() / 120.0
	for m: Variant in _marks.values():
		var md: Dictionary = m
		var enemy := bool(md["enemy"])
		_draw_area(md["area"], Color("e83b3b") if enemy else Color("4d9be6"), Color("fb6b1d") if enemy else Color("8fd3ff"), t)
	if aiming:
		var main := Color("f9c22b") if aim_in_range else Color("e83b3b")
		_draw_area(aim_area(), main, Color("fbff86") if aim_in_range else Color("f68181"), t)


func _draw_area(area: Dictionary, main: Color, light: Color, t: float) -> void:
	var shape := str(area.get("shape", "circle"))
	if shape == "circle":
		_rune(area["origin"], float(area["radius_px"]), main, light, t)
		return
	var points := shape_points(area)
	PixelDraw.polygon_fill(self, points, Color(main, 0.13))
	var shadow := PackedVector2Array()
	for p: Vector2 in points:
		shadow.append(p + Vector2(0, 1))
	PixelDraw.polygon_outline(self, shadow, Color(UiTheme.OUTLINE, 0.6))
	PixelDraw.polygon_outline(self, points, main)


## Contorno (px) de un cono (vértice y arco) o de una línea (rectángulo).
static func shape_points(area: Dictionary) -> PackedVector2Array:
	var origin: Vector2 = area["origin"]
	var dir: Vector2 = area.get("dir", Vector2.RIGHT)
	var pts := PackedVector2Array()
	if str(area.get("shape", "")) == "cone":
		var radius := float(area.get("radius_px", 0.0))
		var half := deg_to_rad(float(area.get("angle_deg", 90.0))) / 2.0
		pts.append(origin)
		for i: int in CONE_ARC_STEPS + 1:
			pts.append(origin + dir.rotated(-half + 2.0 * half * i / CONE_ARC_STEPS) * radius)
		return pts
	var length := float(area.get("length_px", 0.0))
	var side := Vector2(-dir.y, dir.x) * float(area.get("width_px", 0.0)) / 2.0
	pts.append(origin + side)
	pts.append(origin + dir * length + side)
	pts.append(origin + dir * length - side)
	pts.append(origin - side)
	return pts


## Anillo de runa: relleno tenue, borde con contorno oscuro, anillo interior discontinuo girando y cuatro marcas.
func _rune(center: Vector2, radius: float, main: Color, light: Color, t: float) -> void:
	var r := maxf(4.0, radius)
	PixelDraw.disc(self, center, r, Color(main, 0.13))
	PixelDraw.ring(self, center, r + 1.0, Color(UiTheme.OUTLINE, 0.6))
	PixelDraw.ring(self, center, r, main)
	if r > 10.0:
		PixelDraw.ring(self, center, r - 3.0, Color(light, 0.75), 3, t)
	for k: int in 4:
		var a := TAU * k / 4.0 + t * 0.02
		var p := (center + Vector2(cos(a), sin(a)) * (r - 1.0)).round()
		draw_rect(Rect2(p - Vector2.ONE, Vector2(2, 2)), light)
