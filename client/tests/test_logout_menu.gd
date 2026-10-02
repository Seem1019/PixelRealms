extends GutTest
## HU-015 en el cliente: Esc abre el menú solo cuando no queda nada que cerrar, con el menú abierto la barra y el movimiento
## no actúan, "Volver a selección" pide Logout y espera el LoggedOut, y en combate se avisa "No puedes salir en combate".

const WORLD := preload("res://scenes/world/world.tscn")

var _sent: Array[Dictionary] = []


func _world() -> Node2D:
	Settings._config.set_value("net", "server_url", "http://127.0.0.1:1")
	GameState.pending_ticket = "t"
	GameState.pending_character_id = "c"
	var w := WORLD.instantiate() as Node2D
	add_child_autofree(w)
	await get_tree().process_frame
	Net.disconnect_from_server()
	Net._dispatch(JSON.stringify({"t": "Welcome", "d": {
		"selfId": 1, "tick": 10, "tickRate": 20, "snapshotRate": 10, "mapId": "meadow",
		"self": {"x": 300.0, "y": 800.0, "level": 5, "xp": 0, "xpNext": 100, "hp": 80, "maxHp": 100, "res": 40, "maxRes": 60, "resource": "mana", "classId": "mage", "name": "Ana"},
		"inventory": [{"id": "i1", "templateId": "potion_minor", "qty": 2}], "equipment": [], "hotbar": [{"slot": 0, "kind": "spell", "ref": "mage_fireball"}],
		"knownSpells": ["mage_fireball"], "rulesHash": "x"}}))
	for i: int in 3:
		await get_tree().process_frame
	return w


func _esc() -> void:
	var ev := InputEventAction.new()
	ev.action = "ui_cancel"
	ev.pressed = true
	Input.parse_input_event(ev)
	await get_tree().process_frame
	await get_tree().process_frame


func test_escape_closes_things_first_and_opens_the_menu_last() -> void:
	var w := await _world()
	var menu: GameMenu = w.get("_game_menu")
	var inv: Control = w.get("_inventory")
	inv.visible = true
	GameState.set_target(42)
	await _esc()
	assert_false(inv.visible, "primero cierra la ventana")
	assert_false(menu.is_open())
	await _esc()
	assert_eq(GameState.target_id, -1, "luego suelta el objetivo")
	assert_false(menu.is_open())
	await _esc()
	assert_true(menu.is_open(), "sin nada más que hacer, abre el menú")
	await _esc()
	assert_false(menu.is_open(), "Esc con el menú abierto es Continuar")


func test_hud_gear_toggles_the_menu() -> void:
	var w := await _world()
	var hud: CombatHud = w.get("_hud")
	var menu: GameMenu = w.get("_game_menu")
	(hud.get("_menu_button") as Button).pressed.emit()
	assert_true(menu.is_open())
	assert_not_null(menu.button("Continuar"))
	assert_not_null(menu.button("Volver a selección de personaje"))
	assert_not_null(menu.button("Salir del juego"))
	menu.button("Continuar").pressed.emit()
	assert_false(menu.is_open())


func test_with_the_menu_open_the_hotbar_and_movement_do_nothing() -> void:
	var w := await _world()
	var menu: GameMenu = w.get("_game_menu")
	menu.open()
	var gcd_before := GameState.gcd_end_ms
	w.call("_use_slot", 0)
	assert_eq(GameState.gcd_end_ms, gcd_before, "no se predijo ningún casteo")
	Input.action_press("move_right")
	var before: Vector2 = (w.get("prediction") as Prediction).position
	w.set("in_world", true)
	w._physics_process(0.1)
	Input.action_release("move_right")
	assert_eq((w.get("prediction") as Prediction).position, before, "el personaje no se mueve")


func test_logout_in_combat_shows_the_toast_and_keeps_the_menu() -> void:
	var w := await _world()
	var menu: GameMenu = w.get("_game_menu")
	var hud: CombatHud = w.get("_hud")
	menu.open()
	w.set("_logout_req_id", 77)
	w.set("_logout_after", "select")
	menu.waiting = true
	Net._dispatch(JSON.stringify({"t": "Error", "d": {"code": "in_combat", "reqId": 77}}))
	await get_tree().process_frame
	assert_eq((hud.get("_error_label") as Label).text, "No puedes salir en combate")
	assert_true((hud.get("_toast") as Control).visible)
	assert_true(menu.is_open(), "sigue abierto para Continuar")
	assert_false(menu.button("Volver a selección de personaje").disabled)
	assert_eq(str(w.get("_logout_after")), "", "ya no espera el LoggedOut")


func test_reset_clears_the_previous_character() -> void:
	await _world()
	GameState.set_target(9)
	GameState.party = {"leader": "Ana"}
	GameState.reset()
	assert_eq(GameState.self_id, -1)
	assert_eq(GameState.character_name, "")
	assert_eq(GameState.target_id, -1)
	assert_true(GameState.inventory.is_empty())
	assert_true(GameState.hotbar.is_empty())
	assert_true(GameState.known_spells.is_empty())
	assert_true(GameState.party.is_empty())
	assert_eq(GameState.resource_kind, "mana")


func test_logged_out_disconnects_resets_and_goes_to_character_select() -> void:
	var w := await _world()
	var went: Array[String] = []
	w.set("change_scene", func(path: String) -> void: went.append(path))
	(w.get("_game_menu") as GameMenu).open()
	w.call("request_logout", "select")  # sin conexión real: sale directamente, como si ya hubiera llegado LoggedOut
	assert_eq(went, ["res://scenes/character_select/character_select.tscn"] as Array[String])
	assert_eq(GameState.self_id, -1, "no queda nada del personaje anterior")
	assert_false(Net.get("_auto_reconnect"), "no intenta reconectar")
	assert_false(bool(w.get("in_world")))


func test_logged_out_message_finishes_the_requested_logout() -> void:
	var w := await _world()
	var went: Array[String] = []
	w.set("change_scene", func(path: String) -> void: went.append(path))
	w.set("_logout_after", "select")
	Net._dispatch(JSON.stringify({"t": "LoggedOut", "d": {}}))
	assert_eq(went.size(), 1)
	assert_eq(GameState.character_name, "")
