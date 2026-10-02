extends GutTest
## HU-099: hojas HD de los héroes (pixelScale 3) — mismo tamaño en el mundo, más detalle en pantalla.


func test_hero_sheets_are_hd_with_the_full_animation_set() -> void:
	for cls: String in ["warrior", "mage", "priest"]:
		var ref := "characters/" + cls
		assert_eq(EntitySprites.scale_of(ref), 3, ref)
		var frames := EntitySprites.frames_for(ref)
		assert_not_null(frames, ref)
		for anim: String in ["idle", "walk", "attack", "cast", "hurt", "death"]:
			for d: String in EntitySprites.DIRS:
				assert_true(frames.has_animation("%s_%s" % [anim, d]), "%s %s_%s" % [ref, anim, d])
		assert_eq(frames.get_frame_count("walk_s"), 8, "más cuadros que la hoja procedural (4)")


func test_hd_sprite_keeps_its_world_size() -> void:
	var v := EntityVisual.new()
	add_child_autofree(v)
	v.set_sprite("characters/priest", Color.WHITE, "Lumen")
	assert_eq(v.sprite.scale, Vector2.ONE / 3.0)
	assert_eq(v.body_height, 24.0, "la placa a la misma altura que un personaje de 32×32")
	assert_eq(v.sprite.position, Vector2(-20, -36), "pies en el origen: cuadro de 40 lógicos, pies a 36")


func test_hd_portraits_cover_the_same_logical_area() -> void:
	var body := EntitySprites.portrait("characters/priest", false)
	var head := EntitySprites.portrait("characters/priest", true)
	assert_eq(body.get_size(), Vector2(96, 96), "32×32 lógicos a ×3")
	assert_eq(head.get_size(), Vector2(48, 48), "16×16 lógicos a ×3")
	assert_eq(EntitySprites.scale_of("characters/rogue"), 1, "una hoja procedural sigue a escala 1")
