class_name MovementStep
## Traducción literal de MovementStep.cs (regla 6 de CLAUDE.md). Debe pasar shared/test-vectors/movement.json en GUT.
## Trabaja en píxeles: caja de 10×6 px en los pies, paso = speed · 16 · dt, eje X y luego Y, borde exacto sin colisión,
## redondeo a 2 decimales (half away from zero) al final de cada tick.

const TILE_SIZE := 16
const DT_SECONDS := 0.05
const HALF_X := 5.0
const HALF_Y := 3.0
const DIAGONAL := 0.7071067811865476


static func step(x: float, y: float, dx: int, dy: int, speed_tiles_per_sec: float, grid: CollisionGrid, dt: float = DT_SECONDS) -> Vector2:
	dx = clampi(dx, -1, 1)
	dy = clampi(dy, -1, 1)
	if dx == 0 and dy == 0:
		return Vector2(round2(x), round2(y))
	var step_px := speed_tiles_per_sec * TILE_SIZE * dt
	var dir_x := float(dx)
	var dir_y := float(dy)
	if dx != 0 and dy != 0:
		dir_x *= DIAGONAL
		dir_y *= DIAGONAL
	var nx := x + dir_x * step_px
	if dx != 0:
		var hit := _collides(grid, nx, y)
		if hit.x >= 0:
			nx = hit.x * TILE_SIZE - HALF_X if dx > 0 else (hit.x + 1) * TILE_SIZE + HALF_X
	var ny := y + dir_y * step_px
	if dy != 0:
		var hit_y := _collides(grid, nx, ny)
		if hit_y.y >= 0:
			ny = hit_y.y * TILE_SIZE - HALF_Y if dy > 0 else (hit_y.y + 1) * TILE_SIZE + HALF_Y
	return Vector2(round2(nx), round2(ny))


## Devuelve (col, fila) del primer tile sólido que toca el AABB, o (-1, -1) si no hay colisión.
static func _collides(grid: CollisionGrid, x: float, y: float) -> Vector2i:
	var min_col := floori((x - HALF_X) / TILE_SIZE)
	var max_col := ceili((x + HALF_X) / TILE_SIZE) - 1
	var min_row := floori((y - HALF_Y) / TILE_SIZE)
	var max_row := ceili((y + HALF_Y) / TILE_SIZE) - 1
	for row: int in range(min_row, max_row + 1):
		for col: int in range(min_col, max_col + 1):
			if grid.is_solid(col, row):
				return Vector2i(col, row)
	return Vector2i(-1, -1)


## Redondeo a 2 decimales, mitad lejos de cero (como Math.Round(v, 2, MidpointRounding.AwayFromZero)).
static func round2(v: float) -> float:
	var scaled := v * 100.0
	var r: float = floorf(absf(scaled) + 0.5)
	return (r if scaled >= 0.0 else -r) / 100.0
