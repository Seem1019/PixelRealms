class_name FxBench
extends Node
## HU-089 CA3: prueba de rendimiento de los efectos con `/fxbench`, solo en el propio cliente (no envía nada al servidor):
## durante DURATION_SEC mantiene MARKS marcas de área y TEXTS números flotantes alrededor del personaje y al acabar avisa del
## FPS p5 (el CA falla por debajo de MIN_P5_FPS en el equipo de referencia). Así se mide en el navegador sin juntar a 30 jugadores.

signal finished(p5_fps: float)

const DURATION_SEC := 20.0
const WARMUP_SEC := 0.5
const MARKS := 24
const TEXTS := 40
const MIN_P5_FPS := 45.0
## Ids negativos para no pisar las marcas ni los números de entidades reales; los números rotan entre TEXT_IDS ids para no
## chocar con el tope de números por entidad y segundo.
const MARK_ID_BASE := -1000
const TEXT_ID_BASE := -2000
const TEXT_IDS := 64

var reticle: AoeReticle
var floating: FloatingText
## Posición del personaje propio (px del mundo).
var center: Callable
var running: bool = false

var _elapsed: float = 0.0
var _frame_ms: Array[float] = []
var _next_text: int = 0


func start() -> void:
	running = true
	_elapsed = 0.0
	_frame_ms.clear()
	var c: Vector2 = center.call()
	for i: int in MARKS:
		var at := c + Vector2.from_angle(TAU * i / MARKS) * (40.0 + 24.0 * (i % 3))
		reticle.set_mark(MARK_ID_BASE - i, at, 24.0, i % 2 == 0, int(DURATION_SEC * 1000))
	GameState.notice.emit("Prueba de efectos: %d s con %d marcas de área y %d números…" % [int(DURATION_SEC), MARKS, TEXTS])


func _process(delta: float) -> void:
	if not running:
		return
	_elapsed += delta
	if _elapsed > WARMUP_SEC:
		_frame_ms.append(delta * 1000.0)
	var c: Vector2 = center.call()
	for i: int in maxi(0, TEXTS - floating.visible_count()):
		var at := c + Vector2(randf_range(-80, 80), randf_range(-40, 60))
		floating.show_event(TEXT_ID_BASE - _next_text % TEXT_IDS, "dmg", randi_range(1, 99), randf() < 0.1, at)
		_next_text += 1
	if _elapsed >= DURATION_SEC:
		_finish()


## FPS del 5 % de cuadros más lentos: 1000 / percentil 95 del tiempo de cuadro.
static func p5_fps(frame_ms: Array[float]) -> float:
	if frame_ms.is_empty():
		return 0.0
	var sorted := frame_ms.duplicate()
	sorted.sort()
	var p95: float = sorted[mini(sorted.size() - 1, int(ceil(sorted.size() * 0.95)) - 1)]
	return 1000.0 / maxf(p95, 0.001)


func _finish() -> void:
	running = false
	for i: int in MARKS:
		reticle.clear_mark(MARK_ID_BASE - i)
	var fps := p5_fps(_frame_ms)
	GameState.notice.emit("FPS p5: %.1f en %d cuadros (objetivo ≥ %d): %s" % [fps, _frame_ms.size(), int(MIN_P5_FPS), "OK" if fps >= MIN_P5_FPS else "por debajo"])
	finished.emit(fps)
