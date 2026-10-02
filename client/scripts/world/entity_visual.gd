class_name EntityVisual
extends Node2D
## Dibujo de una entidad con los pies en el origen: sombra elíptica, sprite animado de 32×32 (o PlaceholderSprite si falta
## la hoja) y la placa de nombre (nivel + nombre + barra de vida, en ese orden fijo) anclada encima del sprite. La placa
## la puede desplazar el mundo (NameplateLayout) para que no se pise con otras ni se salga de la pantalla.
## Animación del cuerpo (HU-090): prioridad muerte > golpe recibido > ataque/casteo > caminar > idle. El ataque es de un
## disparo y moverse no lo corta; durante el ataque y el casteo mira al objetivo. Si la hoja no trae una animación se
## usa idle. La muerte se queda en su último cuadro; las hojas sin `death` vuelven al cuerpo gris de antes.

const PLATE_GAP := 2
const FLASH_MS := 160
## Retroceso de 1 px al recibir un golpe (en la dirección contraria a quien pega).
const RECOIL_MS := 120
## Ataque o golpe sin animación en la hoja: cuánto dura el estado igualmente (para que mire al objetivo).
const DEFAULT_ONE_SHOT_MS := 300
## Ralentizado (HU-098): tinte azul del cuerpo mientras dure.
const SLOW_TINT := Color(0.62, 0.78, 1.0)

var sprite: AnimatedSprite2D
var placeholder: PlaceholderSprite
var shadow: Sprite2D
var plate: Nameplate
## Aturdido, inmovilizado y escudo sobre la entidad (HU-098).
var status: AuraIndicator
var slowed: bool = false
var sprite_ref: String = ""
## Alto visible del sprite sobre los pies (para anclar la placa).
var body_height: float = 24.0
var _facing: String = "s"
var _flip: bool = false
var _moving: bool = false
var _flash_until_ms: int = -1
var _flash_color: Color = Color.WHITE
var dead: bool = false
var _attack_until_ms: int = -1
var _hurt_until_ms: int = -1
var _recoil_until_ms: int = -1
var _recoil: Vector2 = Vector2.ZERO
var _casting: bool = false
## Dirección forzada mientras ataca o castea ("" = la del movimiento).
var _face_dir: String = ""
var _sprite_base: Vector2 = Vector2.ZERO
## Animación base elegida ("idle", "walk", "attack"…); la que suena es `<base>_<fila>` o idle si falta.
var current_base: String = "idle"


func _ready() -> void:
	shadow = Sprite2D.new()
	shadow.texture = UiTheme.texture("res://assets/sprites/shadow.png")
	shadow.position = Vector2(0, 0)
	shadow.z_index = -1
	add_child(shadow)
	sprite = AnimatedSprite2D.new()
	sprite.centered = false
	add_child(sprite)
	status = AuraIndicator.new()
	add_child(status)
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
		# Hoja HD (pixelScale 3): se dibuja a 1/3 y todo lo demás va en píxeles lógicos.
		var s := float(m["scale"])
		sprite.scale = Vector2.ONE / s
		var size := float(m["frame"]) / s
		var feet := float(m["feet"]) / s
		_sprite_base = Vector2(-size / 2.0, -feet)
		sprite.position = _sprite_base
		# El cuadro HD mide 40 lógicos para que quepan los tajos, pero el cuerpo es el de un personaje de 32.
		var big := s <= 1.0 and size > 32.0
		body_height = feet - (8.0 if big else 4.0) - (size - 32.0 if s > 1.0 else 0.0)
		shadow.texture = UiTheme.texture("res://assets/sprites/shadow_big.png" if big else "res://assets/sprites/shadow.png")
		_play()
	plate.position = Vector2(0, -body_height - PLATE_GAP)
	status.body_height = body_height


## Auras de la entidad (GameState.auras_of): indicadores de control, tinte de ralentizado y mini-íconos de la placa.
func set_auras(list: Array) -> void:
	var summary := AuraStyle.summarize(list, Content.aura)
	status.apply(summary)
	slowed = bool(summary["slowed"])
	plate.aura_icons = summary["icons"]


## Dirección del protocolo y si se está moviendo (walk) o quieto (idle).
func set_motion(dir: String, moving: bool) -> void:
	var f := EntitySprites.facing(dir)
	if f[0] != _facing or f[1] != _flip or moving != _moving:
		_facing = f[0]
		_flip = f[1]
		_moving = moving
		_play()


## Prioridad de HU-090 CA3 (pura, para los tests).
static func pick_base(is_dead: bool, hurt: bool, attacking: bool, casting: bool, moving: bool) -> String:
	if is_dead:
		return "death"
	if hurt:
		return "hurt"
	if attacking:
		return "attack"
	if casting:
		return "cast"
	return "walk" if moving else "idle"


## Animación que existe en la hoja para `base` mirando a `row`; si falta, idle (HU-090 CA2).
static func resolve_anim(frames: SpriteFrames, base: String, row: String) -> String:
	var anim := "%s_%s" % [base, row]
	if frames != null and frames.has_animation(anim):
		return anim
	return "idle_%s" % row


func _play() -> void:
	if sprite == null or sprite.sprite_frames == null:
		return
	var now := Time.get_ticks_msec()
	current_base = pick_base(dead, now < _hurt_until_ms, now < _attack_until_ms, _casting, _moving)
	var row := _facing
	var flip := _flip
	if not _face_dir.is_empty() and current_base in ["attack", "cast", "hurt"]:
		var f := EntitySprites.facing(_face_dir)
		row = f[0]
		flip = f[1]
	var anim := resolve_anim(sprite.sprite_frames, current_base, row)
	if dead and not anim.begins_with("death_"):
		# Hoja sin muerte: cuerpo gris y quieto como antes.
		modulate = Color(0.55, 0.5, 0.55, 0.7)
		if sprite.animation != anim:
			sprite.play(anim)
		sprite.pause()
	else:
		modulate = Color.WHITE
		if sprite.animation != anim:
			sprite.play(anim)
		elif not sprite.is_playing() and sprite.sprite_frames.get_animation_loop(anim):
			sprite.play(anim)
	sprite.flip_h = flip


## `instant`: ya estaba muerto al aparecer (sin reproducir la caída, directo al último cuadro).
func set_dead(value: bool, instant: bool = false) -> void:
	if value == dead:
		return
	dead = value
	_attack_until_ms = -1
	_hurt_until_ms = -1
	_casting = false
	_face_dir = ""
	_play()
	if dead and instant and sprite != null and sprite.sprite_frames != null and sprite.animation.begins_with("death_"):
		sprite.frame = sprite.sprite_frames.get_frame_count(sprite.animation) - 1
		sprite.pause()


## Ataque de un disparo (CombatEvents con este src, o el final de un casteo) mirando hacia `toward` (vector del mundo).
func play_attack(toward: Vector2 = Vector2.ZERO) -> void:
	if dead:
		return
	if toward != Vector2.ZERO:
		_face_dir = EntitySprites.dir_from_vector(toward)
	var ms := EntitySprites.anim_ms(sprite_ref, "attack")
	_attack_until_ms = Time.get_ticks_msec() + (ms if ms > 0 else DEFAULT_ONE_SHOT_MS)
	if sprite != null and sprite.sprite_frames != null:
		sprite.stop()  # vuelve a empezar aunque ya estuviera atacando
	_play()


## Casteo con tiempo (entre CastStarted y CastEnded): animación en bucle mirando hacia `toward`.
func begin_cast(toward: Vector2 = Vector2.ZERO) -> void:
	if dead:
		return
	_casting = true
	if toward != Vector2.ZERO:
		_face_dir = EntitySprites.dir_from_vector(toward)
	_play()


func end_cast() -> void:
	_casting = false
	_play()


## Golpe recibido (daño, no fallos ni esquivas): destello blanco, animación de golpe y 1 px de retroceso.
func hurt(from: Vector2 = Vector2.ZERO) -> void:
	flash(Color(3, 3, 3))
	if dead:
		return
	var now := Time.get_ticks_msec()
	var ms := EntitySprites.anim_ms(sprite_ref, "hurt")
	_hurt_until_ms = now + (ms if ms > 0 else 0)
	_recoil_until_ms = now + RECOIL_MS
	var away := -from
	_recoil = Vector2.ZERO if away == Vector2.ZERO else (Vector2(signf(away.x), 0) if absf(away.x) >= absf(away.y) else Vector2(0, signf(away.y)))
	if sprite != null and sprite.sprite_frames != null:
		sprite.stop()
	_play()


func is_attacking() -> bool:
	return Time.get_ticks_msec() < _attack_until_ms


func is_casting() -> bool:
	return _casting


## Destello del cuerpo (CombatEvents: daño en blanco, cura en verde), para ver a quién alcanza un área.
func flash(color: Color) -> void:
	_flash_color = color
	_flash_until_ms = Time.get_ticks_msec() + FLASH_MS


func is_flashing() -> bool:
	return Time.get_ticks_msec() < _flash_until_ms


func _process(_delta: float) -> void:
	var now := Time.get_ticks_msec()
	var tint := _flash_color if is_flashing() else (SLOW_TINT if slowed and not dead else Color.WHITE)
	if sprite != null:
		sprite.modulate = tint
		sprite.position = _sprite_base + (_recoil if now < _recoil_until_ms and not dead else Vector2.ZERO)
		if sprite.sprite_frames != null:
			var expected := pick_base(dead, now < _hurt_until_ms, now < _attack_until_ms, _casting, _moving)
			if expected != current_base:
				if expected in ["idle", "walk"]:
					_face_dir = ""
				_play()
	if placeholder != null:
		placeholder.modulate = tint


## Rectángulo de la placa en coordenadas del mundo con su posición anclada (sin desplazar).
func plate_anchor_rect() -> Rect2:
	var s := plate.plate_size()
	return Rect2(global_position + Vector2(-s.x / 2.0, -body_height - PLATE_GAP - s.y), s)


## Desplazamiento que aplica el mundo para separar placas (en px del mundo, ya redondeado).
func set_plate_offset(offset: Vector2) -> void:
	plate.position = (Vector2(0, -body_height - PLATE_GAP) + offset).round()
