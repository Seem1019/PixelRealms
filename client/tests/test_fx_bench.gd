extends GutTest
## HU-089 CA3: `/fxbench` mantiene 24 marcas y 40 números y calcula el FPS p5 con el 5 % de cuadros más lentos.


func test_p5_fps_uses_the_slowest_five_percent() -> void:
	var frames: Array[float] = []
	for i: int in 18:
		frames.append(10.0)
	frames.append(40.0)
	frames.append(40.0)
	assert_almost_eq(FxBench.p5_fps(frames), 25.0, 0.01, "1000 / 40 ms")
	assert_eq(FxBench.p5_fps([] as Array[float]), 0.0)


func test_bench_fills_the_screen_and_reports() -> void:
	var reticle := AoeReticle.new()
	var floating := FloatingText.new()
	add_child_autofree(reticle)
	add_child_autofree(floating)
	var bench := FxBench.new()
	bench.reticle = reticle
	bench.floating = floating
	bench.center = func() -> Vector2: return Vector2(200, 150)
	add_child_autofree(bench)
	bench.set_process(false)  # los cuadros los da el test
	watch_signals(bench)
	bench.start()
	bench._process(1.0 / 60.0)
	assert_eq(reticle._marks.size(), FxBench.MARKS)
	assert_eq(floating.visible_count(), FxBench.TEXTS)
	for i: int in ceili(FxBench.DURATION_SEC * 60.0):
		bench._process(1.0 / 60.0)
	assert_signal_emitted(bench, "finished")
	assert_false(bench.running)
	assert_eq(reticle._marks.size(), 0, "al acabar quita sus marcas")
	assert_almost_eq(float(get_signal_parameters(bench, "finished")[0]), 60.0, 0.5)
