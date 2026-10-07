extends GutTest
## HU-102 CA3/CA4: el cono y la línea se apuntan desde el jugador hacia el cursor, el punto que se envía no pasa del alcance
## (solo da la dirección), la vista previa marca a quien alcanzaría el servidor y la marca de un casteo ajeno sale de
## `CastStarted.origin` con su forma.

const WORLD := preload("res://scenes/world/world.tscn")


func _dispatch(t: String, d: Dictionary) -> void:
	Net._dispatch(JSON.stringify({"t": t, "d": d}))


func _world() -> Node2D:
	Settings._config.set_value("net", "server_url", "http://127.0.0.1:1")
	GameState.pending_ticket = "t"
	GameState.pending_character_id = "c"
	var w := WORLD.instantiate() as Node2D
	add_child_autofree(w)
	await get_tree().process_frame
	Net.disconnect_from_server()
	_dispatch("Welcome", {
		"selfId": 1, "tick": 10, "tickRate": 20, "snapshotRate": 10, "mapId": "meadow",
		"self": {"x": 300.0, "y": 800.0, "level": 10, "xp": 0, "xpNext": 100, "hp": 80, "maxHp": 100, "res": 40, "maxRes": 100, "resource": "rage", "classId": "warrior", "name": "Ana"},
		"inventory": [], "equipment": [], "hotbar": [], "knownSpells": ["warrior_cleave"], "rulesHash": "x"})
	for i: int in 3:
		await get_tree().process_frame
	return w


func test_shape_hits_for_a_cone_orders_by_distance_and_respects_walls() -> void:
	var grid := CollisionGrid.new(40, 40)
	var cone := {"shape": "cone", "origin": Vector2(10, 10) * 16.0, "dir": Vector2.RIGHT, "radius_px": 2.5 * 16.0, "angle_deg": 90.0}
	var candidates: Array[Dictionary] = [
		{"id": 1, "feet_px": Vector2(12, 10) * 16.0}, {"id": 2, "feet_px": Vector2(8, 10) * 16.0}, {"id": 3, "feet_px": Vector2(11, 10.2) * 16.0}]
	assert_eq(BodyShape.shape_hits(cone, 5, candidates, grid), [3, 1] as Array[int], "delante y más cercano primero; el 2 está detrás")
	grid.set_blocks_sight(11, 10)
	assert_eq(BodyShape.shape_hits(cone, 5, candidates, grid), [3] as Array[int], "el muro tapa al 1 (el 3 está en la casilla del muro)")


func test_aiming_a_cone_starts_at_the_player_and_the_sent_point_stays_in_range() -> void:
	var w := await _world()
	var cleave := Content.spell("warrior_cleave")
	var player: Node2D = w.get("_player")
	w.call("_start_aiming", cleave)
	var reticle: AoeReticle = w.get("_reticle")
	assert_eq(reticle.aim_shape, "cone")
	var far := player.position + Vector2(200, 0)
	w.call("_aim_directional", far)
	assert_eq(reticle.aim_origin, player.position)
	assert_almost_eq(reticle.aim_dir.x, 1.0, 0.001)
	assert_true(reticle.aim_in_range, "el cono no se sale de alcance: solo da la dirección")
	var sent: Vector2 = w.call("_directional_aim_point", cleave, far)
	var prediction: Variant = w.get("prediction")
	assert_almost_eq(sent.distance_to(prediction.position), float(cleave["range"]) * 16.0, 0.01, "acercado al alcance")
	w.call("_stop_aiming")


func test_a_remote_line_cast_draws_a_line_mark_from_its_origin() -> void:
	var w := await _world()
	var reticle: AoeReticle = w.get("_reticle")
	_dispatch("EntitySpawn", {"id": 8, "kind": "player", "templateId": "priest", "name": "Bob", "x": 320.0, "y": 800.0, "dir": "e", "level": 10, "classId": "priest", "hpPct": 100, "flags": 0})
	_dispatch("CastStarted", {"casterId": 8, "spellId": "priest_path_of_light", "targetPos": {"x": 400.0, "y": 800.0}, "origin": {"x": 320.0, "y": 800.0}, "durationMs": 500})
	var marks: Dictionary = reticle.get("_marks")
	assert_true(marks.has(8))
	var area: Dictionary = (marks[8] as Dictionary)["area"]
	assert_eq(str(area["shape"]), "line")
	assert_eq(area["origin"], Vector2(320, 800))
	assert_almost_eq((area["dir"] as Vector2).x, 1.0, 0.001)
	assert_eq(AoeReticle.shape_points(area).size(), 4)
	_dispatch("CastEnded", {"casterId": 8, "spellId": "priest_path_of_light", "result": "done"})
	assert_false((reticle.get("_marks") as Dictionary).has(8))
