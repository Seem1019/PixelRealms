extends GutTest
## HU-020/HU-022: el cliente construye la misma colisión que el servidor desde maps/test_small.tmj.


func test_collision_matches_server_fixture() -> void:
	var map := TmjMap.load_from("res://maps/test_small.tmj")
	assert_not_null(map, "falta client/maps (tools/sync_content.gd)")
	if map == null:
		return
	assert_eq(map.width, 10)
	assert_true(map.collision.is_solid(3, 3))
	assert_true(map.collision.blocks_sight(3, 3))
	assert_true(map.collision.is_solid(5, 5))
	assert_false(map.collision.blocks_sight(5, 5))
	assert_true(map.collision.is_solid(7, 7), "flip flag")
	assert_true(map.collision.is_solid(1, 8), "capa collision")
	assert_false(map.collision.is_solid(2, 2))
	assert_true(map.collision.is_solid(-1, 0))
	assert_eq(map.zone_at(Vector2(1, 1)).get("name"), "Plaza")
	assert_true(map.zone_at(Vector2(8, 8)).is_empty())


## HU-112: la salida de la Mina al Bosque se dibuja cerrada hasta la Fase 2, y también un portal a un mapa que no viene con el
## cliente. Solo es el dibujo: quien deja pasar es el servidor.
func test_mine_exit_is_drawn_closed_until_its_phase() -> void:
	var map := TmjMap.load_from("res://maps/mine.tmj")
	assert_not_null(map, "falta client/maps (tools/sync_content.gd)")
	if map == null:
		return
	var exit := map.portal_targets.find("forest")
	var back := map.portal_targets.find("meadow")
	assert_true(exit >= 0 and back >= 0, str(map.portal_targets))
	assert_eq(map.portal_min_phases[exit], 2)
	assert_false(map.portal_open(exit, 1))
	assert_true(map.portal_open(exit, 2))
	assert_true(map.portal_open(back, 1), "la vuelta a la Pradera no depende de la fase")
	map.portal_targets[exit] = "tier3"  # la salida de la Cripta a un tier que aún no existe (HU-115)
	assert_false(map.portal_open(exit, 3))


## HU-112: cerrada, la salida lleva un cartel en vez de la escalera; la entrada de la Mina se hornea igual en las dos fases.
func test_the_closed_exit_is_baked_with_a_sign_instead_of_the_stairs() -> void:
	var map := TmjMap.load_from("res://maps/mine.tmj")
	if map == null:
		return
	var closed := TerrainBaker.bake(map, 1)
	var open := TerrainBaker.bake(map, 2)
	assert_false(closed.is_empty() or open.is_empty(), "falta assets/tiles/terrain.png")
	if closed.is_empty() or open.is_empty():
		return
	var exit := Rect2i(map.portals[map.portal_targets.find("forest")])
	var back := Rect2i(map.portals[map.portal_targets.find("meadow")])
	assert_ne((closed["ground"] as Image).get_region(exit).get_data(), (open["ground"] as Image).get_region(exit).get_data(), "cartel o escalera")
	assert_eq((closed["ground"] as Image).get_region(back).get_data(), (open["ground"] as Image).get_region(back).get_data())
