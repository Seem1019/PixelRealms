extends GutTest
## Rediseño visual (ADR-026): lo que vimos mal en las capturas del prototipo y cómo queda. El mapa se hornea sin cambiar
## la colisión, todo el contenido encuentra su sprite e ícono, las placas de nombre no se pisan ni se salen, el detalle del
## libro de hechizos sale al lado de la lista y el diálogo del NPC queda dentro de los 480×270.

const WORLD := preload("res://scenes/world/world.tscn")


func test_baking_the_map_keeps_the_collision_and_fills_both_layers() -> void:
	var map := TmjMap.load_from("res://maps/test_small.tmj")
	var solid_before: Array[bool] = []
	for y: int in map.height:
		for x: int in map.width:
			solid_before.append(map.collision.is_solid(x, y))
	var baked := TerrainBaker.bake(map)
	assert_false(baked.is_empty(), "falta assets/tiles/terrain.png")
	var ground: Image = baked["ground"]
	assert_eq(ground.get_size(), Vector2i(map.width * 16, map.height * 16))
	assert_false(ground.is_invisible(), "el suelo se dibuja entero")
	var i := 0
	for y: int in map.height:
		for x: int in map.width:
			assert_eq(map.collision.is_solid(x, y), solid_before[i], "la colisión no depende del dibujo (%d,%d)" % [x, y])
			i += 1


func test_every_class_monster_and_npc_has_a_sprite_sheet() -> void:
	for cls: Variant in Content.classes():
		var id := str((cls as Dictionary)["id"])
		assert_not_null(EntitySprites.frames_for(EntitySprites.ref_for("player", id, id)), "clase %s" % id)
	for m: Variant in Content._by_id.get("monsters", {}).values():
		var md: Dictionary = m
		var frames := EntitySprites.frames_for(EntitySprites.ref_for("monster", str(md["id"]), ""))
		assert_not_null(frames, "monstruo %s" % md["id"])
		if frames != null:
			for anim: String in ["idle_s", "idle_n", "idle_e", "walk_s", "walk_n", "walk_e"]:
				assert_true(frames.has_animation(anim), "%s: %s" % [md["id"], anim])
	for npc: String in EntitySprites.NPC_SPRITES.keys():
		assert_not_null(EntitySprites.frames_for(EntitySprites.ref_for("npc", npc, "")), "npc %s" % npc)


func test_every_item_spell_and_aura_icon_exists() -> void:
	for kind: String in ["items", "spells", "auras"]:
		for e: Variant in Content._by_id.get(kind, {}).values():
			var ed: Dictionary = e
			if ed.get("icon") != null:
				assert_not_null(UiTheme.icon(str(ed["icon"])), "%s %s → %s" % [kind, ed["id"], ed["icon"]])


func test_close_nameplates_stack_instead_of_overlapping() -> void:
	var view := Rect2(0, 0, 480, 270)
	var a := Rect2(200, 100, 60, 14)
	var b := Rect2(210, 104, 64, 14)  # "Maestro Aldo" sobre "DiegodADMIN"
	var offsets := NameplateLayout.solve([{"id": 1, "rect": a}, {"id": 2, "rect": b}] as Array[Dictionary], view)
	var ra := Rect2(a.position + (offsets[1] as Vector2), a.size)
	var rb := Rect2(b.position + (offsets[2] as Vector2), b.size)
	assert_false(ra.intersects(rb), "%s y %s se pisan" % [ra, rb])


func test_nameplate_at_the_screen_edge_is_kept_inside() -> void:
	var view := Rect2(100, 100, 480, 270)
	var plate := Rect2(540, 150, 90, 14)  # "Lobo de las colinas" cortado por el borde derecho
	var offsets := NameplateLayout.solve([{"id": 9, "rect": plate}] as Array[Dictionary], view)
	var moved := Rect2(plate.position + (offsets[9] as Vector2), plate.size)
	assert_true(view.encloses(moved), "%s se sale de %s" % [moved, view])
	var hidden := NameplateLayout.solve([{"id": 3, "rect": Rect2(900, 150, 40, 14)}] as Array[Dictionary], view)
	assert_eq(hidden[3], Vector2.ZERO, "una entidad fuera de la vista no trae su nombre a la pantalla")


func _world() -> Node2D:
	Settings._config.set_value("net", "server_url", "http://127.0.0.1:1")
	GameState.pending_ticket = "t"
	GameState.pending_character_id = "c"
	var w := WORLD.instantiate() as Node2D
	add_child_autofree(w)
	await get_tree().process_frame
	Net.disconnect_from_server()
	Net._dispatch(JSON.stringify({"t": "Welcome", "d": {
		"selfId": 1, "tick": 10, "tickRate": 20, "snapshotRate": 10, "mapId": "meadow",
		"self": {"x": 300.0, "y": 800.0, "level": 5, "xp": 0, "xpNext": 100, "hp": 80, "maxHp": 100, "res": 40, "maxRes": 60, "resource": "mana", "classId": "mage", "name": "Ana"},
		"inventory": [], "equipment": [], "hotbar": [], "knownSpells": ["mage_fireball"], "rulesHash": "x"}}))
	for i: int in 3:
		await get_tree().process_frame
	return w


func test_spellbook_rows_show_full_names_and_the_detail_goes_beside_the_list() -> void:
	var w := await _world()
	var book: SpellbookWindow = w.get("_spellbook")
	book.toggle()
	await get_tree().process_frame
	await get_tree().process_frame
	var list: VBoxContainer = book.get("_list")
	assert_gt(list.get_child_count(), 3)
	for row: Node in list.get_children():
		var entry := row as SpellbookWindow.SpellEntry
		var spell := Content.spell(entry.spell_id)
		assert_string_contains(entry.row_text(), str(spell["name"]), "antes «Carrera (Nive»")
		assert_string_contains(entry.row_text(), "Nv %d" % int(spell.get("levelReq", 1)))
	var base := UiTheme.base_size()
	assert_true(book.get_global_rect().end.y <= base.y - CombatHud.HOTBAR_HEIGHT - UiTheme.SCREEN_MARGIN, "no tapa la barra rápida")
	var entry := list.get_child(2) as SpellbookWindow.SpellEntry
	book.call("_show_tip", entry, entry.tooltip_bbcode)
	await get_tree().process_frame
	var tip: Control = book.get("_tip")
	assert_true(tip.visible)
	assert_false(tip.get_global_rect().intersects(book.get_global_rect()), "el detalle tapaba la lista")
	assert_true(Rect2(Vector2.ZERO, base).encloses(tip.get_global_rect()))


func test_npc_dialog_stays_inside_the_screen() -> void:
	var w := await _world()
	var social: SocialPanels = w.get("_social")
	social.open_class_change(21)
	for i: int in 3:
		await get_tree().process_frame
	var dialog: Control = social.get("_class_window")
	var screen := Rect2(Vector2.ZERO, UiTheme.base_size())
	assert_true(screen.encloses(dialog.get_global_rect()), "%s fuera de la pantalla (antes salía por arriba a la izquierda)" % dialog.get_global_rect())
	assert_true(dialog.get_global_rect().position.y >= CombatHud.FRAMES_BOTTOM, "no tapa los marcos de vida")


func test_server_error_is_a_framed_toast_that_goes_away() -> void:
	var w := await _world()
	var hud: CombatHud = w.get("_hud")
	hud.show_error("Aún no está listo")
	var toast: Control = hud.get("_toast")
	assert_true(toast.visible)
	assert_eq((hud.get("_error_label") as Label).text, "Aún no está listo")
	hud.set("_error_until", Time.get_ticks_msec() / 1000.0 - 0.1)
	hud._process(0.016)
	assert_false(toast.visible, "desaparece sola")


func test_unit_frames_show_values_and_hotbar_uses_icons() -> void:
	var w := await _world()
	var hud: CombatHud = w.get("_hud")
	assert_eq((hud.get("_self_hp_text") as Label).text, "80/100")
	assert_eq((hud.get("_self_res_text") as Label).text, "40/60")
	GameState.hotbar = [{"slot": 0, "kind": "spell", "ref": "mage_fireball"}]
	hud._refresh_hotbar()
	var slot: CombatHud.HotSlot = (hud.get("_slots") as Array)[0]
	assert_not_null(slot.icon_texture(), "antes salía la palabra «Bola de fuego»")
