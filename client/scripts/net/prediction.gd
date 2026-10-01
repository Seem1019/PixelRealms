class_name Prediction
extends RefCounted
## Predicción y reconciliación del jugador propio (HU-022, docs/architecture.md §4). Guarda los inputs no confirmados;
## al llegar un Snapshot fija la posición autoritativa y re-simula los inputs con seq > ackSeq con MovementStep.

const SNAP_THRESHOLD_PX := 2.0
const LERP_MS := 100.0

var position: Vector2 = Vector2.ZERO  ## posición predicha (px)
var render_position: Vector2 = Vector2.ZERO  ## posición dibujada (suaviza correcciones < 2 px)
var speed_tiles_per_sec: float = 4.0
var last_error_px: float = 0.0
var pending: Array[Dictionary] = []  # [{seq, dx, dy}]

var _grid: CollisionGrid
var _lerp_from: Vector2 = Vector2.ZERO
var _lerp_t: float = 1.0


func setup(grid: CollisionGrid, start_px: Vector2, speed: float) -> void:
	_grid = grid
	position = start_px
	render_position = start_px
	speed_tiles_per_sec = speed
	pending.clear()


## Aplica un input localmente (un tick fijo de 50 ms) y lo guarda como pendiente.
func apply_input(seq: int, dx: int, dy: int) -> void:
	pending.append({"seq": seq, "dx": dx, "dy": dy})
	position = MovementStep.step(position.x, position.y, dx, dy, speed_tiles_per_sec, _grid)
	if _lerp_t >= 1.0:
		render_position = position


## Reconcilia con la posición autoritativa del Snapshot: descarta confirmados, fija y re-simula los pendientes.
func reconcile(server_px: Vector2, ack_seq: int, server_speed: float) -> void:
	speed_tiles_per_sec = server_speed
	var kept: Array[Dictionary] = []
	for inp: Dictionary in pending:
		if int(inp["seq"]) > ack_seq:
			kept.append(inp)
	pending = kept
	var predicted_before := position
	var pos := server_px
	for inp: Dictionary in pending:
		pos = MovementStep.step(pos.x, pos.y, int(inp["dx"]), int(inp["dy"]), speed_tiles_per_sec, _grid)
	last_error_px = pos.distance_to(predicted_before)
	position = pos
	if last_error_px >= SNAP_THRESHOLD_PX:
		render_position = pos  # salto
		_lerp_t = 1.0
	elif last_error_px > 0.0:
		_lerp_from = render_position
		_lerp_t = 0.0


## Avanza el suavizado del render (llamar cada frame).
func update_render(delta: float) -> void:
	if _lerp_t < 1.0:
		_lerp_t = minf(1.0, _lerp_t + delta * 1000.0 / LERP_MS)
		render_position = _lerp_from.lerp(position, _lerp_t)
	else:
		render_position = position
