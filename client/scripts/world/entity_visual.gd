class_name EntityVisual
extends Node2D
## Dibujo de una entidad con los pies en el origen: sombra elíptica, sprite animado de 32×32 (o PlaceholderSprite si falta
## la hoja) y la placa de nombre (nivel + nombre + barra de vida, en ese orden fijo) anclada encima del sprite. La placa
## la puede desplazar el mundo (NameplateLayout) para que no se pise con otras ni se salga de la pantalla.

const PLATE_GAP := 2
const FLASH_MS := 160

var sprite: AnimatedSprite2D
var placeholder: PlaceholderSprite
var shadow: Sprite2D
var plate: Nameplate
var sprite_ref: String = ""
## Alto visible del sprite sobre los pies (para anclar la placa).
var body_height: float = 24.0
var _facing: String = "s"
var _flip: bool = false
var _moving: bool = false
var _flash_until_ms: int = -1
var _flash_color: Color = Color.WHITE
var dead: bool = false


func _ready() -> void:
	shadow = Sprite2D.new()
	shadow.texture = UiTheme.texture("res://assets/sprites/shadow.png")
	shadow.position = Vector2(0, 0)
	shadow.z_index = -1
	add_child(shadow)
	sprite = AnimatedSprite2D.new()
	sprite.centered = false
	add_child(sprite)
	plate = Nameplate.new()
	plate.z_index = 40
	plate.z_as_relative = false
	add_child(plate)


## Elige la hoja (ruta relativa a assets/sprites, sin extensión). Vacía o inexistente → rectángulo con la inicial.
func set_sprite(ref: String, fallback_color: Color, display_name: String) -> void:
	sprite_ref = ref
	var frames := EntitySprites.frames_for(ref)
	if frames == null:
		sprite.visible = false
		if placeholder == null:
			placeholder = PlaceholderSprite.new()
			add_child(placeholder)
		placeholder.color = fallback_color
		placeholder.initial = display_name.left(1)
		placeholder.visible = true
		body_height = 18.0
	else:
		if placeholder != null:
			placeholder.visible = false
		sprite.visible = true
		sprite.sprite_frames = frames
		var m := EntitySprites.meta(ref)
		var size := float(m["frame"])
		sprite.position = Vector2(-size / 2.0, -float(m["feet"]))
		body_height = float(m["feet"]) - (4.0 if size <= 32.0 else 8.0)
		var big := size > 32.0
		shadow.texture = UiTheme.texture("res://assets/sprites/shadow_big.png" if big else "res://assets/sprites/shadow.png")
		_play()
	plate.position = Vector2(0, -body_height - PLATE_GAP)


## Dirección del protocolo y si se está moviendo (walk) o quieto (idle).
func set_motion(dir: String, moving: bool) -> void:
	var f := EntitySprites.facing(dir)
	if f[0] != _facing or f[1] != _flip or moving != _moving:
		_facing = f[0]
		_flip = f[1]
		_moving = moving
		_play()


func _play() -> void:
	if sprite == null or sprite.sprite_frames == null:
		return
	var anim := "%s_%s" % ["walk" if _moving and not dead else "idle", _facing]
	if sprite.sprite_frames.has_animation(anim) and sprite.animation != anim:
		sprite.play(anim)
	elif not sprite.is_playing():
		sprite.play(anim)
	sprite.flip_h = _flip
	if dead:
		sprite.pause()


func set_dead(value: bool) -> void:
	dead = value
	modulate = Color(0.55, 0.5, 0.55, 0.7) if dead else Color.WHITE
	_play()


## Destello del cuerpo (CombatEvents: daño en blanco, cura en verde), para ver a quién alcanza un área.
func flash(color: Color) -> void:
	_flash_color = color
	_flash_until_ms = Time.get_ticks_msec() + FLASH_MS


func is_flashing() -> bool:
	return Time.get_ticks_msec() < _flash_until_ms


func _process(_delta: float) -> void:
	var tint := _flash_color if is_flashing() else Color.WHITE
	if sprite != null:
		sprite.modulate = tint
	if placeholder != null:
		placeholder.modulate = tint


## Rectángulo de la placa en coordenadas del mundo con su posición anclada (sin desplazar).
func plate_anchor_rect() -> Rect2:
	var s := plate.plate_size()
	return Rect2(global_position + Vector2(-s.x / 2.0, -body_height - PLATE_GAP - s.y), s)


## Desplazamiento que aplica el mundo para separar placas (en px del mundo, ya redondeado).
func set_plate_offset(offset: Vector2) -> void:
	plate.position = (Vector2(0, -body_height - PLATE_GAP) + offset).round()
