class_name BodyShape
## Cuadro del cuerpo de una entidad respecto a sus pies (rules.combat.body*): el que se dibuja y el que usa el servidor para
## decidir a quién alcanza un área (TargetResolver.DistanceSquaredToBody). Solo visual en el cliente: el servidor decide.

const TILE_PX := 16.0


static func rect_px(feet_px: Vector2) -> Rect2:
	var half_w := float(Content.rule("combat", "bodyHalfWidthTiles", 0.375)) * TILE_PX
	var above := float(Content.rule("combat", "bodyHeightAboveFeetTiles", 0.75)) * TILE_PX
	var below := float(Content.rule("combat", "bodyDepthBelowFeetTiles", 0.25)) * TILE_PX
	return Rect2(feet_px.x - half_w, feet_px.y - above, half_w * 2.0, above + below)


## Distancia al cuadrado (px²) del centro al punto más cercano del cuadro del cuerpo.
static func distance_squared_to_body(center_px: Vector2, feet_px: Vector2) -> float:
	var r := rect_px(feet_px)
	var nearest := Vector2(clampf(center_px.x, r.position.x, r.end.x), clampf(center_px.y, r.position.y, r.end.y))
	return nearest.distance_squared_to(center_px)


## Línea de visión en casillas, traducción de LineOfSight.cs: Bresenham por la rejilla; la casilla de origen y la de destino
## no bloquean.
static func line_of_sight(grid: CollisionGrid, from_tiles: Vector2, to_tiles: Vector2) -> bool:
	var x0 := floori(from_tiles.x)
	var y0 := floori(from_tiles.y)
	var x1 := floori(to_tiles.x)
	var y1 := floori(to_tiles.y)
	var start := Vector2i(x0, y0)
	var dx := absi(x1 - x0)
	var dy := -absi(y1 - y0)
	var sx := 1 if x0 < x1 else -1
	var sy := 1 if y0 < y1 else -1
	var err := dx + dy
	while true:
		if Vector2i(x0, y0) != start and (x0 != x1 or y0 != y1) and grid.blocks_sight(x0, y0):
			return false
		if x0 == x1 and y0 == y1:
			return true
		var e2 := 2 * err
		if e2 >= dy:
			err += dy
			x0 += sx
		if e2 <= dx:
			err += dx
			y0 += sy
	return true


## Lo que hará el servidor con un área en `center_px` (TargetResolver.Circle): candidatos cuyo cuadro toca el círculo,
## ordenados por distancia al cuadro, luego a los pies, luego id; con LOS del centro a los pies; hasta `max_targets`.
## `candidates` = [{id, feet_px}]. Devuelve los ids. Si el centro cae en una casilla que tapa la vista, nada (no_los).
static func area_hits(center_px: Vector2, radius_px: float, max_targets: int, candidates: Array[Dictionary], grid: CollisionGrid) -> Array[int]:
	var hits: Array[int] = []
	var center_tiles := center_px / TILE_PX
	if grid != null and grid.blocks_sight(floori(center_tiles.x), floori(center_tiles.y)):
		return hits
	var inside: Array[Dictionary] = []
	for c: Dictionary in candidates:
		var d := distance_squared_to_body(center_px, c["feet_px"])
		if d <= radius_px * radius_px:
			inside.append({"id": int(c["id"]), "d": d, "feet": (c["feet_px"] as Vector2).distance_squared_to(center_px), "feet_px": c["feet_px"]})
	return _pick(inside, center_tiles, max_targets, grid)


## HU-102: lo mismo para cualquier forma colocada (`AoeReticle.aim_area`): {shape: "circle"|"cone"|"line", origin (px: centro,
## vértice u origen), dir (unitaria), radius_px, angle_deg, length_px (ya recortado en la primera pared), width_px}.
static func shape_hits(area: Dictionary, max_targets: int, candidates: Array[Dictionary], grid: CollisionGrid) -> Array[int]:
	var shape := str(area.get("shape", "circle"))
	var origin: Vector2 = area["origin"]
	if shape == "circle":
		return area_hits(origin, float(area.get("radius_px", 0.0)), max_targets, candidates, grid)
	var dir: Vector2 = area.get("dir", Vector2.RIGHT)
	var half_w := float(Content.rule("combat", "bodyHalfWidthTiles", 0.375)) * TILE_PX
	var above := float(Content.rule("combat", "bodyHeightAboveFeetTiles", 0.75)) * TILE_PX
	var below := float(Content.rule("combat", "bodyDepthBelowFeetTiles", 0.25)) * TILE_PX
	var inside: Array[Dictionary] = []
	for c: Dictionary in candidates:
		var feet: Vector2 = c["feet_px"]
		var touches := false
		if shape == "cone":
			touches = AreaGeometry.cone_touches(origin, dir, float(area.get("radius_px", 0.0)), float(area.get("angle_deg", 90.0)), feet, half_w, above, below)
		else:
			touches = AreaGeometry.line_touches(origin, dir, float(area.get("length_px", 0.0)), float(area.get("width_px", 0.0)), feet, half_w, above, below)
		if touches:
			inside.append({"id": int(c["id"]), "d": distance_squared_to_body(origin, feet), "feet": feet.distance_squared_to(origin), "feet_px": feet})
	return _pick(inside, origin / TILE_PX, max_targets, grid)


## Más cercanos primero (al cuadro, luego a los pies, luego id), con LOS del centro, vértice u origen a los pies, hasta `max_targets`.
static func _pick(inside: Array[Dictionary], from_tiles: Vector2, max_targets: int, grid: CollisionGrid) -> Array[int]:
	var hits: Array[int] = []
	inside.sort_custom(func(a: Dictionary, b: Dictionary) -> bool:
		if a["d"] != b["d"]:
			return a["d"] < b["d"]
		if a["feet"] != b["feet"]:
			return a["feet"] < b["feet"]
		return int(a["id"]) < int(b["id"]))
	for c: Dictionary in inside:
		if hits.size() >= max_targets:
			break
		if grid != null and not line_of_sight(grid, from_tiles, (c["feet_px"] as Vector2) / TILE_PX):
			continue
		hits.append(int(c["id"]))
	return hits


## ¿El círculo toca el cuadro del cuerpo? Misma cuenta que el servidor: punto del cuadro más cercano al centro.
static func circle_touches(center_px: Vector2, radius_px: float, feet_px: Vector2) -> bool:
	return distance_squared_to_body(center_px, feet_px) <= radius_px * radius_px
