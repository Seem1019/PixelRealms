class_name RemoteEntity
extends Node2D
## Entidad remota (jugador, monstruo, NPC) dibujada a partir de EntitySpawn + Snapshot (HU-023, HU-024).
## Posición interpolada 100 ms atrás con InterpolationBuffer; sprite de 32×32 de su clase, monstruo o NPC (EntityVisual)
## con sombra y placa de nombre: nivel (monstruos), nombre (blanco; azul para el grupo, HU-061) y barra de vida debajo
## (jugadores y monstruos vivos; los NPC no combaten).

const PLAYER_COLOR := Color("4d9be6")
const MONSTER_COLOR := Color("e83b3b")
const NPC_COLOR := Color("1ebc73")
## Destello al recibir un golpe o una cura (blanco / verde), para ver a quién alcanza un área.
const FLASH_MS := EntityVisual.FLASH_MS
## Distancia en px que cuenta como "se está moviendo" entre dos cuadros (anima caminar).
const MOVE_EPSILON := 0.05

var entity_id: int = -1
var kind: String = "player"
var template_id: String = ""
var display_name: String = ""
var level: int = 1
var class_id: String = ""
var hp_pct: int = 100
var dir: String = "down"
var anim: String = "idle"

var selected: bool = false:
	set(value):
		if value != selected:
			selected = value
			queue_redraw()
## Mientras se apunta un área: esta entidad entraría (contorno blanco del cuadro del cuerpo). Solo visual.
var area_hint: bool = false:
	set(value):
		if value != area_hint:
			area_hint = value
			if _hint != null:
				_hint.queue_redraw()
var hostile: bool = false
var cast_started_ms: int = -1
var cast_duration_ms: int = 0

var _buffer: InterpolationBuffer = InterpolationBuffer.new()
var visual: EntityVisual
var health_bar: HealthBar
var _last_pos: Vector2 = Vector2.INF
## Capa por encima del sprite para el contorno de "te alcanzaría el área".
var _hint: Node2D
var _still_frames: int = 0
## ¿La hemos visto viva? Si aparece ya muerta (cadáver en la AOI) no se reproduce la caída (HU-090).
var _seen_alive: bool = false


func _ready() -> void:
	y_sort_enabled = true
	visual = EntityVisual.new()
	add_child(visual)
	health_bar = visual.plate.health_bar
	_hint = Node2D.new()
	_hint.z_index = 1
	_hint.draw.connect(_draw_hint)
	add_child(_hint)
	_apply_identity()
	_refresh_health_bar()
	refresh_level_color()


## Rellena desde un EntitySpawn (docs/protocol.md).
func setup(d: Dictionary) -> void:
	entity_id = int(d.get("id", -1))
	kind = str(d.get("kind", "player"))
	template_id = str(d.get("templateId", ""))
	display_name = str(d.get("name", ""))
	level = int(d.get("level", 1))
	class_id = str(d.get("classId", ""))
	hp_pct = int(d.get("hpPct", 100))
	dir = str(d.get("dir", "down"))
	var pos := Vector2(float(d.get("x", 0)), float(d.get("y", 0)))
	position = pos
	_buffer.clear()
	_buffer.push(float(Time.get_ticks_msec()), pos)
	hostile = kind == "monster"
	_seen_alive = hp_pct > 0
	_apply_identity()
	_refresh_health_bar()
	refresh_level_color()


func _apply_identity() -> void:
	if visual == null or visual.plate == null:
		return
	visual.set_sprite(EntitySprites.ref_for(kind, template_id, class_id), _color_for_kind(), display_name)
	visual.plate.display_name = display_name
	visual.set_motion(dir, false)


## Aplica el estado de un Snapshot (campos frecuentes).
func apply_state(e: Dictionary, now_ms: float) -> void:
	_buffer.push(now_ms, Vector2(float(e.get("x", position.x)), float(e.get("y", position.y))))
	dir = str(e.get("dir", dir))
	hp_pct = int(e.get("hpPct", hp_pct))
	anim = str(e.get("anim", anim))
	_refresh_health_bar()


func _process(_delta: float) -> void:
	if _buffer.size() > 0:
		position = _buffer.sample(float(Time.get_ticks_msec()))
	var moving := _last_pos != Vector2.INF and position.distance_to(_last_pos) > MOVE_EPSILON
	_still_frames = 0 if moving else _still_frames + 1
	_last_pos = position
	if visual != null:
		visual.set_dead(anim == "dead", not _seen_alive)
		_seen_alive = _seen_alive or anim != "dead"
		visual.set_motion(dir, anim == "walk" or _still_frames < 4)
		if cast_started_ms >= 0:
			visual.plate.cast_frac = clampf(float(Time.get_ticks_msec() - cast_started_ms) / float(maxi(1, cast_duration_ms)), 0.0, 1.0)
	if selected:
		queue_redraw()


## Destella el cuerpo FLASH_MS (CombatEvents: daño en blanco, cura en verde).
func flash(color: Color) -> void:
	if visual != null:
		visual.flash(color)


func is_flashing() -> bool:
	return visual != null and visual.is_flashing()


## Cuadro del cuerpo respecto a los pies (rules.combat.body*): el mismo con el que el servidor resuelve las áreas y el
## que se resalta al apuntar.
func body_rect() -> Rect2:
	return BodyShape.rect_px(Vector2.ZERO)


## HU-030 CA1: anillo pixelado bajo los pies (rojo hostil, verde aliado) cuando está seleccionada.
func _draw() -> void:
	if selected:
		PixelDraw.ellipse_ring(self, Vector2(0, 1), 9, 4, Color("e83b3b") if hostile else Color("1ebc73"), UiTheme.OUTLINE)


## Al apuntar un área: contorno blanco del cuadro del cuerpo que alcanzaría, por encima del sprite.
func _draw_hint() -> void:
	if area_hint:
		_hint.draw_rect(hint_rect(), Color(1, 1, 1, 0.9), false, 1.0)


## Rectángulo resaltado al apuntar: el cuadro del cuerpo de las reglas, 1 px por fuera (coordenadas locales).
func hint_rect() -> Rect2:
	var r := body_rect()
	return Rect2(r.position - Vector2(0.5, 0.5), r.size + Vector2.ONE)


func _refresh_health_bar() -> void:
	if health_bar == null:
		return
	health_bar.pct = hp_pct
	var show := kind != "npc" and anim != "dead" and hp_pct > 0
	if health_bar.visible != show:
		health_bar.visible = show
		visual.plate.refresh_layout()


## HU-031 CA4: nivel coloreado según la diferencia con el mío (gris ≤ −5, verde −3..−4, amarillo ±2, naranja +3..+4, rojo ≥ +5).
func refresh_level_color() -> void:
	if visual == null:
		return
	if kind != "monster":
		visual.plate.level_text = ""
		return
	visual.plate.level_text = "%d" % level
	visual.plate.level_color = level_color(level - GameState.level)


static func level_color(diff: int) -> Color:
	if diff <= -5:
		return Color(0.6, 0.6, 0.6)
	if diff <= -3:
		return Color(0.3, 0.9, 0.3)
	if diff <= 2:
		return Color(1, 0.9, 0.2)
	if diff <= 4:
		return Color(1, 0.55, 0.1)
	return Color(1, 0.2, 0.2)


## Mini barra de casteo bajo la vida de la entidad (CastStarted/CastEnded ajenos).
func begin_cast(duration_ms: int) -> void:
	cast_started_ms = Time.get_ticks_msec()
	cast_duration_ms = duration_ms
	if visual != null:
		visual.plate.cast_color = UiTheme.ACCENT
		visual.plate.cast_frac = 0.0 if duration_ms > 0 else -1.0
		visual.plate.refresh_layout()


func end_cast(result: String) -> void:
	cast_started_ms = -1
	if visual != null:
		if result == "interrupted":
			visual.plate.cast_color = UiTheme.ERROR
		visual.plate.cast_frac = -1.0
		visual.plate.refresh_layout()


func set_name_color(color: Color) -> void:
	if visual != null:
		visual.plate.name_color = color


## Cara del sprite para el retrato del marco de objetivo.
func portrait() -> Texture2D:
	return EntitySprites.portrait(visual.sprite_ref, true) if visual != null else null


func _color_for_kind() -> Color:
	match kind:
		"monster":
			return MONSTER_COLOR
		"npc":
			return NPC_COLOR
		_:
			return PLAYER_COLOR
