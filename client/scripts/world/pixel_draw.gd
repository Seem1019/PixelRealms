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


## Relleno de un polígono convexo (cono o línea apuntados, HU-102) por tramos horizontales de 1 px de alto.
static func polygon_fill(ci: CanvasItem, points: PackedVector2Array, color: Color) -> void:
	if points.size() < 3:
		return
	var top := INF
	var bottom := -INF
	for p: Vector2 in points:
		top = minf(top, p.y)
		bottom = maxf(bottom, p.y)
	for y: int in range(floori(top), ceili(bottom)):
		var row := y + 0.5
		var left := INF
		var right := -INF
		for i: int in points.size():
			var a := points[i]
			var b := points[(i + 1) % points.size()]
			if (a.y <= row) == (b.y <= row):
				continue
			var x := a.x + (row - a.y) / (b.y - a.y) * (b.x - a.x)
			left = minf(left, x)
			right = maxf(right, x)
		if right > left:
			ci.draw_rect(Rect2(roundf(left), y, roundf(right) - roundf(left), 1.0), color)


## Borde de 1 px de un polígono cerrado, píxel a píxel (Bresenham entre vértices).
static func polygon_outline(ci: CanvasItem, points: PackedVector2Array, color: Color) -> void:
	for i: int in points.size():
		var a := Vector2i(points[i].round())
		var b := Vector2i(points[(i + 1) % points.size()].round())
		var dx := absi(b.x - a.x)
		var dy := -absi(b.y - a.y)
		var sx := 1 if a.x < b.x else -1
		var sy := 1 if a.y < b.y else -1
		var err := dx + dy
		while true:
			ci.draw_rect(Rect2(Vector2(a), Vector2.ONE), color)
			if a == b:
				break
			var e2 := 2 * err
			if e2 >= dy:
				err += dy
				a.x += sx
			if e2 <= dx:
				err += dx
				a.y += sy
