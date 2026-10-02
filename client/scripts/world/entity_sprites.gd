class_name EntitySprites
## Hojas de sprites de personajes, NPC y monstruos (tools/art/gen_chars.py): filas = direcciones s, n, e (w = e espejado),
## columnas = idle0, idle1, walk0..walk3; tamaño del cuadro y fila de los pies en el .json junto al PNG.
## Construye y guarda en caché un SpriteFrames por hoja con las animaciones idle_<dir> y walk_<dir> (skill pixel-art-assets).

const DIRS := ["s", "n", "e"]
const IDLE_FPS := 3.0
const WALK_FPS := 8.0
## NPC del mapa (templateId del EntitySpawn) → hoja.
const NPC_SPRITES := {"vendor": "npcs/shopkeeper", "class_change": "npcs/trainer"}

static var _frames: Dictionary = {}  # ref → SpriteFrames (o null si no hay hoja)
static var _meta: Dictionary = {}  # ref → {frame: int, feet: int}


## Ruta del sprite (sin extensión, relativa a assets/sprites/) para una entidad: la clase del jugador, el monstruo de
## monsters.json o el NPC. Vacío si no hay ninguno (se usa PlaceholderSprite).
static func ref_for(kind: String, template_id: String, class_id: String) -> String:
	if template_id.is_empty() and class_id.is_empty():
		return ""
	match kind:
		"player":
			return str(Content.character_class(class_id if not class_id.is_empty() else template_id).get("sprite", ""))
		"monster":
			return str(Content.monster(template_id).get("sprite", ""))
		"npc":
			return str(NPC_SPRITES.get(template_id, ""))
	return ""


static func sheet(ref: String) -> Texture2D:
	var path := "res://assets/sprites/%s.png" % ref
	return load(path) as Texture2D if not ref.is_empty() and ResourceLoader.exists(path) else null


## Tamaño del cuadro y fila de los pies (por defecto 32 y 28).
static func meta(ref: String) -> Dictionary:
	if _meta.has(ref):
		return _meta[ref]
	var m := {"frame": 32, "feet": 28}
	var path := "res://assets/sprites/%s.json" % ref
	if FileAccess.file_exists(path):
		var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
		if parsed is Dictionary:
			var size: Array = (parsed as Dictionary).get("frameSize", [32, 32])
			m["frame"] = int(size[0])
			m["feet"] = int((parsed as Dictionary).get("feetY", int(size[1]) - 4))
	_meta[ref] = m
	return m


static func frames_for(ref: String) -> SpriteFrames:
	if _frames.has(ref):
		return _frames[ref]
	var tex := sheet(ref)
	if tex == null:
		_frames[ref] = null
		return null
	var size := int(meta(ref)["frame"])
	var sf := SpriteFrames.new()
	sf.remove_animation("default")
	for row: int in DIRS.size():
		var d: String = DIRS[row]
		for anim: String in ["idle", "walk"]:
			var name := "%s_%s" % [anim, d]
			sf.add_animation(name)
			sf.set_animation_speed(name, IDLE_FPS if anim == "idle" else WALK_FPS)
			sf.set_animation_loop(name, true)
			var cols := [0, 1] if anim == "idle" else [2, 3, 4, 5]
			for c: int in cols:
				var at := AtlasTexture.new()
				at.atlas = tex
				at.region = Rect2(c * size, row * size, size, size)
				sf.add_frame(name, at)
	_frames[ref] = sf
	return sf


## Primer cuadro mirando al sur (cuerpo entero) o solo la cara (16×16, para los retratos de los marcos).
static func portrait(ref: String, head_only: bool) -> Texture2D:
	var tex := sheet(ref)
	if tex == null:
		return null
	var size := int(meta(ref)["frame"])
	var at := AtlasTexture.new()
	at.atlas = tex
	if head_only:
		var k := size / 32
		at.region = Rect2(size / 2 - 8 * k, size - 28 * k + 1, 16 * k, 16 * k) if k <= 1 else Rect2(size / 2 - 8, size - 52, 16, 16)
	else:
		at.region = Rect2(0, 0, size, size)
	return at


## Dirección del protocolo ("s", "n", "e", "w" o "down/up/right/left") → fila de la hoja y si se espeja.
static func facing(dir: String) -> Array:
	match dir:
		"n", "up":
			return ["n", false]
		"e", "right":
			return ["e", false]
		"w", "left":
			return ["e", true]
	return ["s", false]
