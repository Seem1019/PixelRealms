class_name WorldBackdrop
extends Control
## Fondo de las pantallas de inicio: un trozo del mapa del mundo horneado con el tileset real (la aldea), con algunos
## vecinos animados, que se desplaza muy despacio de lado a lado. Encima, un velo oscuro para que el panel se lea.
## Si falta el mapa o el tileset, queda el color de fondo.

const MAP := "res://maps/meadow.tmj"
## Zona del mapa que se muestra (px): la aldea con sus casas, la plaza y la cerca.
const REGION := Rect2i(0, 448, 760, 420)
const PAN_SECONDS := 40.0
const FIGURES := [
	["npcs/shopkeeper", Vector2(272, 768), "s"], ["npcs/trainer", Vector2(448, 768), "s"],
	["characters/warrior", Vector2(330, 700), "e"], ["characters/mage", Vector2(520, 640), "w"],
	["monsters/slime", Vector2(720, 610), "w"],
]

var _scene: Node2D


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	clip_contents = true
	var bg := ColorRect.new()
	bg.color = Color("323353")
	bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(bg)
	var map := TmjMap.load_from(MAP)
	var baked := TerrainBaker.bake(map) if map != null else {}
	if not baked.is_empty():
		_scene = Node2D.new()
		add_child(_scene)
		var ground: Image = baked["ground"]
		var above: Image = baked["above"]
		var region := REGION.intersection(Rect2i(Vector2i.ZERO, ground.get_size()))
		var img := ground.get_region(region)
		var ground_tex := Sprite2D.new()
		ground_tex.centered = false
		ground_tex.texture = ImageTexture.create_from_image(img)
		_scene.add_child(ground_tex)
		for f: Array in FIGURES:
			var frames := EntitySprites.frames_for(str(f[0]))
			if frames == null:
				continue
			var spr := AnimatedSprite2D.new()
			spr.sprite_frames = frames
			var facing := EntitySprites.facing(str(f[2]))
			spr.play("idle_" + str(facing[0]))
			spr.flip_h = bool(facing[1])
			spr.centered = false
			var feet := float(EntitySprites.meta(str(f[0]))["feet"])
			spr.position = (f[1] as Vector2) - Vector2(region.position) - Vector2(16, feet)
			_scene.add_child(spr)
		var above_tex := Sprite2D.new()
		above_tex.centered = false
		above_tex.texture = ImageTexture.create_from_image(above.get_region(region))
		_scene.add_child(above_tex)
		var span := maxf(0.0, region.size.x - UiTheme.base_size().x)
		var tween := create_tween().set_loops()
		tween.tween_property(_scene, "position:x", -span, PAN_SECONDS).set_trans(Tween.TRANS_SINE)
		tween.tween_property(_scene, "position:x", 0.0, PAN_SECONDS).set_trans(Tween.TRANS_SINE)
	var veil := ColorRect.new()
	veil.color = Color(UiTheme.OUTLINE, 0.35)
	veil.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	veil.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(veil)


func _process(_delta: float) -> void:
	if _scene != null:
		_scene.position = _scene.position.round()  # en la rejilla de píxeles mientras se desliza
