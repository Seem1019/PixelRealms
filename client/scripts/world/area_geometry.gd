class_name AreaGeometry
## Traducción de AreaShape.cs y LineOfSight.ClearDistance (HU-102): si un cono o una línea tocan el cuadro del cuerpo, y cuánto
## avanza una línea hasta la primera pared. Solo para la vista previa al apuntar: el servidor decide. Los casos de
## shared/test-vectors/area_shapes.json (copiados a tests/vectors/) deben pasar aquí y en el servidor. Funciona en cualquier
## unidad (casillas o píxeles) mientras todo vaya en la misma, salvo `clear_distance`, que va en casillas.


## Distancia al cuadrado de `p` al punto más cercano del cuadro del cuerpo con los pies en `feet`.
static func distance_squared_to_box(p: Vector2, feet: Vector2, half_w: float, above: float, below: float) -> float:
	var dx := maxf(maxf(feet.x - half_w - p.x, p.x - feet.x - half_w), 0.0)
	var dy := maxf(maxf(feet.y - above - p.y, p.y - feet.y - below), 0.0)
	return dx * dx + dy * dy


## Cono de apertura `angle_deg` (≤ 180) desde `vertex` hacia `dir` (unitaria): el cuadro recortado por los dos bordes de la cuña
## queda, en algún punto, a ≤ `radius` del vértice.
static func cone_touches(vertex: Vector2, dir: Vector2, radius: float, angle_deg: float, feet: Vector2, half_w: float, above: float, below: float) -> bool:
	var dist_sq := distance_squared_to_box(vertex, feet, half_w, above, below)
	if dist_sq > radius * radius:
		return false
	if dist_sq == 0.0:
		return true
	var half := deg_to_rad(angle_deg) / 2.0
	var n1 := _inward_normal(dir.rotated(half), dir)
	var n2 := _inward_normal(dir.rotated(-half), dir)
	var poly: Array[Vector2] = [
		Vector2(feet.x - half_w, feet.y - above), Vector2(feet.x + half_w, feet.y - above),
		Vector2(feet.x + half_w, feet.y + below), Vector2(feet.x - half_w, feet.y + below)]
	poly = _clip(poly, vertex, n1)
	poly = _clip(poly, vertex, n2)
	if poly.is_empty():
		return false
	for i: int in poly.size():
		if _segment_distance_squared(vertex, poly[i], poly[(i + 1) % poly.size()]) <= radius * radius:
			return true
	return false


## Rectángulo de `length` × `width` desde `origin` hacia `dir` (unitaria) contra el cuadro: ejes separadores X, Y, dir y su normal.
static func line_touches(origin: Vector2, dir: Vector2, length: float, width: float, feet: Vector2, half_w: float, above: float, below: float) -> bool:
	var by := (above + below) / 2.0
	var box := Vector2(feet.x, feet.y + (below - above) / 2.0)
	var half := length / 2.0
	var hw := width / 2.0
	var c := origin + dir * half
	var n := Vector2(-dir.y, dir.x)
	var d := box - c
	return absf(d.x) <= half_w + absf(dir.x) * half + absf(n.x) * hw \
		and absf(d.y) <= by + absf(dir.y) * half + absf(n.y) * hw \
		and absf(dir.dot(d)) <= half + absf(dir.x) * half_w + absf(dir.y) * by \
		and absf(n.dot(d)) <= hw + absf(n.x) * half_w + absf(n.y) * by


## Cuánto avanza un rayo (casillas) desde `from` hacia `dir` (unitaria) antes de entrar en una casilla que tapa la vista, como
## mucho `max_distance`. La casilla de origen no cuenta; fuera del mapa tapa.
static func clear_distance(grid: CollisionGrid, from: Vector2, dir: Vector2, max_distance: float) -> float:
	var x := floori(from.x)
	var y := floori(from.y)
	var step_x := signi(int(signf(dir.x)))
	var step_y := signi(int(signf(dir.y)))
	if step_x == 0 and step_y == 0:
		return max_distance
	var delta_x := 1.0 / absf(dir.x) if step_x != 0 else INF
	var delta_y := 1.0 / absf(dir.y) if step_y != 0 else INF
	var next_x := INF
	if step_x > 0:
		next_x = (x + 1 - from.x) * delta_x
	elif step_x < 0:
		next_x = (from.x - x) * delta_x
	var next_y := INF
	if step_y > 0:
		next_y = (y + 1 - from.y) * delta_y
	elif step_y < 0:
		next_y = (from.y - y) * delta_y
	while true:
		var t: float
		if next_x < next_y:
			t = next_x
			x += step_x
			next_x += delta_x
		else:
			t = next_y
			y += step_y
			next_y += delta_y
		if t >= max_distance:
			return max_distance
		if grid.blocks_sight(x, y):
			return t
	return max_distance


static func _inward_normal(edge: Vector2, dir: Vector2) -> Vector2:
	var n := Vector2(edge.y, -edge.x)
	return n if n.dot(dir) >= 0.0 else -n


## Sutherland-Hodgman contra el semiplano normal·(p − vértice) ≥ 0.
static func _clip(src: Array[Vector2], vertex: Vector2, normal: Vector2) -> Array[Vector2]:
	var out: Array[Vector2] = []
	for i: int in src.size():
		var p := src[i]
		var q := src[(i + 1) % src.size()]
		var dp := normal.dot(p - vertex)
		var dq := normal.dot(q - vertex)
		if dp >= 0.0:
			out.append(p)
		if (dp >= 0.0) != (dq >= 0.0):
			out.append(p + (q - p) * (dp / (dp - dq)))
	return out


static func _segment_distance_squared(p: Vector2, a: Vector2, b: Vector2) -> float:
	var ab := b - a
	var len_sq := ab.length_squared()
	var t := clampf((p - a).dot(ab) / len_sq, 0.0, 1.0) if len_sq > 0.0 else 0.0
	return (a + ab * t).distance_squared_to(p)
