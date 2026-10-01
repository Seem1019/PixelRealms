class_name InterpolationBuffer
extends RefCounted
## Interpolación de entidades remotas (HU-023 CA3): guarda (tiempo, posición) de los snapshots y dibuja 100 ms en el pasado,
## interpolando entre los dos snapshots que rodean render_time. Si faltan datos > 250 ms extrapola máx. 100 ms y congela.

const RENDER_DELAY_MS := 100.0
const MAX_EXTRAPOLATION_MS := 100.0
const STALE_MS := 250.0

var _samples: Array[Dictionary] = []  # [{t, pos}] ordenados por t


func push(time_ms: float, pos: Vector2) -> void:
	_samples.append({"t": time_ms, "pos": pos})
	while _samples.size() > 20:
		_samples.remove_at(0)


func is_empty() -> bool:
	return _samples.is_empty()


func size() -> int:
	return _samples.size()


func clear() -> void:
	_samples.clear()


func sample(now_ms: float) -> Vector2:
	if _samples.is_empty():
		return Vector2.ZERO
	var render_time := now_ms - RENDER_DELAY_MS
	var last: Dictionary = _samples[_samples.size() - 1]
	if render_time >= float(last["t"]):
		# Sin dato posterior: extrapolar como mucho 100 ms y luego congelar.
		if _samples.size() >= 2 and render_time - float(last["t"]) <= MAX_EXTRAPOLATION_MS:
			var prev: Dictionary = _samples[_samples.size() - 2]
			var dt := float(last["t"]) - float(prev["t"])
			if dt > 0.0:
				var vel: Vector2 = ((last["pos"] as Vector2) - (prev["pos"] as Vector2)) / dt
				return (last["pos"] as Vector2) + vel * (render_time - float(last["t"]))
		return last["pos"]
	for i: int in range(_samples.size() - 1):
		var a: Dictionary = _samples[i]
		var b: Dictionary = _samples[i + 1]
		if render_time >= float(a["t"]) and render_time <= float(b["t"]):
			var span := float(b["t"]) - float(a["t"])
			var f := 0.0 if span <= 0.0 else (render_time - float(a["t"])) / span
			return (a["pos"] as Vector2).lerp(b["pos"], f)
	return _samples[0]["pos"]
