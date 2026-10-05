class_name MapObjectsLayer
extends Node2D
## HU-083: palancas y puertas del mapa (capas `levers`/`doors` del .tmj) con el estado que manda el servidor en `MapObjects`.
## Sprites de `tools/art/gen_objects.py` (cuadros de 16×16: sin activar | activada, cerrada | abierta; la puerta repite su
## cuadro en cada casilla). Si faltan las hojas, un dibujo sencillo: bloque de madera y poste con el mango a un lado.

## Distancia en píxeles del mundo a la que un clic cuenta como "sobre la palanca".
const LEVER_PICK_RADIUS_PX := 12.0
const LEVER_SHEET := "res://assets/sprites/objects/lever.png"
const DOOR_SHEET := "res://assets/sprites/objects/door.png"
const FRAME := Vector2(16, 16)
## Pie del poste dentro del cuadro de la palanca: va sobre el punto del .tmj.
const LEVER_FOOT := Vector2(8, 12)

var _lever_tex: Texture2D = UiTheme.texture(LEVER_SHEET)
var _door_tex: Texture2D = UiTheme.texture(DOOR_SHEET)

var _map: TmjMap
## id → estado ("on"/"off", "open"/"closed"); lo que falta cuenta como sin activar / cerrada.
var _states: Dictionary = {}


func setup(map: TmjMap) -> void:
	_map = map
	queue_redraw()


func set_states(states: Dictionary) -> void:
	_states = states
	queue_redraw()


## Id de la palanca bajo `world_pos` (píxeles del mundo) o "" si no hay ninguna.
func lever_at(world_pos: Vector2) -> String:
	if _map == null:
		return ""
	for l: Dictionary in _map.levers:
		if (l["pos"] as Vector2).distance_to(world_pos) <= LEVER_PICK_RADIUS_PX:
			return str(l["id"])
	return ""


func _draw() -> void:
	if _map == null:
		return
	for d: Dictionary in _map.doors:
		var rect: Rect2 = d["rect"]
		var open := str(_states.get(d["id"], "closed")) == "open"
		if _door_tex != null:
			var frame := Rect2(Vector2(FRAME.x if open else 0.0, 0), FRAME)
			for y: int in int(rect.size.y / FRAME.y):
				for x: int in int(rect.size.x / FRAME.x):
					draw_texture_rect_region(_door_tex, Rect2(rect.position + Vector2(x, y) * FRAME, FRAME), frame)
		elif open:
			draw_rect(rect, Color(0.45, 0.3, 0.15, 0.35), false, 1.0)
		else:
			draw_rect(rect, Color(0.35, 0.22, 0.1), true)
			draw_rect(rect, Color(0.15, 0.09, 0.04), false, 1.0)
	for l: Dictionary in _map.levers:
		var p: Vector2 = l["pos"]
		var on := str(_states.get(l["id"], "off")) == "on"
		if _lever_tex != null:
			draw_texture_rect_region(_lever_tex, Rect2((p - LEVER_FOOT).round(), FRAME), Rect2(Vector2(FRAME.x if on else 0.0, 0), FRAME))
			continue
		draw_rect(Rect2(p + Vector2(-3, -2), Vector2(6, 4)), Color(0.3, 0.3, 0.32), true)
		var tip := p + (Vector2(4, -7) if on else Vector2(-4, -7))
		draw_line(p, tip, Color(0.55, 0.38, 0.2), 2.0)
		draw_circle(tip, 2.0, Color(0.4, 0.8, 0.3) if on else Color(0.8, 0.3, 0.25))
