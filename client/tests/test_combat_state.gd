extends GutTest
## El cliente aproxima el "en combate" del servidor con los golpes que le llegan (para no pedir cambios de la barra que el
## servidor rechazaría con `in_combat`).


func after_each() -> void:
	GameState.reset()


func test_a_hit_on_myself_puts_me_in_combat_but_a_heal_does_not() -> void:
	GameState.self_id = 7
	assert_false(GameState.is_in_combat())
	Net._dispatch(JSON.stringify({"t": "CombatEvents", "d": {"tick": 1, "e": [{"src": 7, "dst": 7, "kind": "heal", "amount": 10, "crit": false, "school": "magic"}]}}))
	assert_false(GameState.is_in_combat(), "una cura propia no mete en combate")
	Net._dispatch(JSON.stringify({"t": "CombatEvents", "d": {"tick": 2, "e": [{"src": 3, "dst": 7, "kind": "dmg", "amount": 5, "crit": false, "school": "physical"}]}}))
	assert_true(GameState.is_in_combat())


func test_hits_between_others_do_not_count() -> void:
	GameState.self_id = 7
	Net._dispatch(JSON.stringify({"t": "CombatEvents", "d": {"tick": 1, "e": [{"src": 3, "dst": 4, "kind": "dmg", "amount": 5, "crit": false, "school": "physical"}]}}))
	assert_false(GameState.is_in_combat())
