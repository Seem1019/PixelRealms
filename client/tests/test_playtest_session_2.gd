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


# --- 3: ventana de intercambio con casillas ------------------------------------------------------------------------------

const PELT := "0192f0aa-0000-7000-8000-0000000000aa"
const BREAD := "0192f0aa-0000-7000-8000-0000000000bb"


func _open_trade(mine_items: Array, theirs_items: Array, version: int = 1) -> void:
	_dispatch("TradeUpdate", {"state": "open", "partnerId": 8, "version": version, "mine": {"items": mine_items, "gold": 0},
		"theirs": {"items": theirs_items, "gold": 150}, "confirmedMine": false, "confirmedTheirs": true})


func test_trade_window_shows_both_offers_in_item_slots() -> void:
	_welcome({"inventory": [{"id": BREAD, "templateId": "bread", "qty": 20}]})
	_dispatch("EntitySpawn", {"id": 8, "kind": "player", "templateId": "warrior", "name": "Bob", "x": 110.0, "y": 100.0, "dir": "s", "level": 2, "classId": "warrior", "hpPct": 100, "flags": 0})
	await _frames(1)
	_open_trade([{"itemId": BREAD, "templateId": "bread", "qty": 20}], [{"itemId": PELT, "templateId": "wolf_pelt", "qty": 2}])
	await _frames(2)
	var trade: TradeWindow = _world._social._trade
	assert_true(trade.visible)
	assert_string_contains(trade.title_text(), "Bob", "el título dice con quién comercias")
	assert_eq(trade.mine_slots().size(), 6)
	assert_eq(trade.theirs_slots().size(), 6)
	var received: ItemSlot = trade.theirs_slots()[0]
	assert_eq(str(received.item.get("templateId", "")), "wolf_pelt", "antes solo se leía «item ×2»")
	assert_eq(received._qty.text, "2")
	assert_string_contains(received.tooltip_bbcode, "Piel de lobo")
	assert_eq(str(trade.mine_slots()[0].item.get("templateId", "")), "bread")
	assert_string_contains(trade.status_text(), "Bob")


func test_trade_window_never_covers_the_bag() -> void:
	_welcome()
	await _frames(1)
	_world._inventory.toggle()
	_open_trade([], [])
	await _frames(3)
	var trade: Control = _world._social._trade
	assert_false(trade.get_global_rect().intersects(_world._inventory.get_global_rect()), "la ventana tapaba la bolsa")
	assert_lt(trade.size.y, UiTheme.base_size().y * 0.75, "una etiqueta con autoajuste sin ancho estiraba el panel a toda la altura")


func test_items_are_offered_by_dragging_from_the_bag_and_removed_with_right_click() -> void:
	_welcome({"inventory": [{"id": BREAD, "templateId": "bread", "qty": 20}]})
	await _frames(1)
	_open_trade([], [])
	await _frames(1)
	var trade: TradeWindow = _world._social._trade
	trade._on_mine_dropped({"c": "bag", "i": 0}, {"c": "trade", "i": 0}, 0)  # soltar la casilla 0 de la bolsa
	assert_eq(trade.offered_ids(), [BREAD] as Array[String])
	_open_trade([{"itemId": BREAD, "templateId": "bread", "qty": 20}], [], 2)
	trade._on_mine_right_clicked(trade.mine_slots()[0])
	assert_true(trade.offered_ids().is_empty(), "clic derecho en tu casilla la retira")
	assert_false(trade.mine_slots()[0].draggable, "las casillas del intercambio no se arrastran a la bolsa")


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
