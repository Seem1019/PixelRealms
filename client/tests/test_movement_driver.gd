extends GutTest
## Punto 1 de la prueba de juego (saltos hacia atrás): el movimiento predicho debe coincidir con un servidor de 20 Hz
## a cualquier frecuencia de frames. Modelo de servidor: aplica el último MoveInput recibido antes de cada tick, mueve un
## MovementStep por tick y manda un Snapshot cada 2 ticks con ackSeq = último seq aplicado; latencia en ambos sentidos.

const LATENCY_MS := 30.0


func _simulate(frame_ms: float, duration_ms: float, jitter_ms: float = 0.0, latency_ms: float = LATENCY_MS) -> Dictionary:
	var rng := RandomNumberGenerator.new()
	rng.seed = 7
	var grid := CollisionGrid.new(400, 20)
	var pred := Prediction.new()
	pred.setup(grid, Vector2(40, 40), 4.0)
	var driver := MovementDriver.new()
	var server_pos := Vector2(40, 40)
	var server_seq := 0
	var server_dx := 0
	var to_server: Array[Dictionary] = []
	var to_client: Array[Dictionary] = []
	var next_frame := 0.0
	var next_tick := 0.0
	var ticks := 0
	var corrections := 0
	var max_error := 0.0
	while minf(next_frame, next_tick) < duration_ms:
		if next_tick <= next_frame:
			var arrived: Array[Dictionary] = []
			for m: Dictionary in to_server:
				if float(m["at"]) <= next_tick:
					arrived.append(m)
			for m: Dictionary in arrived:
				to_server.erase(m)
				if int(m["seq"]) > server_seq:
					server_seq = int(m["seq"])
					server_dx = int(m["dx"])
			server_pos = MovementStep.step(server_pos.x, server_pos.y, server_dx, 0, 4.0, grid)
			ticks += 1
			if ticks % 2 == 0:
				to_client.append({"at": next_tick + latency_ms + rng.randf_range(-jitter_ms, jitter_ms), "pos": server_pos, "ack": server_seq})
			next_tick += 50.0
		else:
			var delivered: Array[Dictionary] = []
			for s: Dictionary in to_client:
				if float(s["at"]) <= next_frame:
					delivered.append(s)
			for s: Dictionary in delivered:
				to_client.erase(s)
				pred.reconcile(s["pos"], int(s["ack"]), 4.0)
				max_error = maxf(max_error, pred.last_error_px)
				if pred.last_error_px >= Prediction.SNAP_THRESHOLD_PX:
					corrections += 1
			for inp: Dictionary in driver.advance(frame_ms / 1000.0, 1, 0, pred):
				var m := inp.duplicate()
				m["at"] = next_frame + latency_ms + rng.randf_range(-jitter_ms, jitter_ms)
				to_server.append(m)
			next_frame += frame_ms
	return {"corrections": corrections, "max_error": max_error, "client_x": pred.position.x, "server_x": server_pos.x}


func test_60fps_prediction_does_not_snap_back() -> void:
	var r := _simulate(1000.0 / 60.0, 3000.0)
	assert_eq(int(r["corrections"]), 0, "correcciones ≥ 2 px (error máx. %.1f px)" % float(r["max_error"]))


func test_144fps_prediction_does_not_snap_back() -> void:
	var r := _simulate(1000.0 / 144.0, 3000.0)
	assert_eq(int(r["corrections"]), 0, "correcciones ≥ 2 px (error máx. %.1f px)" % float(r["max_error"]))


func test_speed_does_not_depend_on_frame_rate() -> void:
	# 1 s a 4 casillas/s = 64 px, a 30, 60 y 144 fps (un tick de margen por el desfase del acumulador).
	for fps: float in [30.0, 60.0, 144.0]:
		var pred := Prediction.new()
		pred.setup(CollisionGrid.new(400, 20), Vector2(40, 40), 4.0)
		var driver := MovementDriver.new()
		var t := 0.0
		while t < 1000.0:
			driver.advance(1.0 / fps, 1, 0, pred)
			t += 1000.0 / fps
		assert_almost_eq(pred.position.x - 40.0, 64.0, 3.3, "a %d fps" % int(fps))


func test_stopping_sends_one_zero_input_and_nothing_more() -> void:
	var pred := Prediction.new()
	pred.setup(CollisionGrid.new(400, 20), Vector2(40, 40), 4.0)
	var driver := MovementDriver.new()
	driver.advance(0.05, 1, 0, pred)
	var stop := driver.advance(0.05, 0, 0, pred)
	assert_eq(stop.size(), 1)
	assert_eq(int(stop[0]["dx"]), 0)
	var idle := 0
	for i: int in 10:
		idle += driver.advance(0.05, 0, 0, pred).size()
	assert_eq(idle, 0, "quieto no se envía nada")


func test_10ms_jitter_does_not_snap_back() -> void:
	# Con más variación (±20 ms) caen a veces dos inputs en el mismo tick del servidor: corrección de un paso (3,2 px).
	var r := _simulate(1000.0 / 60.0, 10000.0, 10.0)
	assert_eq(int(r["corrections"]), 0, "correcciones ≥ 2 px (error máx. %.1f px)" % float(r["max_error"]))


func test_150ms_round_trip_does_not_snap_back() -> void:
	# HU-022 CA2: 150 ms de ida y vuelta (75 ms en cada sentido) con algo de variación; el jugador propio no tironea.
	var r := _simulate(1000.0 / 60.0, 10000.0, 5.0, 75.0)
	assert_eq(int(r["corrections"]), 0, "correcciones ≥ 2 px (error máx. %.1f px)" % float(r["max_error"]))


func test_own_cast_predicts_at_cast_move_speed_and_restores_it() -> void:
	# HU-022 CA4b: desde mi CastStarted hasta CastEnded la predicción va a velocidad · castMoveSpeedMult, sin esperar al Snapshot.
	var pred := Prediction.new()
	pred.setup(CollisionGrid.new(400, 20), Vector2(40, 40), 4.0)
	pred.set_casting(true, 0.5)
	assert_almost_eq(pred.speed_tiles_per_sec, 2.0, 0.001)
	pred.set_casting(true, 0.5)  # repetido: no se aplica dos veces
	assert_almost_eq(pred.speed_tiles_per_sec, 2.0, 0.001)
	for i: int in 10:
		pred.apply_input(i + 1, 1, 0)
	assert_almost_eq(pred.position.x, 56.0, 0.01, "igual que el vector casting_half_speed_east_10_ticks")
	pred.set_casting(false, 0.5)
	assert_almost_eq(pred.speed_tiles_per_sec, 4.0, 0.001)
