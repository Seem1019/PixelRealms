extends GutTest
## HU-098: estados legibles — categoría y marco de cada aura, indicadores sobre la entidad y mini-íconos en la placa.

const WORLD := preload("res://scenes/world/world.tscn")


func test_categories_come_from_kind_and_is_debuff() -> void:
	assert_eq(AuraStyle.category(Content.aura("warrior_charge_stun")), "stun")
	assert_eq(AuraStyle.category(Content.aura("mage_frost_nova_root")), "root")
	assert_eq(AuraStyle.category(Content.aura("mage_chill")), "slow")
	assert_eq(AuraStyle.category(Content.aura("rogue_poison")), "dot")
	assert_eq(AuraStyle.category(Content.aura("priest_renew_hot")), "hot")
	assert_eq(AuraStyle.category(Content.aura("priest_power_shield_aura")), "shield")
	assert_eq(AuraStyle.category(Content.aura("rogue_sprint_aura")), "buff")
	assert_eq(AuraStyle.category({"kind": "stat_mod", "isDebuff": true, "mods": {"damagePct": -0.2}}), "debuff")
	assert_eq(AuraStyle.category({"kind": "stat_mod", "isDebuff": true, "mods": {"speedPct": -0.3}}), "slow")


func test_every_category_has_a_badge_and_a_spanish_name() -> void:
	for cat: String in AuraStyle.BADGES:
		assert_gt(AuraStyle.badge_pixels(cat).size(), 4, cat)
		assert_true(AuraStyle.CATEGORY_NAMES.has(cat), cat)


func test_frame_is_green_for_buffs_and_red_for_debuffs() -> void:
	assert_eq(AuraStyle.frame_color(Content.aura("priest_renew_hot")), AuraStyle.BUFF_FRAME)
	assert_eq(AuraStyle.frame_color(Content.aura("warrior_hamstring_slow")), AuraStyle.DEBUFF_FRAME)


func test_summary_flags_control_effects_and_orders_debuffs_first() -> void:
	var list := [{"auraId": "priest_renew_hot"}, {"auraId": "warrior_charge_stun"}, {"auraId": "mage_chill"}]
	var s := AuraStyle.summarize(list, Content.aura)
	assert_true(s["stunned"])
	assert_true(s["slowed"])
	assert_false(s["rooted"])
	assert_false(s["shielded"])
	var icons: Array = s["icons"]
	assert_eq(icons.size(), 3)
	assert_true(icons[0]["debuff"])
	assert_false(icons[2]["debuff"], "las beneficiosas al final")


func test_nameplate_makes_room_for_aura_icons() -> void:
	var plate := Nameplate.new()
	add_child_autofree(plate)
	plate.display_name = "Slime"
	var h := plate.plate_size().y
	plate.aura_icons = [{"icon": "spells/charge", "debuff": true, "category": "stun"}]
	assert_eq(plate.plate_size().y, h + Nameplate.AURA_ROW_H, "la fila de íconos cuenta para no pisarse con otras placas")
	var many: Array = []
	for i: int in 10:
		many.append({"icon": "", "debuff": false, "category": "buff"})
	plate.aura_icons = many
	assert_eq(plate.aura_icons.size(), Nameplate.MAX_AURA_ICONS)


func test_auras_show_on_any_visible_entity_and_clear_on_removal() -> void:
	Settings._config.set_value("net", "server_url", "http://127.0.0.1:1")
	GameState.pending_ticket = "t"
	GameState.pending_character_id = "c"
	var world: Node2D = WORLD.instantiate()
	add_child_autofree(world)
	await get_tree().process_frame
	Net.disconnect_from_server()
	var dispatch := func(type: String, d: Dictionary) -> void: Net._dispatch(JSON.stringify({"t": type, "d": d}))
	dispatch.call("Welcome", {
		"selfId": 1, "tick": 10, "tickRate": 20, "snapshotRate": 10, "mapId": "meadow",
		"self": {"x": 100.0, "y": 100.0, "level": 3, "xp": 0, "xpNext": 100, "hp": 80, "maxHp": 100, "res": 40, "maxRes": 60, "resource": "mana", "classId": "mage", "name": "Ana"},
		"inventory": [], "equipment": [], "hotbar": [], "knownSpells": [], "rulesHash": "x",
	})
	dispatch.call("EntitySpawn", {"id": 7, "kind": "monster", "templateId": "slime", "name": "Slime", "x": 140.0, "y": 100.0, "dir": "s", "level": 1, "hpPct": 100, "flags": 0})
	var v: EntityVisual = world._remotes[7].visual
	# Sin seleccionarlo: aturdido y ralentizado se ven sobre él.
	dispatch.call("AuraApplied", {"targetId": 7, "auraId": "warrior_charge_stun", "casterId": 1, "stacks": 1, "durationMs": 2000})
	dispatch.call("AuraApplied", {"targetId": 7, "auraId": "mage_chill", "casterId": 1, "stacks": 1, "durationMs": 4000})
	assert_true(v.status.stunned, "estrellas sobre la cabeza")
	assert_true(v.slowed, "tinte azul")
	assert_eq(v.plate.aura_icons.size(), 2)
	await get_tree().process_frame
	assert_eq(v.sprite.modulate, EntityVisual.SLOW_TINT)
	dispatch.call("AuraRemoved", {"targetId": 7, "auraId": "warrior_charge_stun", "casterId": 1})
	dispatch.call("AuraRemoved", {"targetId": 7, "auraId": "mage_chill", "casterId": 1})
	assert_false(v.status.stunned)
	assert_false(v.slowed)
	assert_eq(v.plate.aura_icons.size(), 0, "al terminar el aura desaparece su indicador")
	# Y el propio personaje también.
	dispatch.call("AuraApplied", {"targetId": 1, "auraId": "priest_power_shield_aura", "casterId": 2, "stacks": 1, "durationMs": 5000})
	assert_true(world._self_visual.status.shielded, "burbuja")
	GameState.auras.clear()
