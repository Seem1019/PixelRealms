extends GutTest
## HU-083 CA1: palancas y puertas de la Mina en el cliente (lectura del .tmj, colisión de predicción, estado y clic).


func after_each() -> void:
	GameState.reset()


func test_mine_has_two_levers_and_a_closed_door_in_the_prediction_grid() -> void:
	var map := TmjMap.load_from("res://maps/mine.tmj")
	assert_eq(map.levers.size(), 3)  # dos en la Sala 2 y la de dentro de la sala del jefe
	assert_eq(map.doors.size(), 1)
	assert_eq(str(map.doors[0]["id"]), "mine_boss_door")
	assert_true(map.collision.is_solid(56, 36), "la puerta empieza cerrada, como en el servidor")
	assert_true(map.collision.blocks_sight(56, 36))
	map.set_door_open("mine_boss_door", true)
	assert_false(map.collision.is_solid(56, 36))
	assert_false(map.collision.blocks_sight(56, 36))
	assert_true(map.collision.is_solid(55, 36), "la roca de al lado sigue siendo roca")
	map.set_door_open("mine_boss_door", false)
	assert_true(map.collision.is_solid(56, 36))


func test_map_objects_message_updates_the_state_and_change_map_clears_it() -> void:
	watch_signals(GameState)
	Net._dispatch(JSON.stringify({"t": "MapObjects", "d": {"objects": [{"id": "mine_lever_west", "state": "on"}, {"id": "mine_boss_door", "state": "closed"}]}}))
	assert_signal_emitted(GameState, "map_objects_changed")
	assert_eq(GameState.map_objects, {"mine_lever_west": "on", "mine_boss_door": "closed"})
	Net._dispatch(JSON.stringify({"t": "MapObjects", "d": {"objects": [{"id": "mine_boss_door", "state": "open"}]}}))
	assert_eq(GameState.map_objects["mine_boss_door"], "open")
	assert_eq(GameState.map_objects["mine_lever_west"], "on", "un cambio no borra lo demás")
	GameState._on_change_map({"mapId": "meadow", "x": 0, "y": 0})
	assert_true(GameState.map_objects.is_empty())


## HU-083: los cambios avisan (palancas que faltan, puerta que se abre o se cierra); el estado al entrar en el mapa, no.
func test_lever_and_door_changes_are_announced_but_not_the_initial_state() -> void:
	watch_signals(GameState)
	Net._dispatch(JSON.stringify({"t": "MapObjects", "d": {"objects": [{"id": "mine_lever_west", "state": "off"}, {"id": "mine_boss_door", "state": "closed"}]}}))
	assert_signal_not_emitted(GameState, "map_object_toggled", "al entrar no hay nada que anunciar")
	Net._dispatch(JSON.stringify({"t": "MapObjects", "d": {"objects": [{"id": "mine_lever_west", "state": "on"}]}}))
	assert_signal_emitted_with_parameters(GameState, "map_object_toggled", ["mine_lever_west", "on"])
	Net._dispatch(JSON.stringify({"t": "MapObjects", "d": {"objects": [{"id": "mine_boss_door", "state": "open"}]}}))
	assert_signal_emitted_with_parameters(GameState, "map_object_toggled", ["mine_boss_door", "open"])


func test_a_click_on_the_lever_picks_it() -> void:
	var layer := MapObjectsLayer.new()
	add_child_autofree(layer)
	layer.setup(TmjMap.load_from("res://maps/mine.tmj"))
	var west := Vector2(51.5, 32.5) * 16.0
	assert_eq(layer.lever_at(west + Vector2(3, -4)), "mine_lever_west")
	assert_eq(layer.lever_at(west + Vector2(40, 0)), "")
	assert_not_null(layer._lever_tex, "palanca con sprite (tools/art/gen_objects.py)")
	assert_not_null(layer._door_tex, "puerta con sprite")
