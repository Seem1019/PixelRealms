extends GutTest
## HU-063: lista de jugadores en línea (tecla O) con nombre, clase, nivel y zona.


func test_row_shows_name_class_level_and_zone() -> void:
	var text := SocialPanels.online_row_text({"name": "Ana", "classId": "mage", "level": 4, "zone": "Campos"})
	assert_eq(text, "Ana · %s · nv 4 · Campos" % UiText.class_name_of("mage"))


func test_online_list_message_reaches_game_state() -> void:
	watch_signals(GameState)
	Net._dispatch(JSON.stringify({"t": "OnlineList", "d": {"players": [{"name": "Bob", "classId": "priest", "level": 2, "zone": "Aldea"}]}}))
	assert_signal_emitted(GameState, "online_list_received")


func test_o_key_opens_the_online_list() -> void:
	assert_true(InputMap.has_action("toggle_online_list"))
	var keys: Array = InputMap.action_get_events("toggle_online_list").map(func(e: InputEvent) -> int: return (e as InputEventKey).keycode)
	assert_has(keys, KEY_O)


## HU-063 CA2: la fila de otro jugador abre el menú (susurrar / invitar) con clic izquierdo o derecho; la propia no.
func test_other_player_rows_accept_right_click() -> void:
	var panels := SocialPanels.new()
	add_child_autofree(panels)
	var me := GameState.character_name
	panels._show_online_list([{"name": me, "classId": "warrior", "level": 3, "zone": "Aldea"}, {"name": "Bob", "classId": "priest", "level": 2, "zone": "Campos"}])
	assert_true(panels.is_online_list_open())
	var rows: Array[Button] = []
	for c: Node in panels._online_rows.get_children():
		if c is Button and not c.is_queued_for_deletion():
			rows.append(c as Button)
	assert_eq(rows.size(), 2)
	var bob: Button = rows[1]
	assert_ne(bob.button_mask & MOUSE_BUTTON_MASK_RIGHT, 0)
	assert_ne(bob.button_mask & MOUSE_BUTTON_MASK_LEFT, 0)
	assert_eq(rows[0].pressed.get_connections().size(), 0)
