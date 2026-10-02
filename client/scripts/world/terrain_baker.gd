class_name TerrainBaker
## Hornea un mapa (TmjMap) en dos imágenes con el tileset `assets/tiles/terrain.png` (lo genera tools/art/gen_tiles.py):
## `ground` (suelo, decoración y objetos sólidos con su sombra; debajo de las entidades) y `above` (copas de árbol y aleros,
## encima de las entidades). Solo cambia lo visual: lee los mismos GIDs del .tmj que la colisión (placeholder.tsj).
## Los bordes entre terrenos se resuelven con "dual-grid": cada casilla de dibujo cae en la esquina de 4 casillas del mapa y
## elige una de 16 piezas según cuáles de las 4 son del terreno, así los caminos, el agua y las rocas tienen bordes irregulares.

const ATLAS_PATH := "res://assets/tiles/terrain.png"
const T := 16

## GIDs de placeholder.tsj (firstgid 1).
const GID_GRASS := 1
const GID_DIRT := 2
const GID_ROAD := 3
const GID_WALL := 4
const GID_BUSH := 5
const GID_WATER := 6
const GID_ROCK := 7
const GID_FLOOR := 8

## Filas del atlas (deben coincidir con tools/art/gen_tiles.py).
const ROW_GRASS := 0
const ROW_DIRT_FLOOR := 1
const ROW_DUAL_DIRT := 2
const ROW_DUAL_ROAD := 3
const ROW_DUAL_WATER := 4
const ROW_DUAL_ROCK := 5
const ROW_DUAL_FOREST := 6
const ROW_DUAL_BUSH := 7
const ROW_DUAL_CANOPY := 8
const ROW_DUAL_CAVE := 9
const ROW_FENCE := 10
const ROW_ROOF := 11
const ROW_WALL := 12
const ROW_OBJECTS := 13
const ROW_EXTRA := 14
## Columna de la primera copa de 32×32 en las filas 14-15 (2 claras y 2 oscuras) y del pozo de 32×32.
const CROWN_COL := 4
const WELL_COL := 12
## Agua en zona segura de hasta tantas casillas: es un pozo (el de la plaza), no un estanque.
const WELL_MAX_CELLS := 4

const SHADOW := Color(0.12, 0.07, 0.14, 0.38)
const CANOPY_SHADOW := Color(0.1, 0.12, 0.08, 0.3)

static var _cache: Dictionary = {}  # clave del mapa → {ground: Image, above: Image}
static var _atlas: Image


static func atlas() -> Image:
	if _atlas == null and ResourceLoader.exists(ATLAS_PATH):
		var tex := load(ATLAS_PATH) as Texture2D
		if tex != null:
			_atlas = tex.get_image()
			if _atlas.is_compressed():
				_atlas.decompress()
			_atlas.convert(Image.FORMAT_RGBA8)
	return _atlas


## {ground: Image, above: Image} o vacío si falta el tileset (el llamador usa el respaldo de colores).
static func bake(map: TmjMap) -> Dictionary:
	if map == null or atlas() == null:
		return {}
	var key := "%s:%dx%d" % [map.map_id, map.width, map.height]
	if _cache.has(key):
		return _cache[key]
	var b := _Bake.new(map, atlas())
	var result := b.run()
	_cache[key] = result
	return result


class _Bake:
	var map: TmjMap
	var src: Image
	var w: int
	var h: int
	var ground: Image
	var solid: Image
	var above: Image
	var canopy: Image
	var g: PackedInt32Array
	var d: PackedInt32Array
	var wl: PackedInt32Array
	var ab: PackedInt32Array
	var cave: bool = false
	## Casillas de roca que tocan el borde del mapa: se dibujan como bosque en exteriores.
	var forest_cells: Dictionary = {}
	## Bloques de muro fuera de la zona segura: peñascos (se dibujan con la roca).
	var boulder_cells: Dictionary = {}
	var tone := FastNoiseLite.new()
	## Agua pequeña dentro de una zona segura: el pozo de la plaza (casillas → true) y el centro de cada pozo (px).
	var wells: Dictionary = {}
	var well_centres: Array[Vector2i] = []

	func _init(m: TmjMap, atlas_img: Image) -> void:
		map = m
		src = atlas_img
		w = m.width
		h = m.height
		g = _layer("ground")
		d = _layer("detail")
		wl = _layer("walls")
		ab = _layer("above")

	func _layer(name: String) -> PackedInt32Array:
		if map.layers.has(name):
			return map.layers[name]
		var empty := PackedInt32Array()
		empty.resize(map.width * map.height)
		return empty

	func run() -> Dictionary:
		var px := Vector2i(w * T, h * T)
		ground = Image.create_empty(px.x, px.y, false, Image.FORMAT_RGBA8)
		solid = Image.create_empty(px.x, px.y, false, Image.FORMAT_RGBA8)
		above = Image.create_empty(px.x, px.y, false, Image.FORMAT_RGBA8)
		canopy = Image.create_empty(px.x, px.y, false, Image.FORMAT_RGBA8)
		var floor_count := 0
		for v: int in g:
			if v == GID_FLOOR:
				floor_count += 1
		cave = floor_count * 2 > g.size()
		tone.seed = 7
		tone.frequency = 0.09
		_base()
		_dual(ground, ROW_DUAL_DIRT, func(x: int, y: int) -> bool: return _at(g, x, y) == GID_DIRT, ROW_DIRT_FLOOR)
		_dual(ground, ROW_DUAL_ROAD, func(x: int, y: int) -> bool: return _at(g, x, y) == GID_ROAD, ROW_EXTRA)
		_find_wells()
		_dual(ground, ROW_DUAL_WATER, func(x: int, y: int) -> bool: return _at(wl, x, y) == GID_WATER and not wells.has(clampi(y, 0, h - 1) * w + clampi(x, 0, w - 1)), -1)
		_decorate()
		_structures()
		if cave:
			_dual(solid, ROW_DUAL_CAVE, func(x: int, y: int) -> bool: return _at(wl, x, y) == GID_ROCK, -1)
		else:
			_find_forest()
			_dual(solid, ROW_DUAL_FOREST, func(x: int, y: int) -> bool: return forest_cells.has(clampi(y, 0, h - 1) * w + clampi(x, 0, w - 1)), -1)
			_crowns(solid, func(x: int, y: int) -> bool: return forest_cells.has(y * w + x), 2, 2)
			var rocky := func(x: int, y: int) -> bool:
				var i := clampi(y, 0, h - 1) * w + clampi(x, 0, w - 1)
				return (wl[i] == GID_ROCK and not forest_cells.has(i)) or boulder_cells.has(i)
			_dual(solid, ROW_DUAL_ROCK, rocky, -1)
		_dual(solid, ROW_DUAL_BUSH, func(x: int, y: int) -> bool: return _at(wl, x, y) == GID_BUSH, -1)
		_objects()
		_composite(ground, solid, SHADOW, Vector2i(2, 3))
		_canopies()
		return {"ground": ground, "above": above}

	# --- Ayudas ------------------------------------------------------------------------------------------------------

	## Valor de una capa con las coordenadas sujetas al mapa (los bordes continúan el terreno).
	func _at(layer: PackedInt32Array, x: int, y: int) -> int:
		return layer[clampi(y, 0, h - 1) * w + clampi(x, 0, w - 1)]

	func _raw(layer: PackedInt32Array, x: int, y: int) -> int:
		if x < 0 or y < 0 or x >= w or y >= h:
			return -1
		return layer[y * w + x]

	func _cell(col: int, row: int) -> Rect2i:
		return Rect2i(col * T, row * T, T, T)

	static func _hash(x: int, y: int, salt: int) -> int:
		var v := (x * 73856093) ^ (y * 19349663) ^ (salt * 83492791)
		v = (v ^ (v >> 13)) * 1274126177
		return absi(v ^ (v >> 16))

	func _composite(dst: Image, layer: Image, shadow: Color, offset: Vector2i) -> void:
		var tint := Image.create_empty(layer.get_width(), layer.get_height(), false, Image.FORMAT_RGBA8)
		tint.fill(shadow)
		dst.blend_rect_mask(tint, layer, Rect2i(Vector2i.ZERO, layer.get_size()), offset)
		dst.blend_rect(layer, Rect2i(Vector2i.ZERO, layer.get_size()), Vector2i.ZERO)

	# --- Suelo -------------------------------------------------------------------------------------------------------

	func _base() -> void:
		for y: int in h:
			for x: int in w:
				var v := g[y * w + x]
				var hv := _hash(x, y, 1)
				var col: int
				var row: int
				if v == GID_FLOOR:
					row = ROW_DIRT_FLOOR
					col = 8 + hv % 4
				else:
					row = ROW_GRASS
					var n := tone.get_noise_2d(x, y)
					col = (6 + hv % 2) if n < -0.35 else ((4 + hv % 2) if n > 0.4 else hv % 4)
				ground.blit_rect(src, _cell(col, row), Vector2i(x * T, y * T))

	## Terreno dual-grid: la casilla de dibujo (i, j) cubre la esquina común de las casillas (i-1..i, j-1..j) del mapa.
	## `full_row` ≥ 0: las piezas totalmente interiores usan una de las 4 variantes de esa fila (menos repetición).
	func _dual(dst: Image, row: int, is_terrain: Callable, full_row: int) -> void:
		for j: int in h + 1:
			for i: int in w + 1:
				var cfg := 0
				if is_terrain.call(i - 1, j - 1):
					cfg |= 1
				if is_terrain.call(i, j - 1):
					cfg |= 2
				if is_terrain.call(i - 1, j):
					cfg |= 4
				if is_terrain.call(i, j):
					cfg |= 8
				if cfg == 0:
					continue
				var rect := _cell(cfg, row)
				if cfg == 15 and full_row >= 0:
					rect = _cell(_hash(i, j, row) % 4, full_row)
				dst.blend_rect(src, rect, Vector2i(i * T - T / 2, j * T - T / 2))

	func _decorate() -> void:
		for y: int in h:
			for x: int in w:
				if wl[y * w + x] != 0:
					continue
				var hv := _hash(x, y, 7)
				var dv := d[y * w + x]
				var pos := Vector2i(x * T, y * T)
				if dv == GID_DIRT and not cave:
					ground.blend_rect(src, _cell(8 + [2, 3, 5, 7][hv % 4], ROW_GRASS), pos)
					continue
				var v := g[y * w + x]
				if not _surrounded_by(x, y, v):
					continue
				var roll := hv % 100
				if v == GID_GRASS:
					if roll < 16:
						ground.blend_rect(src, _cell(8 + [0, 1, 0, 1, 6][hv % 5], ROW_GRASS), pos)
					elif roll < 21:
						ground.blend_rect(src, _cell(8 + [2, 3][hv % 2], ROW_GRASS), pos)
					elif roll < 23:
						ground.blend_rect(src, _cell(8 + [4, 5][hv % 2], ROW_GRASS), pos)
				elif v == GID_DIRT:
					if roll < 9:
						ground.blend_rect(src, _cell(4 + hv % 4, ROW_DIRT_FLOOR), pos)
				elif v == GID_FLOOR:
					if roll < 8:
						ground.blend_rect(src, _cell(12 + [0, 0, 1, 2, 3][hv % 5], ROW_DIRT_FLOOR), pos)

	func _surrounded_by(x: int, y: int, v: int) -> bool:
		for o: Vector2i in [Vector2i(-1, 0), Vector2i(1, 0), Vector2i(0, -1), Vector2i(0, 1)]:
			if _at(g, x + o.x, y + o.y) != v or _at(wl, x + o.x, y + o.y) != 0:
				return false
		return true

	# --- Sólidos ------------------------------------------------------------------------------------------------------

	## Las rocas conectadas con el borde del mapa son el límite del mundo: en exteriores se dibujan como bosque.
	func _find_forest() -> void:
		var stack: Array[int] = []
		for x: int in w:
			stack.append(x)
			stack.append((h - 1) * w + x)
		for y: int in h:
			stack.append(y * w)
			stack.append(y * w + w - 1)
		while not stack.is_empty():
			var i: int = stack.pop_back()
			if forest_cells.has(i) or wl[i] != GID_ROCK:
				continue
			forest_cells[i] = true
			var x := i % w
			var y := i / w
			if x > 0:
				stack.append(i - 1)
			if x < w - 1:
				stack.append(i + 1)
			if y > 0:
				stack.append(i - w)
			if y < h - 1:
				stack.append(i + w)

	## Muros (GID 4): componentes de una casilla de grosor → cerca; bloques → casa (zona segura) o ruina.
	func _structures() -> void:
		var seen := {}
		for start: int in wl.size():
			if wl[start] != GID_WALL or seen.has(start):
				continue
			var cells: Array[int] = []
			var stack: Array[int] = [start]
			while not stack.is_empty():
				var i: int = stack.pop_back()
				if seen.has(i) or wl[i] != GID_WALL:
					continue
				seen[i] = true
				cells.append(i)
				var x := i % w
				var y := i / w
				if x > 0:
					stack.append(i - 1)
				if x < w - 1:
					stack.append(i + 1)
				if y > 0:
					stack.append(i - w)
				if y < h - 1:
					stack.append(i + w)
			if _is_thin(cells):
				_fence(cells)
			else:
				_building(cells)

	func _is_wall(x: int, y: int) -> bool:
		return _raw(wl, x, y) == GID_WALL

	## Ninguna casilla tiene vecinos muro a la vez a la derecha y abajo con la diagonal llena (no hay bloques 2×2).
	func _is_thin(cells: Array[int]) -> bool:
		for i: int in cells:
			var x := i % w
			var y := i / w
			if _is_wall(x + 1, y) and _is_wall(x, y + 1) and _is_wall(x + 1, y + 1):
				return false
		return true

	func _fence(cells: Array[int]) -> void:
		for i: int in cells:
			var x := i % w
			var y := i / w
			var cfg := int(_is_wall(x, y - 1)) | int(_is_wall(x + 1, y)) << 1 | int(_is_wall(x, y + 1)) << 2 | int(_is_wall(x - 1, y)) << 3
			solid.blend_rect(src, _cell(cfg, ROW_FENCE), Vector2i(x * T, y * T))

	func _building(cells: Array[int]) -> void:
		var x0 := w
		var y0 := h
		var x1 := 0
		var y1 := 0
		for i: int in cells:
			x0 = mini(x0, i % w)
			x1 = maxi(x1, i % w)
			y0 = mini(y0, i / w)
			y1 = maxi(y1, i / w)
		var centre := Vector2((x0 + x1 + 1) / 2.0, (y0 + y1 + 1) / 2.0)
		var house := bool(map.zone_at(centre).get("safe", false))
		var has_eave := _raw(ab, (x0 + x1) / 2, y0 - 1) == GID_BUSH
		var door_x := (x0 + x1) / 2
		for i: int in cells:
			var x := i % w
			var y := i / w
			var pos := Vector2i(x * T, y * T)
			var col_kind := 0 if x == x0 else (2 if x == x1 else 1)
			if not house:
				boulder_cells[i] = true
				continue
			if y == y1:
				var kind := 0 if x == x0 else (2 if x == x1 else (3 if x == door_x else (4 if (x - x0) % 2 == 1 else 1)))
				solid.blend_rect(src, _cell(kind, ROW_WALL), pos)
			else:
				var row_kind := 2 if y == y1 - 1 else (0 if y == y0 and not has_eave else 1)
				solid.blend_rect(src, _cell(row_kind * 3 + col_kind, ROW_ROOF), pos)
		# Alero sobre el tejado (capa above del .tmj): se dibuja encima de las entidades. Sobre un peñasco es una copa.
		if not house:
			return
		for x: int in range(x0, x1 + 1):
			if _raw(ab, x, y0 - 1) == GID_BUSH:
				var col_kind := 0 if x == x0 else (2 if x == x1 else 1)
				above.blend_rect(src, _cell(9 + col_kind, ROW_ROOF), Vector2i(x * T, (y0 - 1) * T))
				ab[(y0 - 1) * w + x] = -GID_BUSH  # ya dibujado: no es copa de árbol

	func _find_wells() -> void:
		var seen := {}
		for start: int in wl.size():
			if wl[start] != GID_WATER or seen.has(start):
				continue
			var cells: Array[int] = []
			var stack: Array[int] = [start]
			while not stack.is_empty():
				var i: int = stack.pop_back()
				if seen.has(i) or wl[i] != GID_WATER:
					continue
				seen[i] = true
				cells.append(i)
				for o: Vector2i in [Vector2i(-1, 0), Vector2i(1, 0), Vector2i(0, -1), Vector2i(0, 1)]:
					var x := i % w + o.x
					var y := i / w + o.y
					if x >= 0 and y >= 0 and x < w and y < h:
						stack.append(y * w + x)
			if cells.size() > WELL_MAX_CELLS:
				continue
			var centre := Vector2.ZERO
			for i: int in cells:
				centre += Vector2(i % w + 0.5, i / w + 0.5)
			centre /= cells.size()
			if not bool(map.zone_at(centre).get("safe", false)):
				continue
			for i: int in cells:
				wells[i] = true
			well_centres.append(Vector2i(roundi(centre.x * T), roundi(centre.y * T)))

	## Cementerios (fogata), portales (escalera) y pozos del .tmj.
	func _objects() -> void:
		for c: Vector2i in well_centres:
			solid.blend_rect(src, Rect2i(WELL_COL * T, ROW_EXTRA * T, 2 * T, 2 * T), c - Vector2i(T, T + 4))
		for p: Vector2 in map.graveyards:
			solid.blend_rect(src, _cell(7, ROW_OBJECTS), Vector2i(floori(p.x) - T / 2, floori(p.y) - T / 2))
		for r: Rect2 in map.portals:
			solid.blend_rect(src, _cell(8, ROW_OBJECTS), Vector2i(floori(r.get_center().x) - T / 2, floori(r.get_center().y) - T / 2))

	# --- Copas de árbol (capa above sin muro debajo) -------------------------------------------------------------

	func _canopies() -> void:
		var is_canopy := func(x: int, y: int) -> bool: return _raw(ab, x, y) == GID_BUSH
		_dual(canopy, ROW_DUAL_CANOPY, is_canopy, -1)
		_crowns(canopy, is_canopy, 0, 2)
		# Tronco asomando bajo cada copa: en la casilla inferior central de cada columna de copas de 1+ de ancho.
		for y: int in h:
			for x: int in w:
				if is_canopy.call(x, y) and not is_canopy.call(x, y + 1) and _hash(x, y, 9) % 3 == 0:
					if is_canopy.call(x - 1, y) or is_canopy.call(x + 1, y) or not is_canopy.call(x, y - 1):
						ground.blend_rect(src, _cell(0, ROW_OBJECTS), Vector2i(x * T, y * T + T / 2))
		var tint := Image.create_empty(canopy.get_width(), canopy.get_height(), false, Image.FORMAT_RGBA8)
		tint.fill(CANOPY_SHADOW)
		ground.blend_rect_mask(tint, canopy, Rect2i(Vector2i.ZERO, canopy.get_size()), Vector2i(3, 6))
		above.blend_rect(canopy, Rect2i(Vector2i.ZERO, canopy.get_size()), Vector2i.ZERO)


	## Copas redondas de 32×32 repartidas al tresbolillo sobre un área (bosque o arboleda), de arriba abajo para que las de
	## delante tapen a las de detrás. `first`: primera de las copas del atlas; `count`: cuántas variantes usar.
	func _crowns(dst: Image, inside: Callable, first: int, count: int) -> void:
		for y: int in h:
			for x: int in w:
				if (x + (y / 2) % 2) % 2 != 0 or y % 2 != 0:
					continue
				if not (inside.call(x, y) and inside.call(x + 1, y) and inside.call(x, y + 1)):
					continue
				var hv := _hash(x, y, 31)
				var col := CROWN_COL + (first + hv % count) * 2
				var jitter := Vector2i(hv % 5 - 2, (hv / 5) % 5 - 2)
				dst.blend_rect(src, Rect2i(col * T, ROW_EXTRA * T, 2 * T, 2 * T), Vector2i(x * T, y * T) + jitter)
