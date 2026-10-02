extends GutTest
## HU-095: acercarse solo al objetivo fuera de alcance (lógica pura de Approach).


func test_direction_is_eight_way() -> void:
	assert_eq(Approach.direction(Vector2.ZERO, Vector2(100, 0)), Vector2i(1, 0))
	assert_eq(Approach.direction(Vector2.ZERO, Vector2(0, -50)), Vector2i(0, -1))
	assert_eq(Approach.direction(Vector2.ZERO, Vector2(-40, 40)), Vector2i(-1, 1), "diagonal exacta")
	assert_eq(Approach.direction(Vector2.ZERO, Vector2(100, 20)), Vector2i(1, 0), "eje menor < tan(22,5°): recto")
	assert_eq(Approach.direction(Vector2.ZERO, Vector2(100, 60)), Vector2i(1, 1))
	assert_eq(Approach.direction(Vector2.ZERO, Vector2.ZERO), Vector2i.ZERO)


func test_walks_until_in_reach_then_arrives() -> void:
	var a := Approach.new()
	a.start(7, 24.0, 0)  # básico de espada: 1,5 casillas
	assert_true(a.is_active())
	assert_eq(a.update(Vector2.ZERO, Vector2(80, 0), 50), Vector2i(1, 0))
	assert_eq(a.state, Approach.State.MOVING)
	assert_eq(a.update(Vector2(60, 0), Vector2(80, 0), 100), Vector2i.ZERO, "a 20 px ≤ 24 − margen: llegó")
	assert_eq(a.state, Approach.State.ARRIVED)
	assert_false(a.is_active())


func test_already_in_reach_arrives_without_moving() -> void:
	var a := Approach.new()
	a.start(7, 80.0, 0)
	assert_eq(a.update(Vector2.ZERO, Vector2(50, 0), 0), Vector2i.ZERO)
	assert_eq(a.state, Approach.State.ARRIVED)


func test_gives_up_when_no_progress_for_a_second() -> void:
	var a := Approach.new()
	a.start(7, 24.0, 0)
	a.update(Vector2.ZERO, Vector2(200, 0), 0)
	assert_eq(a.update(Vector2(1, 0), Vector2(200, 0), 600), Vector2i(1, 0), "aún no pasa 1 s")
	assert_eq(a.update(Vector2(1, 0), Vector2(200, 0), 1000), Vector2i.ZERO, "contra una pared: se abandona")
	assert_eq(a.state, Approach.State.STUCK)


func test_progress_resets_the_stuck_timer() -> void:
	var a := Approach.new()
	a.start(7, 24.0, 0)
	a.update(Vector2.ZERO, Vector2(400, 0), 0)
	a.update(Vector2(30, 0), Vector2(400, 0), 900)
	assert_eq(a.update(Vector2(60, 0), Vector2(400, 0), 1800), Vector2i(1, 0), "avanzando: sigue")
	assert_eq(a.state, Approach.State.MOVING)


func test_cancel_stops_and_clears_pending_action() -> void:
	var a := Approach.new()
	a.start(7, 24.0, 0, func() -> void: pass)
	a.cancel()
	assert_false(a.is_active())
	assert_false(a.on_arrive.is_valid())
	assert_eq(a.update(Vector2.ZERO, Vector2(80, 0), 10), Vector2i.ZERO)
