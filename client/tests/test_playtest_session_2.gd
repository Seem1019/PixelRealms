extends GutTest
## Segunda partida de prueba (2 h, dos jugadores): cada test reproduce un fallo visto jugando (círculo de área, NPC,
## intercambio, duelo, recarga de pociones). Instancia la escena World sin servidor y le inyecta mensajes como si llegaran
## por Net.

const WORLD := preload("res://scenes/world/world.tscn")

var _world: Node2D
var _draws: int = 0


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
		"self": {"x": 100.0, "y": 100.0, "level": 5, "xp": 0, "xpNext": 100, "hp": 80, "maxHp": 100, "res": 100, "maxRes": 100, "resource": "mana", "classId": "mage", "name": "Ana"},
		"inventory": [], "equipment": [],
		"hotbar": [{"slot": 0, "kind": "spell", "ref": "mage_flame_burst"}, {"slot": 1, "kind": "spell", "ref": "mage_fireball"}],
		"knownSpells": ["mage_flame_burst", "mage_fireball"], "rulesHash": "x",
	}
	d.merge(extra, true)
	_dispatch("Welcome", d)


func _frames(n: int) -> void:
	for i: int in n:
		await get_tree().process_frame


# --- 4: en duelo el rival es un objetivo enemigo -------------------------------------------------------------------------

func test_duel_opponent_is_hostile_only_while_the_duel_is_active() -> void:
	_welcome()
	_dispatch("EntitySpawn", {"id": 8, "kind": "player", "templateId": "warrior", "name": "Bob", "x": 110.0, "y": 100.0, "dir": "s", "level": 2, "classId": "warrior", "hpPct": 100, "flags": 0})
	await _frames(1)
	var bob: RemoteEntity = _world._remotes[8]
	assert_false(bob.hostile)
	_dispatch("DuelUpdate", {"state": "active", "opponentId": 8})
	assert_true(bob.hostile, "clic derecho abría el menú de jugador en vez de autoatacar")
	_world._cycle_target()
	assert_eq(GameState.target_id, 8, "Tab también lo selecciona")
	_dispatch("DuelUpdate", {"state": "ended", "opponentId": 8, "winnerId": 1})
	assert_false(bob.hostile)


# --- 1: el círculo de área se quedaba pintado al cancelar o lanzar -------------------------------------------------------

func test_area_circle_is_erased_when_aiming_stops() -> void:
	_welcome()
	await _frames(2)
	_world._reticle.draw.connect(func() -> void: _draws += 1)
	_world._use_slot(0)  # Estallido de llamas: empieza a apuntar
	await _frames(3)
	assert_gt(_draws, 0, "mientras se apunta se redibuja")
	_world._stop_aiming()  # clic derecho o clic izquierdo para lanzar
	var before := _draws
	await _frames(3)
	assert_gt(_draws, before, "sin un redibujado más, el último círculo se quedaba en pantalla hasta volver a apuntar")
	var settled := _draws
	await _frames(3)
	assert_eq(_draws, settled, "y después deja de redibujar")
