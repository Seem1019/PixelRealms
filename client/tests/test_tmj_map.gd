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
