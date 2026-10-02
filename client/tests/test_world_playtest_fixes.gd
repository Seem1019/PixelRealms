extends GutTest
## Arreglos de la prueba de juego: cada test reproduce un fallo visto jugando (barra, apuntado, cambio de clase, XP, vida).
## Instancia la escena World sin servidor y le inyecta mensajes como si llegaran por Net.

const WORLD := preload("res://scenes/world/world.tscn")

var _world: Node2D


func before_each() -> void:
	Settings._config.set_value("net", "server_url", "http://127.0.0.1:1")  # nadie escucha: la conexión falla sin romper el test
	GameState.pending_ticket = "t"
	GameState.pending_character_id = "c"
	_world = WORLD.instantiate()
	add_child_autofree(_world)
	await get_tree().process_frame
	Net.disconnect_from_server()  # sin reintentos durante el test
	for i: int in 3:
		await get_tree().process_frame


func _dispatch(type: String, d: Dictionary) -> void:
	Net._dispatch(JSON.stringify({"t": type, "d": d}))


func _welcome(extra: Dictionary = {}) -> void:
	var d := {
		"selfId": 1, "tick": 10, "tickRate": 20, "snapshotRate": 10, "mapId": "meadow",
		"self": {"x": 100.0, "y": 100.0, "level": 3, "xp": 0, "xpNext": 100, "hp": 80, "maxHp": 100, "res": 40, "maxRes": 60, "resource": "mana", "classId": "mage", "name": "Ana"},
		"inventory": [], "equipment": [], "hotbar": [{"slot": 0, "kind": "spell", "ref": "mage_fireball"}],
		"knownSpells": ["mage_fireball"], "rulesHash": "x",
	}
	d.merge(extra, true)
	_dispatch("Welcome", d)


# --- Punto 1: el seq de movimiento es por conexión ---------------------------------------------------------------------

func test_second_welcome_on_the_same_connection_keeps_the_move_seq() -> void:
	_welcome()
	await get_tree().process_frame
	_world._movement.seq = 120  # lleva un rato caminando
	_welcome()  # p. ej. el que reenvía el servidor tras cambiar de clase
	assert_eq(_world._movement.seq, 120, "el servidor descartaría los MoveInput con seq ≤ 120")


func test_new_connection_restarts_the_move_seq() -> void:
	_welcome()
	_world._movement.seq = 120
	_world._on_connected("t")  # reconexión: Hello nuevo, el servidor reinicia LastInputSeq
	assert_eq(_world._movement.seq, 0)


# --- Punto 8: cambio de clase ----------------------------------------------------------------------------------------

func test_class_change_welcome_keeps_the_world_and_applies_the_new_class() -> void:
	_welcome()
	_dispatch("EntitySpawn", {"id": 7, "kind": "monster", "templateId": "slime", "name": "Slime", "x": 120.0, "y": 100.0, "dir": "s", "level": 1, "hpPct": 100, "flags": 0})
	_dispatch("EntitySpawn", {"id": 9, "kind": "npc", "templateId": "class_change", "name": "Maestra", "x": 110.0, "y": 100.0, "dir": "s", "level": 1, "hpPct": 100, "flags": 0})
	await get_tree().process_frame
	_world._select(7)
	_dispatch("AuraApplied", {"targetId": 7, "auraId": "mage_chill", "casterId": 1, "stacks": 1, "durationMs": 4000})
	var map_before: TmjMap = _world.map
	# El servidor reenvía Welcome por la misma conexión con la clase nueva (EventDispatcher, ClassChangedEvent).
	_welcome({"self": {"x": 100.0, "y": 100.0, "level": 3, "xp": 0, "xpNext": 100, "hp": 90, "maxHp": 110, "res": 100, "maxRes": 100, "resource": "mana", "classId": "priest", "name": "Ana"},
		"hotbar": [{"slot": 0, "kind": "spell", "ref": "priest_heal"}], "knownSpells": ["priest_heal"]})
	await get_tree().process_frame
	assert_eq(_world._remotes.size(), 2, "el servidor no vuelve a mandar EntitySpawn: borrarlas deja el mundo vacío")
	assert_eq(_world.map, map_before, "mismo mapa: no se recarga")
	assert_eq(GameState.class_id, "priest")
	assert_eq(GameState.known_spells, ["priest_heal"] as Array[String])
	assert_eq(str((GameState.hotbar[0] as Dictionary).get("ref", "")), "priest_heal")
	assert_eq(GameState.target_id, 7, "el objetivo sigue seleccionado")
	assert_eq(GameState.auras_of(7).size(), 1, "las auras de otros no cambian con mi clase")


# --- Punto 6: poción desde la barra ----------------------------------------------------------------------------------

func test_hotbar_item_sends_the_bag_instance_id_not_the_template() -> void:
	_welcome({
		"inventory": [{"id": "0192f0aa-0000-7000-8000-000000000001", "templateId": "bread", "qty": 3}, null,
			{"id": "0192f0aa-0000-7000-8000-000000000002", "templateId": "minor_healing_potion", "qty": 20}],
		"hotbar": [{"slot": 4, "kind": "item", "ref": "minor_healing_potion"}],
	})
	var payload: Dictionary = _world._use_item_payload("minor_healing_potion")
	assert_eq(str(payload.get("itemId", "")), "0192f0aa-0000-7000-8000-000000000002")
	assert_true(payload.has("reqId"))


func test_hotbar_item_without_stock_sends_nothing() -> void:
	_welcome({"inventory": [], "hotbar": [{"slot": 4, "kind": "item", "ref": "minor_healing_potion"}]})
	assert_true((_world._use_item_payload("minor_healing_potion") as Dictionary).is_empty())
