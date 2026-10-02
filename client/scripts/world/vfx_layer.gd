class_name VfxLayer
extends Node2D
## Efectos de hechizo (HU-091): brillo bajo los pies al castear, proyectiles, impactos, chispas de cura y estallidos de área,
## con las hojas de tools/art/gen_vfx.py en assets/sprites/vfx/<nombre>.png (+ .json con frameSize, frames, fps, loop y,
## en los proyectiles, una fila por cada una de las 8 direcciones: no se rotan). Reserva fija de nodos: con MAX_ACTIVE vivos,
## uno nuevo recicla el más antiguo; fuera de la vista (`view_rect` + CULL_MARGIN) no se crea nada.
## Los brillos de casteo van en `ground` (bajo los cuerpos); el resto en este nodo, que el mundo pone por encima de las
## entidades y por debajo de las placas, los números y la interfaz.

const POOL_SIZE := 64
const MAX_ACTIVE := 48
const CULL_MARGIN := 32.0
const DIRS8 := ["e", "se", "s", "sw", "w", "nw", "n", "ne"]

## Nodo hermano bajo las entidades donde van los brillos de casteo (lo asigna el mundo; si falta, van aquí).
var ground: Node2D
## Rectángulo visible del mundo; vacío = sin recorte (tests).
var view_rect: Rect2 = Rect2()

static var _frames_cache: Dictionary = {}  # nombre → SpriteFrames (null si falta la hoja)
static var _meta_cache: Dictionary = {}

var _pool: Array[AnimatedSprite2D] = []
## Activos, del más antiguo al más nuevo: {sprite, name, end_ms (-1 = hasta que lo quiten), from, to, start_ms, travel_ms,
## follow (Callable → Vector2), on_arrive (Callable), key}
var _active: Array[Dictionary] = []


func _ready() -> void:
	for i: int in POOL_SIZE:
		var s := AnimatedSprite2D.new()
		s.centered = true
		s.visible = false
		add_child(s)
		_pool.append(s)


static func meta(sheet: String) -> Dictionary:
	if _meta_cache.has(sheet):
		return _meta_cache[sheet]
	var m := {}
	var path := "res://assets/sprites/vfx/%s.json" % sheet
	if FileAccess.file_exists(path):
		var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
		if parsed is Dictionary:
			m = parsed
	_meta_cache[sheet] = m
	return m


## SpriteFrames de una hoja de efectos: una animación "default" o, si la hoja trae filas, una por dirección.
static func frames_for(sheet: String) -> SpriteFrames:
	if _frames_cache.has(sheet):
		return _frames_cache[sheet]
	var path := "res://assets/sprites/vfx/%s.png" % sheet
	var m := meta(sheet)
	if m.is_empty() or not ResourceLoader.exists(path):
		_frames_cache[sheet] = null
		return null
	var tex := load(path) as Texture2D
	var size: Array = m.get("frameSize", [16, 16])
	var w := int(size[0])
	var h := int(size[1])
	var rows: Array = m.get("rows", ["default"])
	var sf := SpriteFrames.new()
	sf.remove_animation("default")
	for r: int in rows.size():
		var anim := str(rows[r])
		sf.add_animation(anim)
		sf.set_animation_speed(anim, float(m.get("fps", 10)))
		sf.set_animation_loop(anim, bool(m.get("loop", false)))
		for c: int in int(m.get("frames", 1)):
			var at := AtlasTexture.new()
			at.atlas = tex
			at.region = Rect2(c * w, r * h, w, h)
			sf.add_frame(anim, at)
	_frames_cache[sheet] = sf
	return sf


static func duration_ms(sheet: String) -> int:
	var m := meta(sheet)
	return roundi(1000.0 * int(m.get("frames", 1)) / maxf(1.0, float(m.get("fps", 10))))


## Fila de 8 direcciones más cercana al vector (sin rotar el sprite).
static func dir8(v: Vector2) -> String:
	if v == Vector2.ZERO:
		return "e"
	var idx := posmod(roundi(v.angle() / (PI / 4.0)), 8)
	return DIRS8[idx]


func active_count() -> int:
	return _active.size()


func visible_on_screen(p: Vector2) -> bool:
	return view_rect.size == Vector2.ZERO or view_rect.grow(CULL_MARGIN).has_point(p)


## Efecto de un disparo en `pos` (impacto, cura, estallido). `delay_ms` lo retrasa (estallidos escalonados de un área).
func play_once(sheet: String, pos: Vector2, delay_ms: int = 0) -> bool:
	if not visible_on_screen(pos):
		return false
	var e := _take(sheet, pos, false)
	if e.is_empty():
		return false
	var now := Time.get_ticks_msec()
	e["start_ms"] = now + delay_ms
	e["end_ms"] = now + delay_ms + duration_ms(sheet)
	var s: AnimatedSprite2D = e["sprite"]
	s.visible = delay_ms <= 0
	if delay_ms <= 0:
		s.play("default")
	return true


## Efecto en bucle que sigue a `follow` (Callable → Vector2 de los pies) hasta `stop(key)`: brillo de casteo.
func play_loop(sheet: String, key: String, follow: Callable) -> bool:
	stop(key)
	var pos: Vector2 = follow.call()
	if not visible_on_screen(pos):
		return false
	var e := _take(sheet, pos, true)
	if e.is_empty():
		return false
	e["key"] = key
	e["follow"] = follow
	e["end_ms"] = -1
	(e["sprite"] as AnimatedSprite2D).play("default")
	return true


func stop(key: String) -> void:
	for i: int in range(_active.size() - 1, -1, -1):
		if str(_active[i].get("key", "")) == key:
			_release(i)


## Proyectil de `from` a `to` (Callable → Vector2 para seguir a un objetivo que se mueve) en `travel_ms`; al llegar llama a
## `on_arrive` (impacto y número). Si ni el origen ni el destino se ven, no se dibuja pero `on_arrive` se llama igual.
func launch(sheet: String, from: Vector2, to: Callable, travel_ms: int, on_arrive: Callable) -> bool:
	var dest: Vector2 = to.call()
	if not visible_on_screen(from) and not visible_on_screen(dest):
		get_tree().create_timer(travel_ms / 1000.0).timeout.connect(on_arrive)
		return false
	var e := _take(sheet, from, false)
	if e.is_empty():
		on_arrive.call()
		return false
	var now := Time.get_ticks_msec()
	e["from"] = from
	e["follow"] = to
	e["start_ms"] = now
	e["travel_ms"] = maxi(1, travel_ms)
	e["end_ms"] = now + travel_ms
	e["on_arrive"] = on_arrive
	e["projectile"] = true
	var s: AnimatedSprite2D = e["sprite"]
	var row := dir8(dest - from)
	s.play(row if s.sprite_frames.has_animation(row) else s.sprite_frames.get_animation_names()[0])
	return true


func clear_all() -> void:
	for i: int in range(_active.size() - 1, -1, -1):
		_release(i)


## Toma un nodo de la reserva; con MAX_ACTIVE vivos recicla el más antiguo (sin llamar a su llegada: un proyectil
## reciclado entrega su número al momento).
func _take(sheet: String, pos: Vector2, under_feet: bool) -> Dictionary:
	var frames := frames_for(sheet)
	if frames == null:
		return {}
	if _active.size() >= MAX_ACTIVE:
		_release(0, true)
	var s: AnimatedSprite2D = null
	for cand: AnimatedSprite2D in _pool:
		if not cand.visible and not _in_use(cand):
			s = cand
			break
	if s == null:
		_release(0, true)
		return _take(sheet, pos, under_feet)
	var parent: Node2D = ground if under_feet and ground != null else self
	if s.get_parent() != parent:
		s.reparent(parent, false)
	s.sprite_frames = frames
	s.position = pos.round()
	s.offset = Vector2(0, -2) if under_feet else Vector2.ZERO
	s.visible = true
	var e := {"sprite": s, "name": sheet, "start_ms": Time.get_ticks_msec()}
	_active.append(e)
	return e


func _in_use(s: AnimatedSprite2D) -> bool:
	for e: Dictionary in _active:
		if e["sprite"] == s:
			return true
	return false


func _release(i: int, deliver: bool = false) -> void:
	var e: Dictionary = _active[i]
	_active.remove_at(i)
	var s: AnimatedSprite2D = e["sprite"]
	s.stop()
	s.visible = false
	if deliver and e.has("on_arrive"):
		(e["on_arrive"] as Callable).call()


func _process(_delta: float) -> void:
	var now := Time.get_ticks_msec()
	for i: int in range(_active.size() - 1, -1, -1):
		if i >= _active.size():
			continue
		var e: Dictionary = _active[i]
		var s: AnimatedSprite2D = e["sprite"]
		if e.has("projectile"):
			var dest: Vector2 = (e["follow"] as Callable).call()
			var t := clampf(float(now - int(e["start_ms"])) / float(e["travel_ms"]), 0.0, 1.0)
			var from: Vector2 = e["from"]
			s.position = from.lerp(dest, t).round()
			if t >= 1.0:
				var arrive: Callable = e["on_arrive"]
				_release(i)
				arrive.call()
			continue
		if e.has("follow"):
			s.position = ((e["follow"] as Callable).call() as Vector2).round()
			continue
		if not s.visible and now >= int(e["start_ms"]):
			s.visible = true
			s.play("default")
		if int(e["end_ms"]) >= 0 and now >= int(e["end_ms"]):
			_release(i)
