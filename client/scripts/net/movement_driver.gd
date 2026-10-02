class_name MovementDriver
extends RefCounted
## Movimiento propio (HU-021 CA1, HU-022): simula a ticks fijos de 50 ms (como el servidor, sin depender de los fps) y
## envía un MoveInput por tick mientras hay movimiento, más uno con 0,0 al parar. Así cada seq pendiente es exactamente un
## paso del servidor y la reconciliación re-simula lo que el servidor aún no ha aplicado.

const TICK_SEC := 0.05
## Igual que GameConstants.MaxCatchUpTicks: tras un tirón no se recuperan más de 3 ticks de golpe.
const MAX_CATCH_UP_TICKS := 3
const EPSILON := 0.000001

var seq: int = 0
var _acc: float = 0.0
var _last_dx: int = 0
var _last_dy: int = 0


## Sesión nueva (Welcome de entrada al mundo): el servidor empieza a contar seq desde 0.
func reset() -> void:
	seq = 0
	_acc = 0.0
	_last_dx = 0
	_last_dy = 0


## Tras un cambio de mapa el servidor ha parado al jugador: el próximo input con movimiento se envía aunque no cambie.
func forget_last_input() -> void:
	_last_dx = 0
	_last_dy = 0


## Avanza `delta` segundos con la dirección pulsada. Devuelve los MoveInput a enviar ({seq, dx, dy}) y aplica cada tick a
## `prediction`.
func advance(delta: float, dx: int, dy: int, prediction: Prediction) -> Array[Dictionary]:
	var out: Array[Dictionary] = []
	_acc = minf(_acc + delta, TICK_SEC * MAX_CATCH_UP_TICKS)
	while _acc >= TICK_SEC - EPSILON:
		_acc -= TICK_SEC
		var moving := dx != 0 or dy != 0
		if moving or dx != _last_dx or dy != _last_dy:
			seq += 1
			out.append({"seq": seq, "dx": dx, "dy": dy})
			_last_dx = dx
			_last_dy = dy
		if moving:
			prediction.apply_input(seq, dx, dy)
	return out
