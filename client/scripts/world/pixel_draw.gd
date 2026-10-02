class_name PixelDraw
## Dibujo en la rejilla de píxeles lógicos (sin polígonos suavizados): con `canvas_items` los círculos de Godot salen lisos
## a la resolución real, así que anillos, elipses y rellenos se pintan píxel a píxel o por tramos horizontales.


## Anillo elíptico de 1 px con contorno oscuro opcional por fuera (selección bajo los pies).
static func ellipse_ring(ci: CanvasItem, center: Vector2, rx: int, ry: int, color: Color, outline: Color = Color(0, 0, 0, 0)) -> void:
	var pts := ellipse_points(rx, ry)
	if outline.a > 0.0:
		for p: Vector2i in pts:
			ci.draw_rect(Rect2(center + Vector2(p) + Vector2(0, 1), Vector2.ONE), outline)
	for p: Vector2i in pts:
		ci.draw_rect(Rect2(center + Vector2(p), Vector2.ONE), color)


## Píxeles del borde de una elipse (centrada en 0, enteros).
static func ellipse_points(rx: int, ry: int) -> Array[Vector2i]:
	var out: Array[Vector2i] = []
	var seen := {}
	var steps := maxi(16, (rx + ry) * 4)
	for i: int in steps:
		var a := TAU * i / steps
		var p := Vector2i(roundi(cos(a) * rx), roundi(sin(a) * ry))
		if not seen.has(p):
			seen[p] = true
			out.append(p)
	return out


## Círculo de radio `r` (px): relleno por tramos horizontales de 1 px de alto.
static func disc(ci: CanvasItem, center: Vector2, r: float, color: Color) -> void:
	var c := center.round()
	var ri := int(r)
	for dy: int in range(-ri, ri + 1):
		var half := floorf(sqrt(maxf(0.0, r * r - dy * dy)))
		if half > 0.0:
			ci.draw_rect(Rect2(c.x - half, c.y + dy, half * 2.0, 1.0), color)


## Borde de un círculo de 1 px; `dash` > 0 deja huecos (anillo de runas) y `phase` lo gira.
static func ring(ci: CanvasItem, center: Vector2, r: float, color: Color, dash: int = 0, phase: float = 0.0) -> void:
	var c := center.round()
	var steps := maxi(24, int(TAU * r))
	var last := Vector2i(1 << 20, 0)
	for i: int in steps:
		if dash > 0 and ((i + int(phase)) / dash) % 2 == 1:
			continue
		var a := TAU * i / steps
		var p := Vector2i(roundi(cos(a) * r), roundi(sin(a) * r))
		if p != last:
			ci.draw_rect(Rect2(c + Vector2(p), Vector2.ONE), color)
			last = p


static func rect_outline(ci: CanvasItem, r: Rect2, color: Color) -> void:
	var p := r.position.round()
	var s := r.size.round()
	ci.draw_rect(Rect2(p.x, p.y, s.x, 1), color)
	ci.draw_rect(Rect2(p.x, p.y + s.y - 1, s.x, 1), color)
	ci.draw_rect(Rect2(p.x, p.y, 1, s.y), color)
	ci.draw_rect(Rect2(p.x + s.x - 1, p.y, 1, s.y), color)
