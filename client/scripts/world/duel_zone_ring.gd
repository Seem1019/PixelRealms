class_name DuelZoneRing
extends Node2D
## Línea de la zona del duelo (HU-101): solo la ven los dos duelistas, desde la cuenta atrás hasta el final. Naranja como el
## nombre del rival; roja y parpadeando mientras uno mismo está fuera (el servidor da `zoneGraceSec` para volver).

const ZONE := Color(1, 0.6, 0.2)
const OUTSIDE := Color("ea4f36")

var radius_px: float = 0.0
var outside: bool = false


func _ready() -> void:
	z_index = -3  # como el círculo de alcance: bajo los cuerpos y sobre el suelo
	visible = false
	GameState.duel_zone_changed.connect(refresh)


func refresh() -> void:
	var zone := GameState.duel_zone
	visible = not zone.is_empty()
	outside = visible and GameState.duel_outside_until_ms >= 0
	if visible:
		position = Vector2(float(zone.get("x", 0.0)), float(zone.get("y", 0.0)))
		radius_px = float(zone.get("r", 0.0))
	queue_redraw()


func _process(_delta: float) -> void:
	if visible and outside:
		queue_redraw()  # parpadeo


func color() -> Color:
	if not outside:
		return ZONE
	return Color(OUTSIDE, 0.55 + 0.45 * absf(sin(Time.get_ticks_msec() / 160.0)))


func _draw() -> void:
	if radius_px <= 0.0:
		return
	var c := color()
	var segments := maxi(32, int(TAU * radius_px / 4.0))
	# Sombra de 3 px para leerse sobre la hierba y la roca, y la línea de 1 px encima.
	draw_arc(Vector2.ZERO, radius_px, 0.0, TAU, segments, Color(c, c.a * 0.25), 3.0)
	draw_arc(Vector2.ZERO, radius_px, 0.0, TAU, segments, c, 1.0)
