class_name FloatingText
extends Node2D
## Números flotantes de combate (HU-038 CA2/CA6, ADR-018): reserva fija de etiquetas, suben y se desvanecen en 1 s.
## Colores de la skill combat-system: blanco daño, amarillo crit con "!", verde cura, gris "Falla"/"Esquiva", azul "Absorbe",
## "Inmune" para controles bloqueados, morado "+N XP" sobre uno mismo, "¡Nivel N!" dorado al subir de nivel (HU-041 CA3; lo ven
## todos los de la AOI). Más de MAX_PER_ENTITY_PER_SEC números por entidad y
## segundo → uno sumado. Los ticks de una misma aura sobre la misma entidad que llegan a menos de GROUP_WINDOW_SEC (varios
## lanzadores con el mismo sangrado) suman en el número que ya está subiendo (HU-038 CA6).

const POOL_SIZE := 48
const MAX_VISIBLE := 40
const MAX_PER_ENTITY_PER_SEC := 6
const LIFETIME := 1.0
const RISE_PX := 18.0
const GROUP_WINDOW_SEC := 0.3

var _pool: Array[Label] = []
var _active: Array[Dictionary] = []  # {label, t, group?, amount?}
var _per_entity: Dictionary = {}  # entity_id → {windowStart, count, pending}


func _ready() -> void:
	for i: int in POOL_SIZE:
		var l := Label.new()
		l.add_theme_font_size_override("font_size", UiTheme.FONT_BODY)
		l.add_theme_constant_override("outline_size", UiTheme.OUTLINE_THICK)  # legible sobre cualquier suelo
		l.add_theme_color_override("font_outline_color", UiTheme.OUTLINE)
		l.visible = false
		l.z_index = 50
		add_child(l)
		_pool.append(l)


## Muestra una entrada de CombatEvents sobre la posición dada (px del mundo). `group` (p. ej. "7:rogue_poison:dmg") junta los
## ticks de una misma aura en un solo número.
func show_event(entity_id: int, kind: String, amount: int, crit: bool, world_pos: Vector2, group: String = "") -> void:
	if not group.is_empty() and _add_to_group(group, kind, amount):
		return
	var text := ""
	var color := Color.WHITE
	match kind:
		"dmg":
			text = str(amount) + ("!" if crit else "")
			color = Color("f9c22b") if crit else Color.WHITE
		"heal":
			text = "+" + str(amount) + ("!" if crit else "")
			color = Color("91db69")
		"miss":
			text = "Falla"
			color = Color(0.7, 0.7, 0.7)
		"dodge":
			text = "Esquiva"
			color = Color(0.7, 0.7, 0.7)
		"absorb":
			text = "Absorbe " + str(amount)
			color = Color("8fd3ff")
		"immune":
			text = "Inmune"
			color = Color(0.8, 0.8, 0.8)
		"xp":
			text = "+%d XP" % amount
			color = Color("a884f3")
		"level":
			text = "¡Nivel %d!" % amount
			color = UiTheme.ACCENT
		_:
			return
	if not _allow(entity_id, kind, amount, world_pos):
		return
	if _spawn(text, color, world_pos, crit) and not group.is_empty():
		var e: Dictionary = _active[-1]
		e["group"] = group
		e["amount"] = amount


## Suma `amount` al número de `group` si sigue recién salido; false si no hay ninguno (se muestra uno nuevo).
func _add_to_group(group: String, kind: String, amount: int) -> bool:
	for e: Dictionary in _active:
		if str(e.get("group", "")) == group and float(e["t"]) < GROUP_WINDOW_SEC:
			e["amount"] = int(e["amount"]) + amount
			(e["label"] as Label).text = ("+%d" if kind == "heal" else "%d") % int(e["amount"])
			return true
	return false


## Límite por entidad y segundo: a partir del 6.º número se acumula y se muestra sumado al cerrar la ventana.
func _allow(entity_id: int, kind: String, amount: int, world_pos: Vector2) -> bool:
	var now := Time.get_ticks_msec()
	var st: Dictionary = _per_entity.get(entity_id, {"windowStart": now, "count": 0, "pending": 0, "pos": world_pos})
	if now - int(st["windowStart"]) >= 1000:
		if int(st["pending"]) > 0:
			_spawn(str(st["pending"]), Color.WHITE, st["pos"])
		st = {"windowStart": now, "count": 0, "pending": 0, "pos": world_pos}
	st["count"] = int(st["count"]) + 1
	st["pos"] = world_pos
	var allowed: bool = int(st["count"]) <= MAX_PER_ENTITY_PER_SEC
	if not allowed and kind == "dmg":
		st["pending"] = int(st["pending"]) + amount
	_per_entity[entity_id] = st
	return allowed


## Saca una etiqueta de la reserva; false si ya hay MAX_VISIBLE o no queda ninguna libre.
func _spawn(text: String, color: Color, world_pos: Vector2, big: bool = false) -> bool:
	if _active.size() >= MAX_VISIBLE:
		return false
	var l: Label = null
	for cand: Label in _pool:
		if not cand.visible:
			l = cand
			break
	if l == null:
		return false
	l.text = text
	l.add_theme_color_override("font_color", color)
	l.add_theme_font_size_override("font_size", UiTheme.FONT_HEADLINE if big else UiTheme.FONT_BODY)
	l.add_theme_constant_override("outline_size", UiTheme.OUTLINE_THICK * 2 if big else UiTheme.OUTLINE_THICK)
	l.modulate.a = 1.0
	l.position = (world_pos + Vector2(-8 + randf_range(-4, 4), -50)).round()  # por encima de la placa de nombre
	l.visible = true
	_active.append({"label": l, "t": 0.0})
	return true


func _process(delta: float) -> void:
	for i: int in range(_active.size() - 1, -1, -1):
		var e := _active[i]
		var t: float = float(e["t"]) + delta
		var l: Label = e["label"]
		if t >= LIFETIME:
			l.visible = false
			_active.remove_at(i)
			continue
		e["t"] = t
		e["y"] = float(e.get("y", l.position.y)) - RISE_PX * delta
		l.position.y = roundf(float(e["y"]))  # en la rejilla de píxeles
		l.modulate.a = 1.0 - t / LIFETIME
