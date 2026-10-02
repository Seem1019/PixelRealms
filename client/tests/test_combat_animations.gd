extends GutTest
## HU-090 (animaciones del cuerpo) y HU-091 (efectos de hechizo): prioridad de animaciones, respaldo a idle, hojas completas,
## tope de la reserva de efectos y que el número de un proyectil salga al llegar.


func test_priority_is_death_then_hurt_then_attack_or_cast_then_walk_then_idle() -> void:
	assert_eq(EntityVisual.pick_base(true, true, true, true, true), "death")
	assert_eq(EntityVisual.pick_base(false, true, true, true, true), "hurt")
	assert_eq(EntityVisual.pick_base(false, false, true, true, true), "attack", "moverse no corta el ataque")
	assert_eq(EntityVisual.pick_base(false, false, false, true, true), "cast")
	assert_eq(EntityVisual.pick_base(false, false, false, false, true), "walk")
	assert_eq(EntityVisual.pick_base(false, false, false, false, false), "idle")


func test_missing_animation_falls_back_to_idle() -> void:
	var sf := SpriteFrames.new()
	sf.add_animation("idle_s")
	assert_eq(EntityVisual.resolve_anim(sf, "attack", "s"), "idle_s")
	assert_eq(EntityVisual.resolve_anim(sf, "idle", "s"), "idle_s")


func test_every_class_and_monster_sheet_has_the_combat_animations() -> void:
	var refs: Array[String] = []
	for cls: Variant in Content.classes():
		refs.append(EntitySprites.ref_for("player", str((cls as Dictionary)["id"]), str((cls as Dictionary)["id"])))
	for m: Variant in Content._by_id.get("monsters", {}).values():
		refs.append(EntitySprites.ref_for("monster", str((m as Dictionary)["id"]), ""))
	for ref: String in refs:
		var sf := EntitySprites.frames_for(ref)
		assert_not_null(sf, ref)
		for anim: String in ["attack", "cast", "hurt", "death"]:
			for d: String in EntitySprites.DIRS:
				assert_true(sf.has_animation("%s_%s" % [anim, d]), "%s: %s_%s" % [ref, anim, d])
		assert_false(sf.get_animation_loop("attack_s"), "%s: el ataque es de un disparo" % ref)
		assert_false(sf.get_animation_loop("death_s"), "%s: la muerte se queda en el último cuadro" % ref)
		assert_true(sf.get_animation_loop("cast_s"), "%s: el casteo es un bucle" % ref)
		var ms := EntitySprites.anim_ms(ref, "attack")
		assert_between(ms, 200, 400, "%s: ataque de 3-4 cuadros a 12 fps" % ref)


func _visual(ref: String) -> EntityVisual:
	var v := EntityVisual.new()
	add_child_autofree(v)
	v.set_sprite(ref, Color.WHITE, "X")
	return v


func test_attack_faces_the_target_and_walking_does_not_cut_it() -> void:
	var v := _visual("characters/warrior")
	v.set_motion("s", false)
	v.play_attack(Vector2(-20, 3))
	assert_eq(v.sprite.animation, &"attack_e")
	assert_true(v.sprite.flip_h, "mira a la izquierda (oeste)")
	v.set_motion("n", true)
	assert_eq(v.current_base, "attack")
	assert_eq(v.sprite.animation, &"attack_e", "sigue mirando al objetivo")
	v.set("_attack_until_ms", Time.get_ticks_msec() - 1)
	v._process(0.016)
	assert_eq(v.sprite.animation, &"walk_n", "al terminar vuelve a caminar hacia donde va")


func test_cast_loops_until_it_ends_and_hurt_takes_priority() -> void:
	var v := _visual("characters/mage")
	v.begin_cast(Vector2(0, -10))
	assert_eq(v.sprite.animation, &"cast_n")
	v.hurt(Vector2(5, 0))
	assert_eq(v.current_base, "hurt")
	assert_true(v.is_flashing())
	v.set("_hurt_until_ms", Time.get_ticks_msec() - 1)
	v._process(0.016)
	assert_eq(v.sprite.animation, &"cast_n")
	v.end_cast()
	assert_eq(v.current_base, "idle")


func test_death_replaces_the_gray_body_and_a_corpse_starts_on_the_last_frame() -> void:
	var v := _visual("monsters/boar")
	v.set_dead(true, true)
	assert_eq(v.sprite.animation, &"death_s")
	assert_eq(v.sprite.frame, v.sprite.sprite_frames.get_frame_count("death_s") - 1)
	assert_eq(v.modulate, Color.WHITE, "ya no se pinta de gris")
	v.play_attack(Vector2.RIGHT)
	v.hurt(Vector2.RIGHT)
	assert_eq(v.current_base, "death", "un muerto no ataca ni se queja")
	v.set_dead(false)
	assert_eq(v.current_base, "idle")


func test_sheet_without_death_keeps_the_old_gray_body() -> void:
	var v := _visual("characters/warrior")
	var sf := SpriteFrames.new()
	sf.remove_animation("default")
	sf.add_animation("idle_s")
	sf.add_frame("idle_s", PlaceholderTexture2D.new())
	v.sprite.sprite_frames = sf
	v.set_dead(true)
	assert_eq(v.sprite.animation, &"idle_s")
	assert_ne(v.modulate, Color.WHITE)


func test_every_spell_projectile_cast_impact_and_area_sheet_exists() -> void:
	for s: Variant in Content._by_id.get("spells", {}).values():
		var spell: Dictionary = s
		assert_not_null(VfxLayer.frames_for(VfxCatalog.cast_sheet(spell)), "%s: brillo" % spell["id"])
		var proj := VfxCatalog.projectile_sheet(spell)
		if not proj.is_empty():
			var sf := VfxLayer.frames_for(proj)
			assert_not_null(sf, "%s: proyectil %s" % [spell["id"], proj])
			assert_eq(sf.get_animation_names().size(), 8, "8 direcciones, sin rotar")
		for kind: String in ["dmg", "heal"]:
			assert_not_null(VfxLayer.frames_for(VfxCatalog.impact_sheet(spell, kind, false)), "%s: impacto" % spell["id"])
		if VfxCatalog.is_area(spell):
			assert_not_null(VfxLayer.frames_for("area_%s" % VfxCatalog.element(spell)), "%s: área" % spell["id"])
			var pts := VfxCatalog.area_points(spell, Vector2(100, 100), Vector2(140, 100))
			assert_between(pts.size(), 1, VfxCatalog.AREA_MAX_BURSTS)
	for b: String in VfxCatalog.RANGED_BASIC.values():
		assert_not_null(VfxLayer.frames_for(b), b)


func test_vfx_pool_is_capped_drops_the_oldest_and_skips_offscreen() -> void:
	var layer := VfxLayer.new()
	add_child_autofree(layer)
	for i: int in 100:
		layer.play_once("impact_fire", Vector2(i, 0))
	assert_eq(layer.active_count(), VfxLayer.MAX_ACTIVE)
	var visible := 0
	for c: Node in layer.get_children():
		visible += int((c as CanvasItem).visible)
	assert_eq(visible, VfxLayer.MAX_ACTIVE, "nunca más nodos dibujándose que el tope")
	layer.clear_all()
	layer.view_rect = Rect2(0, 0, 480, 270)
	assert_false(layer.play_once("impact_fire", Vector2(2000, 2000)), "fuera de la vista no se crea")
	assert_eq(layer.active_count(), 0)
	var arrived := [false]
	layer.launch("fireball", Vector2(2000, 0), func() -> Vector2: return Vector2(2100, 0), 10, func() -> void: arrived[0] = true)
	await get_tree().create_timer(0.05).timeout
	assert_true(arrived[0], "un proyectil fuera de pantalla entrega igual su número")


func _presenter() -> Dictionary:
	var layer := VfxLayer.new()
	add_child_autofree(layer)
	var floating := FloatingText.new()
	add_child_autofree(floating)
	var a := _visual("characters/mage")
	var b := _visual("monsters/wolf")
	var p := CombatPresenter.new()
	add_child_autofree(p)
	p.vfx = layer
	p.floating = floating
	p.entity_pos = func(id: int) -> Vector2: return {1: Vector2(100, 100), 2: Vector2(180, 100)}.get(id, Vector2.INF)
	p.visual_of = func(id: int) -> EntityVisual: return {1: a, 2: b}.get(id)
	p.archetype_of = func(id: int) -> String: return "mage" if id == 1 else "wolf"
	return {"p": p, "floating": floating, "a": a, "b": b, "vfx": layer}


func test_ranged_basic_attack_flies_and_shows_the_number_on_arrival() -> void:
	var t := _presenter()
	var p: CombatPresenter = t["p"]
	var floating: FloatingText = t["floating"]
	p.combat_events({"e": [{"src": 1, "dst": 2, "kind": "dmg", "amount": 7, "crit": false, "school": "magic"}]})
	assert_eq((t["a"] as EntityVisual).current_base, "attack", "el src ataca con el evento")
	assert_eq(floating.get("_active").size(), 0, "el número espera al proyectil")
	assert_false((t["b"] as EntityVisual).is_flashing())
	await get_tree().create_timer(0.3).timeout
	assert_eq(floating.get("_active").size(), 1, "llegó: número")
	assert_eq((t["b"] as EntityVisual).current_base, "hurt")


func test_cast_glow_attack_on_release_and_miss_does_not_hurt() -> void:
	var t := _presenter()
	var p: CombatPresenter = t["p"]
	var layer: VfxLayer = t["vfx"]
	p.cast_started({"casterId": 1, "spellId": "mage_fireball", "targetId": 2, "durationMs": 2000})
	assert_eq((t["a"] as EntityVisual).current_base, "cast")
	assert_eq(layer.active_count(), 1, "brillo bajo los pies")
	p.cast_ended({"casterId": 1, "spellId": "mage_fireball", "result": "done"})
	assert_eq((t["a"] as EntityVisual).current_base, "attack")
	assert_eq(layer.active_count(), 1, "se apaga el brillo y sale la bola de fuego")
	p.combat_events({"e": [{"src": 1, "dst": 2, "spellId": "mage_fireball", "kind": "miss", "amount": 0, "crit": false, "school": "magic"}]})
	await get_tree().create_timer(0.5).timeout
	assert_ne((t["b"] as EntityVisual).current_base, "hurt", "un fallo no es un golpe")
	assert_false((t["b"] as EntityVisual).is_flashing())


func test_interrupted_cast_does_not_attack() -> void:
	var t := _presenter()
	var p: CombatPresenter = t["p"]
	p.cast_started({"casterId": 1, "spellId": "mage_fireball", "targetId": 2, "durationMs": 2000})
	p.cast_ended({"casterId": 1, "spellId": "mage_fireball", "result": "interrupted"})
	assert_eq((t["a"] as EntityVisual).current_base, "idle")
	assert_eq((t["vfx"] as VfxLayer).active_count(), 0)
