class_name Approach
extends RefCounted
## Acercarse solo al objetivo (HU-095): mientras está activo, da cada tick la dirección de 8 vías hacia el objetivo hasta
## quedar a `range_px − ARRIVE_MARGIN_PX`. El mundo convierte esa dirección en MoveInput normales (con predicción): el
## servidor sigue validando colisión, alcance y línea de visión. Lógica pura: sin nodos ni red, para testearla.

## Margen dentro del alcance para llegar: la posición del servidor va un poco por detrás de la predicha.
const ARRIVE_MARGIN_PX := 4.0
## Sin acercarse al menos STUCK_MIN_PX en STUCK_MS (p. ej. contra una pared) se abandona.
const STUCK_MS := 1000
const STUCK_MIN_PX := 2.0
## tan(22,5°): por debajo, el eje menor no cuenta y se camina recto.
const DIAGONAL_RATIO := 0.4142

enum State { IDLE, MOVING, ARRIVED, STUCK }

var state: State = State.IDLE
var target_id: int = -1
var range_px: float = 0.0
## Qué hacer al llegar (lanzar el hechizo pendiente); vacío para el básico, que ya está activado en el servidor.
var on_arrive: Callable = Callable()
var _best_dist: float = INF
var _progress_ms: int = 0


func is_active() -> bool:
	return state == State.MOVING


func start(entity_id: int, reach_px: float, now_ms: int, arrive: Callable = Callable()) -> void:
	state = State.MOVING
	target_id = entity_id
	range_px = reach_px
	on_arrive = arrive
	_best_dist = INF
	_progress_ms = now_ms


func cancel() -> void:
	state = State.IDLE
	target_id = -1
	on_arrive = Callable()


## ¿Está `to` dentro del alcance desde `from` (con el margen de llegada)?
static func in_reach(from: Vector2, to: Vector2, reach_px: float) -> bool:
	return from.distance_to(to) <= maxf(0.0, reach_px - ARRIVE_MARGIN_PX)


## Dirección de 8 vías (−1/0/1 por eje) de `from` hacia `to`.
static func direction(from: Vector2, to: Vector2) -> Vector2i:
	var d := to - from
	var dx := int(signf(d.x)) if absf(d.x) >= DIAGONAL_RATIO * absf(d.y) and absf(d.x) > 0.5 else 0
	var dy := int(signf(d.y)) if absf(d.y) >= DIAGONAL_RATIO * absf(d.x) and absf(d.y) > 0.5 else 0
	return Vector2i(dx, dy)


## Un paso: la dirección a caminar este tick (cero si llegó, se atascó o no está activo). Cambia `state` a ARRIVED o
## STUCK cuando toca; el llamador ejecuta `on_arrive` al llegar.
func update(self_pos: Vector2, target_pos: Vector2, now_ms: int) -> Vector2i:
	if state != State.MOVING:
		return Vector2i.ZERO
	var dist := self_pos.distance_to(target_pos)
	if in_reach(self_pos, target_pos, range_px):
		state = State.ARRIVED
		return Vector2i.ZERO
	if dist < _best_dist - STUCK_MIN_PX:
		_best_dist = dist
		_progress_ms = now_ms
	elif now_ms - _progress_ms >= STUCK_MS:
		state = State.STUCK
		return Vector2i.ZERO
	return direction(self_pos, target_pos)
