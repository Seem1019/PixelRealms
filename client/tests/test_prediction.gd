extends GutTest
## HU-022 CA3: reconciliación suave (< 2 px) o salto; HU-023 CA3: interpolación 100 ms.


func _grid() -> CollisionGrid:
	return CollisionGrid.new(20, 20)


func test_prediction_resimulates_pending_inputs() -> void:
	var p := Prediction.new()
	p.setup(_grid(), Vector2(40, 40), 4.0)
	p.apply_input(1, 1, 0)
	p.apply_input(2, 1, 0)
	p.apply_input(3, 1, 0)
	assert_almost_eq(p.position.x, 49.6, 0.001)
	# El servidor confirma el seq 1 en (43.2, 40): los seq 2 y 3 se re-simulan → 49.6 sin error.
	p.reconcile(Vector2(43.2, 40), 1, 4.0)
	assert_almost_eq(p.position.x, 49.6, 0.001)
	assert_eq(p.pending.size(), 2)
	assert_almost_eq(p.last_error_px, 0.0, 0.001)


func test_small_error_lerps_big_error_snaps() -> void:
	var p := Prediction.new()
	p.setup(_grid(), Vector2(40, 40), 4.0)
	p.apply_input(1, 1, 0)  # 43.2
	p.reconcile(Vector2(42.0, 40), 1, 4.0)  # error 1.2 px → suave
	assert_almost_eq(p.last_error_px, 1.2, 0.001)
	assert_almost_eq(p.render_position.x, 43.2, 0.001)
	p.update_render(0.05)
	assert_true(p.render_position.x < 43.2 and p.render_position.x > 42.0)
	p.update_render(0.1)
	assert_almost_eq(p.render_position.x, 42.0, 0.001)
	p.reconcile(Vector2(10.0, 40), 1, 4.0)  # error grande → salto
	assert_almost_eq(p.render_position.x, 10.0, 0.001)


func test_interpolation_buffer_lerps_between_snapshots() -> void:
	var b := InterpolationBuffer.new()
	b.push(0.0, Vector2(0, 0))
	b.push(100.0, Vector2(10, 0))
	b.push(200.0, Vector2(20, 0))
	assert_almost_eq(b.sample(250.0).x, 15.0, 0.001)  # render_time 150 → entre 100 y 200
	assert_almost_eq(b.sample(350.0).x, 25.0, 0.001)  # 250 → extrapola 50 ms
	assert_almost_eq(b.sample(600.0).x, 20.0, 0.001)  # > 100 ms sin datos → congela en el último
