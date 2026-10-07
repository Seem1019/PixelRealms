class_name TmjMap
extends RefCounted
## Lee un mapa de Tiled (.tmj, capas CSV) copiado a res://maps/ por tools/sync_content.gd. Construye la CollisionGrid del
## cliente (mismo algoritmo que TiledMapLoader.cs) y guarda los GIDs de las capas de tiles para dibujarlas.

const GID_MASK := 0x1FFFFFFF
const TILE_LAYERS := ["ground", "detail", "walls", "above"]

var map_id: String = ""
var display_name: String = ""
## HU-108: con qué atlas se hornea (TerrainBaker.atlas_path_for). `biome` para exteriores (meadow, forest) y `palette`
## para cuevas (mine, crypt); vacías, las de la Pradera y la Mina.
var biome: String = ""
var palette: String = ""
var width: int = 0
var height: int = 0
var tile_size: int = 16
var collision: CollisionGrid = CollisionGrid.new()
var layers: Dictionary = {}  # nombre → PackedInt32Array de GIDs (ya enmascarados)
var zones: Array[Dictionary] = []
## Solo para dibujar: cementerios (fogata) y portales (entrada a la cueva), en píxeles.
var graveyards: Array[Vector2] = []
var portals: Array[Rect2] = []
## HU-083: palancas {id, door, pos (px)} y puertas {id, rect (px)}. Las puertas empiezan cerradas en `collision`.
var levers: Array[Dictionary] = []
var doors: Array[Dictionary] = []
var _door_base: Dictionary = {}  # id de puerta → [[x, y, solid, blocksSight]] de sus casillas sin la puerta
var _tile_props: Dictionary = {}  # gid → {solid, blocksSight}


static func load_from(path: String) -> TmjMap:
	var map := TmjMap.new()
	if not map._parse(path):
		return null
	return map


func gid_at(layer_name: String, x: int, y: int) -> int:
	if not layers.has(layer_name) or x < 0 or y < 0 or x >= width or y >= height:
		return 0
	var data: PackedInt32Array = layers[layer_name]
	return data[y * width + x]


func zone_at(pos: Vector2) -> Dictionary:
	for z: Dictionary in zones:
		var rect: Rect2 = z["rect"]
		if rect.has_point(pos):
			return z
	return {}


func _parse(path: String) -> bool:
	if not FileAccess.file_exists(path):
		push_warning("Mapa no encontrado: %s" % path)
		return false
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
	if not (parsed is Dictionary):
		push_error("Mapa inválido: %s" % path)
		return false
	var root: Dictionary = parsed
	width = int(root.get("width", 0))
	height = int(root.get("height", 0))
	tile_size = int(root.get("tilewidth", 16))
	var props := _props(root)
	map_id = str(props.get("mapId", path.get_file().get_basename()))
	display_name = str(props.get("displayName", map_id))
	biome = str(props.get("biome", ""))
	palette = str(props.get("palette", ""))
	collision = CollisionGrid.new(width, height)
	_load_tile_props(root, path.get_base_dir())
	for layer: Variant in root.get("layers", []):
		var l: Dictionary = layer
		var name := str(l.get("name", ""))
		if str(l.get("type", "")) == "tilelayer":
			var raw: Array = l.get("data", [])
			var data := PackedInt32Array()
			data.resize(raw.size())
			for i: int in raw.size():
				var gid := int(raw[i]) & GID_MASK
				data[i] = gid
				if (name == "walls" or name == "collision") and gid != 0 and _tile_props.has(gid):
					var tp: Dictionary = _tile_props[gid]
					if tp.get("solid", false):
						collision.set_solid(i % width, i / width)
					if tp.get("blocksSight", false):
						collision.set_blocks_sight(i % width, i / width)
			layers[name] = data
		elif str(l.get("type", "")) == "objectgroup" and name == "graveyards":
			for o: Variant in l.get("objects", []):
				graveyards.append(Vector2(float((o as Dictionary).get("x", 0)), float((o as Dictionary).get("y", 0))))
		elif str(l.get("type", "")) == "objectgroup" and name == "portals":
			for o: Variant in l.get("objects", []):
				var po: Dictionary = o
				portals.append(Rect2(float(po.get("x", 0)), float(po.get("y", 0)), maxf(float(po.get("width", 0)), 1.0), maxf(float(po.get("height", 0)), 1.0)))
		elif str(l.get("type", "")) == "objectgroup" and name == "levers":
			for o: Variant in l.get("objects", []):
				var lo: Dictionary = o
				var lp := _props(lo)
				levers.append({"id": str(lp.get("leverId", lo.get("name", ""))), "door": str(lp.get("doorId", "")), "pos": Vector2(float(lo.get("x", 0)), float(lo.get("y", 0))),
					"opens_alone": bool(lp.get("opensAlone", false))})
		elif str(l.get("type", "")) == "objectgroup" and name == "doors":
			for o: Variant in l.get("objects", []):
				var dobj: Dictionary = o
				var dp := _props(dobj)
				doors.append({"id": str(dp.get("doorId", dobj.get("name", ""))), "rect": Rect2(float(dobj.get("x", 0)), float(dobj.get("y", 0)), float(dobj.get("width", 0)), float(dobj.get("height", 0)))})
		elif str(l.get("type", "")) == "objectgroup" and name == "zones":
			for o: Variant in l.get("objects", []):
				var od: Dictionary = o
				var op := _props(od)
				zones.append({
					"name": str(op.get("name", od.get("name", ""))),
					"safe": bool(op.get("safe", false)),
					"rect": Rect2(float(od.get("x", 0)) / tile_size, float(od.get("y", 0)) / tile_size, float(od.get("width", 0)) / tile_size, float(od.get("height", 0)) / tile_size),
				})
	# Las puertas empiezan cerradas, como en el servidor (MapInstance): se guarda lo que había debajo para abrirlas.
	for d: Dictionary in doors:
		var cells: Array = []
		var rect: Rect2 = d["rect"]
		for y: int in range(floori(rect.position.y / tile_size), ceili(rect.end.y / tile_size)):
			for x: int in range(floori(rect.position.x / tile_size), ceili(rect.end.x / tile_size)):
				cells.append([x, y, collision.is_solid(x, y), collision.blocks_sight(x, y)])
		_door_base[str(d["id"])] = cells
		set_door_open(str(d["id"]), false)
	return true


## HU-083: abre (devuelve las casillas a lo que dice el mapa) o cierra (sólidas y opacas) una puerta en la colisión de predicción.
func set_door_open(door_id: String, open: bool) -> void:
	for cell: Variant in _door_base.get(door_id, []):
		var c: Array = cell
		collision.set_solid(int(c[0]), int(c[1]), bool(c[2]) if open else true)
		collision.set_blocks_sight(int(c[0]), int(c[1]), bool(c[3]) if open else true)


func _load_tile_props(root: Dictionary, base_dir: String) -> void:
	for ts: Variant in root.get("tilesets", []):
		var t: Dictionary = ts
		var first_gid := int(t.get("firstgid", 1))
		var def: Dictionary = t
		if t.has("source"):
			var src := base_dir.path_join(str(t["source"]))
			var ext: Variant = JSON.parse_string(FileAccess.get_file_as_string(src)) if FileAccess.file_exists(src) else null
			if ext is Dictionary:
				def = ext
		for tile: Variant in def.get("tiles", []):
			var td: Dictionary = tile
			var p := _props(td)
			_tile_props[first_gid + int(td.get("id", 0))] = {"solid": bool(p.get("solid", false)), "blocksSight": bool(p.get("blocksSight", false))}


static func _props(el: Dictionary) -> Dictionary:
	var d := {}
	for p: Variant in el.get("properties", []):
		var pd: Dictionary = p
		d[str(pd.get("name", ""))] = pd.get("value")
	return d
