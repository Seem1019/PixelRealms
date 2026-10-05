extends GutTest
## HU-101: la zona del duelo llega en la cuenta atrás, el aviso de estar fuera no repite el "¡Luchad!" y todo se borra al terminar.


func after_each() -> void:
	GameState.reset()


func _dispatch(d: Dictionary) -> void:
	Net._dispatch(JSON.stringify({"t": "DuelUpdate", "d": d}))


func test_zone_warning_and_end() -> void:
	var ring := DuelZoneRing.new()
	add_child_autofree(ring)
	watch_signals(GameState)
	var zone := {"x": 168.0, "y": 160.0, "r": 192.0}
	_dispatch({"state": "countdown", "opponentId": 8, "startsInMs": 3000, "zone": zone})
	assert_eq(GameState.duel_zone.get("r"), 192.0)
	assert_true(ring.visible, "la línea se ve ya en la cuenta atrás")
	assert_eq(ring.position, Vector2(168, 160))
	_dispatch({"state": "active", "opponentId": 8, "zone": zone})
	assert_signal_emit_count(GameState, "duel_changed", 2)
	assert_false(ring.outside)

	_dispatch({"state": "active", "opponentId": 8, "zone": zone, "outsideMs": 5000})
	assert_signal_emit_count(GameState, "duel_changed", 2, "el aviso no es un duelo nuevo")
	assert_true(GameState.duel_outside_until_ms > Time.get_ticks_msec() + 4000)
	assert_true(ring.outside)
	assert_eq(GameState.duel_state, "active")
	assert_eq(GameState.duel_opponent_id, 8)

	_dispatch({"state": "active", "opponentId": 8, "zone": zone})
	assert_eq(GameState.duel_outside_until_ms, -1, "volvió a la zona")
	assert_false(ring.outside)

	_dispatch({"state": "ended", "opponentId": 8, "winnerId": 3, "reason": "zone"})
	assert_eq(GameState.duel_reason, "zone")
	assert_true(GameState.duel_zone.is_empty())
	assert_false(ring.visible)
