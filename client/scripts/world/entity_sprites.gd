class_name EntitySprites
## Hojas de sprites de personajes, NPC y monstruos (tools/art/gen_chars.py): filas = direcciones s, n, e (w = e espejado),
## columnas según la tabla `anims` del .json junto al PNG ({nombre: {column, frames, fps, loop}}: idle, walk, attack, cast,
## hurt, death; HU-090) más el tamaño del cuadro y la fila de los pies. Una hoja sin `anims` (formato anterior) solo trae
## idle0, idle1, walk0..walk3. Construye y guarda en caché un SpriteFrames por hoja con `<anim>_<dir>` (skill pixel-art-assets).
## Hojas HD (HU-099, tools/art/import_heroes.py): `pixelScale` > 1 = píxeles de la hoja por píxel lógico; quien la dibuja
## la escala a 1/pixelScale, así mide lo mismo en el mundo pero conserva el detalle de la ventana (×3).

const DIRS := ["s", "n", "e"]
## Hojas sin tabla `anims`: idle en las columnas 0-1 y walk en 2-5.
const LEGACY_ANIMS := {
	"idle": {"column": 0, "frames": 2, "fps": 3, "loop": true},
	"walk": {"column": 2, "frames": 4, "fps": 8, "loop": true},
}
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


## Tamaño del cuadro y fila de los pies en píxeles de la hoja (por defecto 32 y 28) y su escala (`pixelScale`, 1 por defecto).
static func meta(ref: String) -> Dictionary:
	if _meta.has(ref):
		return _meta[ref]
	var m := {"frame": 32, "feet": 28, "scale": 1, "anims": LEGACY_ANIMS}
	var path := "res://assets/sprites/%s.json" % ref
	if FileAccess.file_exists(path):
		var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
		if parsed is Dictionary:
			var size: Array = (parsed as Dictionary).get("frameSize", [32, 32])
			m["frame"] = int(size[0])
			m["feet"] = int((parsed as Dictionary).get("feetY", int(size[1]) - 4))
			m["scale"] = maxi(1, int((parsed as Dictionary).get("pixelScale", 1)))
			var anims: Variant = (parsed as Dictionary).get("anims")
			if anims is Dictionary and not (anims as Dictionary).is_empty():
				m["anims"] = anims
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
	var anims: Dictionary = meta(ref)["anims"]
	var columns := tex.get_width() / size
	for row: int in DIRS.size():
		var d: String = DIRS[row]
		for anim: String in anims.keys():
			var info: Dictionary = anims[anim]
			var first := int(info.get("column", 0))
			var count := int(info.get("frames", 1))
			if first + count > columns:
				continue  # la hoja no llega a esas columnas: EntityVisual cae a idle
			var name := "%s_%s" % [anim, d]
			sf.add_animation(name)
			sf.set_animation_speed(name, float(info.get("fps", 8)))
			sf.set_animation_loop(name, bool(info.get("loop", true)))
			for c: int in range(first, first + count):
				var at := AtlasTexture.new()
				at.atlas = tex
				at.region = Rect2(c * size, row * size, size, size)
				sf.add_frame(name, at)
	_frames[ref] = sf
	return sf


## Píxeles de la hoja por píxel lógico (1 en las hojas normales, 3 en las HD).
static func scale_of(ref: String) -> int:
	return int(meta(ref)["scale"])


## Primer cuadro mirando al sur: el cuerpo (32×32 lógicos alrededor de los pies) o solo la cara (16×16, retratos de los
## marcos). En una hoja HD el recorte mide ×pixelScale: quien lo muestra fija su tamaño lógico (32 o 16) y gana detalle.
static func portrait(ref: String, head_only: bool) -> Texture2D:
	var tex := sheet(ref)
	if tex == null:
		return null
	var size := int(meta(ref)["frame"])
	var feet := int(meta(ref)["feet"])
	var s := scale_of(ref)
	var at := AtlasTexture.new()
	at.atlas = tex
	if head_only:
		var k := size / 32
		if s > 1:
			at.region = Rect2(size / 2 - 8 * s, feet - 27 * s, 16 * s, 16 * s)
		else:
			at.region = Rect2(size / 2 - 8 * k, size - 28 * k + 1, 16 * k, 16 * k) if k <= 1 else Rect2(size / 2 - 8, size - 52, 16, 16)
	elif s > 1:
		at.region = Rect2(size / 2 - 16 * s, feet - 28 * s, 32 * s, 32 * s)
	else:
		at.region = Rect2(0, 0, size, size)
	return at


## Duración en ms de una animación de la hoja (frames / fps); 0 si la hoja no la tiene.
static func anim_ms(ref: String, anim: String) -> int:
	var info: Dictionary = (meta(ref)["anims"] as Dictionary).get(anim, {})
	if info.is_empty():
		return 0
	return roundi(1000.0 * int(info.get("frames", 1)) / maxf(1.0, float(info.get("fps", 8))))


## Vector (del mundo) → dirección del protocolo de 4 vías: el eje dominante manda.
static func dir_from_vector(v: Vector2) -> String:
	if absf(v.x) >= absf(v.y):
		return "e" if v.x >= 0.0 else "w"
	return "s" if v.y >= 0.0 else "n"


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
