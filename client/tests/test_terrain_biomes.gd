extends GutTest
## HU-108: el Bosque y la Cripta se hornean con su propio atlas (propiedades de mapa `biome` y `palette`), con la misma
## disposición de piezas que el de la Pradera; los mapas sin propiedad (la Pradera y la Mina) siguen con `terrain.png`.

const FOREST := "res://assets/tiles/terrain_forest.png"
const CRYPT := "res://assets/tiles/terrain_crypt.png"


func test_current_maps_keep_the_meadow_atlas() -> void:
	for id: String in ["meadow", "mine"]:
		var map := TmjMap.load_from("res://maps/%s.tmj" % id)
		assert_not_null(map, "falta client/maps (tools/sync_content.gd)")
		if map == null:
			continue
		assert_eq(map.biome, "", id)
		assert_eq(map.palette, "", id)
		assert_eq(TerrainBaker.atlas_path_for(map), TerrainBaker.ATLAS_PATH, id)


## HU-111: el Bosque (tools/maps/gen_tier2_maps.py) se hornea con su atlas y el cliente lee sus dos zonas y sus puntos seguros.
func test_the_forest_map_uses_the_forest_atlas() -> void:
	var map := TmjMap.load_from("res://maps/forest.tmj")
	assert_not_null(map, "falta client/maps/forest.tmj (tools/sync_content.gd)")
	if map == null:
		return
	assert_eq(map.biome, "forest")
	assert_eq(TerrainBaker.atlas_path_for(map), FOREST)
	var names: Array[String] = []
	for z: Dictionary in map.zones:
		names.append(str(z["name"]))
	assert_true(names.has("Linde del Bosque") and names.has("Pantano"), str(names))
	assert_eq(map.graveyards.size(), 2, "una fogata por zona")


func test_biome_and_palette_are_read_from_the_map_properties() -> void:
	var forest := _map_with({"biome": "forest"})
	assert_eq(forest.biome, "forest")
	assert_eq(TerrainBaker.atlas_path_for(forest), FOREST)
	var crypt := _map_with({"palette": "crypt"})
	assert_eq(crypt.palette, "crypt")
	assert_eq(TerrainBaker.atlas_path_for(crypt), CRYPT)
	assert_eq(TerrainBaker.atlas_path_for(_map_with({"biome": "meadow"})), TerrainBaker.ATLAS_PATH)
	assert_eq(TerrainBaker.atlas_path_for(_map_with({"palette": "mine"})), TerrainBaker.ATLAS_PATH)
	assert_eq(TerrainBaker.atlas_path_for(_map_with({"biome": "forest", "palette": "crypt"})), CRYPT, "manda la paleta")


func test_an_unknown_biome_falls_back_to_the_meadow_atlas() -> void:
	assert_eq(TerrainBaker.atlas_path_for(_map_with({"biome": "desert"})), TerrainBaker.ATLAS_PATH)


func test_forest_and_crypt_atlases_keep_the_meadow_layout() -> void:
	var meadow := TerrainBaker.atlas(TerrainBaker.ATLAS_PATH)
	for path: String in [FOREST, CRYPT]:
		var img := TerrainBaker.atlas(path)
		assert_not_null(img, "falta %s (tools/art/gen_tiles.py)" % path)
		if img == null or meadow == null:
			continue
		assert_eq(img.get_size(), meadow.get_size(), path)
		for row: int in 16:
			for col: int in 16:
				var cell := Rect2i(col * 16, row * 16, 16, 16)
				assert_eq(img.get_region(cell).is_invisible(), meadow.get_region(cell).is_invisible(),
					"%s: pieza (fila %d, columna %d)" % [path.get_file(), row, col])


func test_the_crypt_recolors_the_mine_interior_pieces_and_nothing_else() -> void:
	var mine := TerrainBaker.atlas(TerrainBaker.ATLAS_PATH)
	var forest := TerrainBaker.atlas(FOREST)
	var crypt := TerrainBaker.atlas(CRYPT)
	assert_true(mine != null and forest != null and crypt != null, "faltan atlas")
	if mine == null or forest == null or crypt == null:
		return
	var interior: Array[Vector2i] = [Vector2i(TerrainBaker.ROW_OBJECTS, 8)]
	for col: int in range(8, 16):
		interior.append(Vector2i(TerrainBaker.ROW_DIRT_FLOOR, col))
	for col: int in 16:
		interior.append(Vector2i(TerrainBaker.ROW_DUAL_CAVE, col))
	for row: int in 16:
		for col: int in 16:
			var cell := Rect2i(col * 16, row * 16, 16, 16)
			if interior.has(Vector2i(row, col)):
				assert_true(_same_alpha(crypt.get_region(cell), mine.get_region(cell)), "misma pieza que la Mina (%d, %d)" % [row, col])
			else:
				assert_true(_same_pixels(crypt.get_region(cell), forest.get_region(cell)), "fuera del interior es el Bosque (%d, %d)" % [row, col])
	var floor_px := Vector2i(8 * 16 + 1, TerrainBaker.ROW_DIRT_FLOOR * 16 + 1)
	assert_ne(crypt.get_pixelv(floor_px), mine.get_pixelv(floor_px), "el suelo de la Cripta no es el de la Mina")
	assert_gt(crypt.get_pixelv(floor_px).g, crypt.get_pixelv(floor_px).r, "el suelo de la Cripta es verde")


func test_a_forest_map_is_baked_with_the_forest_atlas_and_cached_apart() -> void:
	var meadow_map := TmjMap.load_from("res://maps/test_small.tmj")
	var forest_map := TmjMap.load_from("res://maps/test_small.tmj")
	forest_map.biome = "forest"
	var meadow: Dictionary = TerrainBaker.bake(meadow_map)
	var forest: Dictionary = TerrainBaker.bake(forest_map)
	assert_false(forest.is_empty(), "falta %s" % FOREST)
	if forest.is_empty():
		return
	assert_true((forest["ground"] as Image).get_data() != (meadow["ground"] as Image).get_data(), "el Bosque no reutiliza el horneado de la Pradera")
	assert_eq((forest["ground"] as Image).get_size(), (meadow["ground"] as Image).get_size())
	assert_true(is_same(TerrainBaker.bake(forest_map), forest), "el segundo horneado sale de la caché")


func _map_with(props: Dictionary) -> TmjMap:
	var properties: Array = []
	for k: String in props:
		properties.append({"name": k, "type": "string", "value": props[k]})
	var root := {"width": 2, "height": 2, "tilewidth": 16, "tileheight": 16, "properties": properties, "tilesets": [],
		"layers": [{"name": "ground", "type": "tilelayer", "width": 2, "height": 2, "data": [1, 1, 1, 1]}]}
	var path := "user://test_terrain_biome.tmj"
	var f := FileAccess.open(path, FileAccess.WRITE)
	f.store_string(JSON.stringify(root))
	f.close()
	return TmjMap.load_from(path)


## Mismos píxeles visibles (el import de Godot rellena el color de los transparentes con el de sus vecinos).
func _same_pixels(a: Image, b: Image) -> bool:
	for y: int in a.get_height():
		for x: int in a.get_width():
			var ca := a.get_pixel(x, y)
			if ca.a != b.get_pixel(x, y).a or (ca.a > 0.0 and ca != b.get_pixel(x, y)):
				return false
	return true


func _same_alpha(a: Image, b: Image) -> bool:
	for y: int in a.get_height():
		for x: int in a.get_width():
			if a.get_pixel(x, y).a != b.get_pixel(x, y).a:
				return false
	return true
