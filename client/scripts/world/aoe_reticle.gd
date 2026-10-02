class_name AoeReticle
extends Node2D
## Marca de área (HU-086 CA1/CA3): anillo pixelado con runas de `aoeRadius` bajo el cursor mientras se apunta (dorado; rojo
## si está fuera de alcance) y
## marcas en el suelo de casteos ajenos (CastStarted{targetPos}) hasta que terminan. La forma y el tamaño salen del contenido.
## Una marca se borra con su CastEnded, si el lanzador sale de la AOI (EntityDespawn) o, si nada de eso llega, al pasar la
## duración del casteo más EXPIRY_MARGIN_MS. Se dibuja en el suelo (debajo de las entidades) con un relleno muy tenue, para
## leerse sin tapar a nadie: borde de 1 px con contorno oscuro y un anillo interior discontinuo que gira despacio.

const POOL_SIZE := 32
const MAX_VISIBLE := 24
const EXPIRY_MARGIN_MS := 500

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

var _marks: Dictionary = {}  # caster_id → {pos, radius, enemy, expires_ms}


func set_mark(caster_id: int, pos_px: Vector2, radius_px: float, enemy: bool, duration_ms: int = 0) -> void:
	if _marks.size() >= MAX_VISIBLE and not _marks.has(caster_id) and not enemy:
		return  # las enemigas nunca se ocultan (ADR-018)
	_marks[caster_id] = {"pos": pos_px, "radius": radius_px, "enemy": enemy, "expires_ms": Time.get_ticks_msec() + duration_ms + EXPIRY_MARGIN_MS}
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
		_rune(md["pos"], float(md["radius"]), Color("e83b3b") if enemy else Color("4d9be6"), Color("fb6b1d") if enemy else Color("8fd3ff"), t)
	if aiming:
		var main := Color("f9c22b") if aim_in_range else Color("e83b3b")
		_rune(aim_pos, aim_radius_px, main, Color("fbff86") if aim_in_range else Color("f68181"), t)


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
