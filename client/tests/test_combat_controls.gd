extends GutTest
## HU-094 (básico con Espacio), HU-095 (acercarse solo) y HU-096 (círculo de alcance): escena World sin servidor, con
## los mensajes inyectados como si llegaran por Net y los envíos capturados en `send_fn`.

const WORLD := preload("res://scenes/world/world.tscn")
const STAFF_REACH_PX := 5.0 * 16.0  # rules.weapons.types.staff.rangeTiles

var _world: Node2D
var _sent: Array[Dictionary] = []


func before_each() -> void:
	Settings._config.set_value("net", "server_url", "http://127.0.0.1:1")
	GameState.pending_ticket = "t"
	GameState.pending_character_id = "c"
	_world = WORLD.instantiate()
	add_child_autofree(_world)
	await get_tree().process_frame
	Net.disconnect_from_server()
	for i: int in 3:
		await get_tree().process_frame
	_sent.clear()
	_world.send_fn = func(type: String, data: Dictionary) -> void: _sent.append({"t": type, "d": data})
	var equipment: Array = []
	equipment.resize(9)
	equipment[7] = {"id": "w1", "templateId": "apprentice_staff", "qty": 1}
	_dispatch("Welcome", {
		"selfId": 1, "tick": 10, "tickRate": 20, "snapshotRate": 10, "mapId": "meadow",
		"self": {"x": 100.0, "y": 100.0, "level": 3, "xp": 0, "xpNext": 100, "hp": 80, "maxHp": 100, "res": 40, "maxRes": 60, "resource": "mana", "classId": "mage", "name": "Ana"},
		"inventory": [], "equipment": equipment, "hotbar": [{"slot": 0, "kind": "spell", "ref": "mage_fireball"}],
		"knownSpells": ["mage_fireball"], "rulesHash": "x",
	})
	_world.in_world = true


func _dispatch(type: String, d: Dictionary) -> void:
	Net._dispatch(JSON.stringify({"t": type, "d": d}))


func _spawn_slime(id: int, x: float) -> void:
	_dispatch("EntitySpawn", {"id": id, "kind": "monster", "templateId": "slime", "name": "Slime", "x": x, "y": 100.0, "dir": "s", "level": 1, "hpPct": 100, "flags": 0})


func _types() -> Array[String]:
	var out: Array[String] = []
	for m: Dictionary in _sent:
		out.append(str(m["t"]))
	return out


func _press(action: String) -> void:
	var ev := InputEventAction.new()
	ev.action = action
	ev.pressed = true
	_world._unhandled_input(ev)


func test_space_is_the_basic_attack_action() -> void:
	assert_true(InputMap.has_action("basic_attack"))
	var keys := InputMap.action_get_events("basic_attack").filter(func(e: InputEvent) -> bool: return e is InputEventKey and (e as InputEventKey).keycode == KEY_SPACE)
	assert_eq(keys.size(), 1)


func test_basic_reach_comes_from_the_equipped_weapon() -> void:
	assert_almost_eq(_world.basic_reach_px(), STAFF_REACH_PX, 0.01)
	GameState.equipment = []
	assert_eq(_world.basic_reach_px(), 0.0, "sin arma no hay básico")


func test_space_attacks_the_selected_enemy_in_reach_without_moving() -> void:
	_spawn_slime(7, 150.0)
	_world._select(7)
	_sent.clear()
	_press("basic_attack")
	assert_eq(_types(), ["AutoAttack"])
	assert_eq(_sent[0]["d"], {"on": true})
	assert_false(_world._approach.is_active())


func test_space_without_target_picks_the_nearest_enemy() -> void:
	_spawn_slime(7, 200.0)
	_spawn_slime(8, 130.0)
	_press("basic_attack")
	assert_eq(GameState.target_id, 8)
	assert_eq(_types(), ["SelectTarget", "AutoAttack"])


func test_space_with_no_enemy_around_does_nothing() -> void:
	_press("basic_attack")
	assert_eq(_sent.size(), 0)


func test_far_target_starts_walking_towards_it() -> void:
	_spawn_slime(7, 300.0)
	_world._select(7)
	_press("basic_attack")
	assert_true(_world._approach.is_active(), "200 px > 80 px del bastón: se acerca")
	assert_eq(_world._step_approach(), Vector2i(1, 0))


func test_out_of_range_spell_walks_then_casts_on_arrival() -> void:
	_spawn_slime(7, 300.0)  # bola de fuego: 8 casillas = 128 px; está a 200 px
	_world._select(7)
	_sent.clear()
	_world._use_slot(0)
	assert_false(_types().has("CastSpell"), "aún lejos: no se lanza")
	assert_true(_world._approach.is_active())
	_world.prediction.position = Vector2(200, 100)  # llegó caminando
	_world._step_approach()
	assert_true(_types().has("CastSpell"), "al entrar en alcance se lanza (HU-095 CA2)")
	assert_false(_world._approach.is_active())


func test_changing_target_cancels_the_approach() -> void:
	_spawn_slime(7, 300.0)
	_spawn_slime(8, 60.0)
	_world._select(7)
	_press("basic_attack")
	assert_true(_world._approach.is_active())
	_world._select(8)
	assert_false(_world._approach.is_active())


func test_escape_cancels_the_approach() -> void:
	_spawn_slime(7, 300.0)
	_world._select(7)
	_press("basic_attack")
	_press("ui_cancel")
	assert_false(_world._approach.is_active())


func test_range_ring_colors() -> void:
	var ring := RangeRing.new()
	add_child_autofree(ring)
	ring.show_range(80.0, 1)
	assert_true(ring.visible)
	assert_eq(ring.color(), RangeRing.IN_RANGE)
	ring.show_range(80.0, 0)
	assert_eq(ring.color(), RangeRing.OUT_OF_RANGE)
	ring.show_range(0.0, -1)
	assert_false(ring.visible, "sin alcance (p. ej. sin arma) no se dibuja")
