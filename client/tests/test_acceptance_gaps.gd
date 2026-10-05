extends GutTest
## Criterios ya implementados que no tenían test: HU-031 CA4 (color del nivel de los monstruos según la diferencia con el mío) y
## HU-062 CA5 (F1–F5 seleccionan a uno mismo y a los miembros 1–4 del grupo), más sus bordes.

const WORLD := preload("res://scenes/world/world.tscn")
## Colores de RemoteEntity.level_color para los rangos de HU-031 CA4.
const GREY := Color(0.6, 0.6, 0.6)
const GREEN := Color(0.3, 0.9, 0.3)
const YELLOW := Color(1, 0.9, 0.2)
const ORANGE := Color(1, 0.55, 0.1)
const RED := Color(1, 0.2, 0.2)


func after_each() -> void:
	GameState.reset()


func _dispatch(type: String, d: Dictionary) -> void:
	Net._dispatch(JSON.stringify({"t": type, "d": d}))


## Escena World sin servidor, con un Welcome de Ana (id 1, maga) al nivel dado.
func _world_at_level(level: int) -> Node2D:
	Settings._config.set_value("net", "server_url", "http://127.0.0.1:1")  # nadie escucha: la conexión falla sin romper el test
	GameState.pending_ticket = "t"
	GameState.pending_character_id = "c"
	var world: Node2D = WORLD.instantiate()
	add_child_autofree(world)
	await get_tree().process_frame
	Net.disconnect_from_server()
	for i: int in 3:
		await get_tree().process_frame
	_dispatch("Welcome", {
		"selfId": 1, "tick": 10, "tickRate": 20, "snapshotRate": 10, "mapId": "meadow",
		"self": {"x": 100.0, "y": 100.0, "level": level, "xp": 0, "xpNext": 100, "hp": 80, "maxHp": 100, "res": 40, "maxRes": 60, "resource": "mana", "classId": "mage", "name": "Ana"},
		"inventory": [], "equipment": [], "hotbar": [], "knownSpells": [], "rulesHash": "x",
	})
	await get_tree().process_frame
	return world


func _monster(id: int, level: int) -> Dictionary:
	return {"id": id, "kind": "monster", "templateId": "slime", "name": "Slime", "x": 120.0, "y": 100.0, "dir": "s", "level": level, "hpPct": 100, "flags": 0}


# --- HU-031 CA4: color del nivel ----------------------------------------------------------------------------------------

func test_level_color_follows_the_thresholds_of_the_story() -> void:  # HU-031 CA4
	# Gris ≤ −5, verde −3..−4, amarillo ±2, naranja +3..+4, rojo ≥ +5 (con los dos lados de cada frontera).
	var expected := {-9: GREY, -5: GREY, -4: GREEN, -3: GREEN, -2: YELLOW, 0: YELLOW, 2: YELLOW, 3: ORANGE, 4: ORANGE, 5: RED, 10: RED}
	for diff: int in expected:
		assert_eq(RemoteEntity.level_color(diff), expected[diff], "diferencia %d" % diff)


func test_monster_plate_colors_its_level_against_mine() -> void:  # HU-031 CA4
	GameState.level = 6
	var monster := RemoteEntity.new()
	add_child_autofree(monster)
	var cases := {1: GREY, 2: GREEN, 6: YELLOW, 9: ORANGE, 11: RED}
	for monster_level: int in cases:
		monster.setup(_monster(7, monster_level))
		assert_eq(monster.visual.plate.level_text, str(monster_level))
		assert_eq(monster.visual.plate.level_color, cases[monster_level], "monstruo nv%d siendo yo nv6" % monster_level)
	var player := RemoteEntity.new()
	add_child_autofree(player)
	player.setup({"id": 8, "kind": "player", "name": "Bob", "level": 12, "classId": "warrior", "hpPct": 100})
	# HU-041 CA3: el nivel de un jugador se ve sobre su nombre, pero sin color de dificultad (eso es solo de los monstruos).
	assert_eq(player.visual.plate.level_text, "12")
	assert_eq(player.visual.plate.level_color, UiTheme.TEXT_MUTED, "el nivel coloreado es solo de los monstruos")


func test_monster_level_colors_follow_my_level_up() -> void:  # HU-031 CA4 (borde: subo de nivel con monstruos a la vista)
	var world: Node2D = await _world_at_level(3)
	_dispatch("EntitySpawn", _monster(7, 1))
	await get_tree().process_frame
	var slime: RemoteEntity = world._remotes[7]
	assert_eq(slime.visual.plate.level_color, YELLOW, "nv1 siendo yo nv3: −2")
	_dispatch("LevelUp", {"level": 4, "newSpells": []})
	await get_tree().process_frame
	assert_eq(GameState.level, 4)
	assert_eq(slime.visual.plate.level_color, GREEN, "nv1 siendo yo nv4: −3, el Slime ya es verde")


# --- HU-062 CA5: F1–F5 ------------------------------------------------------------------------------------------------

func _key(keycode: Key, pressed: bool = true, echo: bool = false) -> InputEventKey:
	var ev := InputEventKey.new()
	ev.keycode = keycode
	ev.pressed = pressed
	ev.echo = echo
	return ev


func _member(member_name: String, entity_id: int) -> Dictionary:
	var md := {"name": member_name, "classId": "warrior", "level": 3, "hpPct": 100, "online": true, "mapId": "meadow"}
	if entity_id > 0:
		md["entityId"] = entity_id
	return md


## Ana (id 1) con el grupo dado (ella incluida, como lo manda PartyUpdate) y unos marcos de grupo vigilados.
func _panels_with_party(members: Array) -> SocialPanels:
	GameState.self_id = 1
	GameState.character_name = "Ana"
	GameState.party = {} if members.is_empty() else {"leader": "Ana", "members": members}
	var panels := SocialPanels.new()
	add_child_autofree(panels)
	watch_signals(panels)
	return panels


func _full_party() -> Array:
	return [_member("Ana", 1), _member("Bob", 8), _member("Cid", 9), _member("Dan", 10), _member("Eva", 11)]


func test_f1_selects_myself() -> void:  # HU-062 CA5
	var panels := _panels_with_party(_full_party())
	panels._unhandled_input(_key(KEY_F1))
	assert_signal_emitted_with_parameters(panels, "party_member_selected", [1])


func test_f2_to_f5_select_members_one_to_four() -> void:  # HU-062 CA5
	var panels := _panels_with_party(_full_party())
	var keys: Array[Key] = [KEY_F2, KEY_F3, KEY_F4, KEY_F5]
	var ids: Array[int] = [8, 9, 10, 11]
	for i: int in keys.size():
		panels._unhandled_input(_key(keys[i]))
		assert_signal_emitted_with_parameters(panels, "party_member_selected", [ids[i]], i)
	assert_signal_emit_count(panels, "party_member_selected", 4)


func test_f_key_of_a_missing_member_selects_nobody() -> void:  # HU-062 CA5 (borde: grupo de dos)
	var panels := _panels_with_party([_member("Ana", 1), _member("Bob", 8)])
	panels._unhandled_input(_key(KEY_F3))
	panels._unhandled_input(_key(KEY_F5))
	assert_signal_not_emitted(panels, "party_member_selected")
	panels._unhandled_input(_key(KEY_F2))
	assert_signal_emitted_with_parameters(panels, "party_member_selected", [8])


func test_member_without_entity_is_not_selectable() -> void:  # HU-062 CA5 (borde: en otro mapa o desconectado)
	var panels := _panels_with_party([_member("Ana", 1), _member("Bob", -1), _member("Cid", 9)])
	panels._unhandled_input(_key(KEY_F2))
	assert_signal_not_emitted(panels, "party_member_selected")
	panels._unhandled_input(_key(KEY_F3))  # el orden es el del grupo: F3 sigue siendo Cid aunque Bob no tenga entidad
	assert_signal_emitted_with_parameters(panels, "party_member_selected", [9])


func test_without_a_party_f1_still_selects_myself() -> void:  # HU-062 CA5 (borde: sin grupo)
	var panels := _panels_with_party([])
	panels._unhandled_input(_key(KEY_F2))
	assert_signal_not_emitted(panels, "party_member_selected")
	panels._unhandled_input(_key(KEY_F1))
	assert_signal_emitted_with_parameters(panels, "party_member_selected", [1])


func test_released_repeated_or_other_f_keys_do_not_select() -> void:  # HU-062 CA5 (borde: soltar, autorrepetición, F6)
	var panels := _panels_with_party(_full_party())
	panels._unhandled_input(_key(KEY_F2, false))
	panels._unhandled_input(_key(KEY_F2, true, true))
	panels._unhandled_input(_key(KEY_F6))
	assert_signal_not_emitted(panels, "party_member_selected")


func test_f2_in_the_world_targets_that_member_and_tells_the_server() -> void:  # HU-062 CA5 (de punta a punta)
	var world: Node2D = await _world_at_level(3)
	var sent: Array[Dictionary] = []
	world.send_fn = func(type: String, data: Dictionary) -> void: sent.append({"t": type, "d": data})
	GameState.party = {"leader": "Ana", "members": [_member("Ana", 1), _member("Bob", 8)]}
	var social: SocialPanels = world._social
	social._unhandled_input(_key(KEY_F2))
	assert_eq(GameState.target_id, 8)
	var select := sent.filter(func(m: Dictionary) -> bool: return m["t"] == "SelectTarget")
	assert_eq(select.size(), 1)
	if select.size() == 1:
		var d: Dictionary = (select[0] as Dictionary)["d"]
		assert_eq(int(d.get("targetId", -1)), 8)
