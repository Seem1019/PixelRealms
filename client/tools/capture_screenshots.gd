extends Node
## Capturas del rediseño visual (docs/screenshots/redesign/) y del combate y el menú (docs/screenshots/combat/, HU-090,
## HU-091, HU-015). Sin servidor: instancia las escenas y les inyecta mensajes como
## los tests. Necesita render (no --headless):
##   xvfb-run -a -s "-screen 0 1440x810x24" godot --path client --rendering-driver opengl3 -s res://tools/screenshots.gd
## Argumento opcional tras `--`: nombres de captura separados por comas (p. ej. `-- world_hud,npc_dialog`).
## (tools/screenshots.gd arranca este nodo cuando ya existen los autoloads.)

const OUT := "res://../docs/screenshots/redesign/"
const OUT_COMBAT := "res://../docs/screenshots/combat/"
const COMBAT_SHOTS := ["status_effects", "range_ring", "attack_classes", "monster_attacks", "cast_glow", "projectile", "impact", "area_resolve", "dead_monster", "esc_menu", "logout_in_combat"]
const WORLD := "res://scenes/world/world.tscn"

var _only: PackedStringArray = []


func _ready() -> void:
	var args := OS.get_cmdline_user_args()
	if not args.is_empty():
		_only = args[0].split(",")
	await get_tree().process_frame
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(OUT))
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(OUT_COMBAT))
	await _login()
	await _character_select()
	await _world_shots()
	await _combat_shots()
	get_tree().quit()


func _wanted(name: String) -> bool:
	return _only.is_empty() or _only.has(name)


func _shot(name: String, dir: String = OUT, settle_frames: int = 4) -> Image:
	for i: int in settle_frames:
		await get_tree().process_frame
	await RenderingServer.frame_post_draw
	var img := get_tree().root.get_texture().get_image()
	img.save_png(ProjectSettings.globalize_path(dir + name + ".png"))
	print("captura: ", name, " ", img.get_size())
	return img


func _login() -> void:
	if not _wanted("login"):
		return
	var scene := (load("res://scenes/login/login.tscn") as PackedScene).instantiate()
	get_tree().root.add_child(scene)
	(scene.get_node("%Username") as LineEdit).text = "DiegoAG"
	await _shot("login")
	scene.queue_free()
	await get_tree().process_frame


func _character_select() -> void:
	if not _wanted("character_select"):
		return
	var scene := (load("res://scenes/character_select/character_select.tscn") as PackedScene).instantiate()
	get_tree().root.add_child(scene)
	await get_tree().process_frame
	scene.call("show_characters", [
		{"id": "a", "name": "Ana", "classId": "mage", "level": 5},
		{"id": "b", "name": "DiegodADMIN", "classId": "warrior", "level": 6},
		{"id": "c", "name": "Sombra", "classId": "rogue", "level": 3},
		{"id": "d", "name": "Lumen", "classId": "priest", "level": 1},
	])
	(scene.get_node("%Status") as Label).text = ""
	await _shot("character_select")
	scene.queue_free()
	await get_tree().process_frame


func _dispatch(type: String, d: Dictionary) -> void:
	Net._dispatch(JSON.stringify({"t": type, "d": d}))


func _new_world(x: float, y: float) -> Node2D:
	Settings._config.set_value("net", "server_url", "http://127.0.0.1:1")
	GameState.pending_ticket = "t"
	GameState.pending_character_id = "c"
	var world := (load(WORLD) as PackedScene).instantiate() as Node2D
	get_tree().root.add_child(world)
	await get_tree().process_frame
	Net.disconnect_from_server()
	await get_tree().process_frame
	_dispatch("Welcome", {
		"selfId": 1, "tick": 10, "tickRate": 20, "snapshotRate": 10, "mapId": "meadow",
		"self": {"x": x, "y": y, "level": 5, "xp": 340, "xpNext": 600, "hp": 132, "maxHp": 160, "res": 70, "maxRes": 110, "resource": "mana", "classId": "mage", "name": "Ana"},
		"inventory": [
			{"id": "i1", "templateId": "minor_healing_potion", "qty": 12}, {"id": "i2", "templateId": "bread", "qty": 5},
			{"id": "i3", "templateId": "iron_sword", "qty": 1}, {"id": "i4", "templateId": "wolf_tooth_necklace", "qty": 1},
			{"id": "i5", "templateId": "slime_goo", "qty": 7}, null, {"id": "i6", "templateId": "worn_dagger", "qty": 1},
			{"id": "i7", "templateId": "recruit_mail_shirt", "qty": 1}, {"id": "i8", "templateId": "minor_mana_potion", "qty": 3},
		],
		"equipment": [{"id": "e1", "templateId": "apprentice_hood", "qty": 1}, null, {"id": "e2", "templateId": "novice_robe", "qty": 1},
			null, null, {"id": "e3", "templateId": "boar_hide_boots", "qty": 1}, null, {"id": "e4", "templateId": "novice_wand", "qty": 1}, null],
		"hotbar": [{"slot": 0, "kind": "spell", "ref": "mage_fireball"}, {"slot": 1, "kind": "spell", "ref": "mage_frostbolt"},
			{"slot": 2, "kind": "spell", "ref": "mage_flame_burst"}, {"slot": 3, "kind": "spell", "ref": "mage_frost_nova"},
			{"slot": 4, "kind": "item", "ref": "minor_healing_potion"}, {"slot": 5, "kind": "item", "ref": "bread"},
			{"slot": 7, "kind": "item", "ref": "minor_mana_potion"}],
		"knownSpells": ["mage_fireball", "mage_frostbolt", "mage_flame_burst", "mage_frost_nova", "mage_blink"], "rulesHash": "x",
	})
	_dispatch("StatsUpdate", {"stats": {"str": 6, "agi": 8, "int": 21, "spi": 14, "sta": 12},
		"derived": {"attackPower": 6.0, "spellPower": 24.5, "critChance": 0.062, "dodgeChance": 0.041, "armor": 18, "mitigation": 0.09, "haste": 1.0},
		"maxHp": 160, "maxRes": 110})
	var cam := world.get("_camera") as Camera2D
	cam.position_smoothing_enabled = false  # la captura no espera a que la cámara llegue deslizándose
	cam.make_current()  # la cámara de la escena anterior ya no existe
	await get_tree().process_frame
	cam.reset_smoothing()
	return world


func _snapshot(at: Vector2, ents: Array, hp: int = 132) -> void:
	_dispatch("Snapshot", {"tick": 20, "ackSeq": 0, "self": {"x": at.x, "y": at.y, "speed": 4.0, "hp": hp, "maxHp": 160, "res": 70, "maxRes": 110}, "ents": ents})


func _world_shots() -> void:
	var names := ["world_hud", "character_and_bag", "spellbook_tooltip", "npc_dialog", "area_spell", "village"]
	var any := false
	for n: String in names:
		any = any or _wanted(n)
	if not any:
		return
	if _wanted("village") or _wanted("npc_dialog") or _wanted("character_and_bag"):
		var w := await _new_world(360, 800)
		_dispatch("EntitySpawn", {"id": 20, "kind": "npc", "templateId": "vendor", "name": "Marta la tendera", "x": 272.0, "y": 768.0, "dir": "s", "level": 1, "hpPct": 100, "flags": 0})
		_dispatch("EntitySpawn", {"id": 21, "kind": "npc", "templateId": "class_change", "name": "Maestro Aldo", "x": 448.0, "y": 768.0, "dir": "s", "level": 1, "hpPct": 100, "flags": 0})
		_dispatch("EntitySpawn", {"id": 22, "kind": "player", "templateId": "warrior", "name": "DiegodADMIN", "x": 452.0, "y": 774.0, "dir": "w", "level": 6, "classId": "warrior", "hpPct": 100, "flags": 0})
		_dispatch("EntitySpawn", {"id": 23, "kind": "player", "templateId": "priest", "name": "Lumen", "x": 330.0, "y": 830.0, "dir": "e", "level": 4, "classId": "priest", "hpPct": 80, "flags": 0})
		await get_tree().process_frame
		if _wanted("village"):
			await _shot("village")
		if _wanted("character_and_bag"):
			w.get("_character").call("toggle")
			w.get("_inventory").call("toggle")
			await _shot("character_and_bag")
			w.get("_character").call("toggle")
			w.get("_inventory").call("toggle")
		if _wanted("npc_dialog"):
			w.get("_social").call("open_class_change", 21)
			for i: int in 3:
				await get_tree().process_frame
			var cw := w.get("_social").get("_class_window") as Control
			print("dialogo: pos ", cw.position, " size ", cw.size, " min ", cw.get_combined_minimum_size())
			await _shot("npc_dialog")
		w.queue_free()
		await get_tree().process_frame
	if _wanted("world_hud") or _wanted("area_spell") or _wanted("spellbook_tooltip"):
		var w := await _new_world(780, 640)
		_dispatch("EntitySpawn", {"id": 7, "kind": "monster", "templateId": "slime", "name": "Slime", "x": 830.0, "y": 610.0, "dir": "w", "level": 1, "hpPct": 100, "flags": 0})
		_dispatch("EntitySpawn", {"id": 8, "kind": "monster", "templateId": "slime", "name": "Slime", "x": 860.0, "y": 650.0, "dir": "w", "level": 2, "hpPct": 100, "flags": 0})
		_dispatch("EntitySpawn", {"id": 9, "kind": "monster", "templateId": "wolf", "name": "Lobo de las colinas", "x": 745.0, "y": 560.0, "dir": "e", "level": 4, "hpPct": 100, "flags": 0})
		_dispatch("EntitySpawn", {"id": 10, "kind": "monster", "templateId": "boar", "name": "Jabalí", "x": 700.0, "y": 690.0, "dir": "e", "level": 2, "hpPct": 100, "flags": 0})
		_dispatch("EntitySpawn", {"id": 11, "kind": "player", "templateId": "rogue", "name": "Sombra", "x": 810.0, "y": 680.0, "dir": "n", "level": 3, "classId": "rogue", "hpPct": 45, "flags": 0})
		await get_tree().process_frame
		_snapshot(Vector2(780, 640), [{"id": 7, "x": 830.0, "y": 610.0, "dir": "w", "hpPct": 62, "anim": "idle"}, {"id": 9, "x": 745.0, "y": 560.0, "dir": "e", "hpPct": 100, "anim": "idle"}])
		w.call("_select", 7)
		_dispatch("AuraApplied", {"targetId": 7, "auraId": "mage_chill", "casterId": 1, "stacks": 1, "durationMs": 6000})
		_dispatch("AuraApplied", {"targetId": 1, "auraId": "bread_hot", "casterId": 1, "stacks": 1, "durationMs": 60000})
		if _wanted("world_hud"):
			_dispatch("CombatEvents", {"tick": 21, "e": [{"src": 1, "dst": 7, "spellId": "mage_fireball", "kind": "dmg", "amount": 23, "crit": false, "school": "magic"},
				{"src": 1, "dst": 9, "spellId": "mage_fireball", "kind": "dmg", "amount": 41, "crit": true, "school": "magic"}]})
			_dispatch("XpGain", {"amount": 12, "sourceId": 7})
			_dispatch("CastStarted", {"casterId": 1, "spellId": "mage_frostbolt", "targetId": 7, "durationMs": 2000})
			(w.get("_hud") as CombatHud).show_error("Aún no está listo")
			(w.get("_chat") as ChatPanel).add_message("say", "Sombra", "¡Cuidado con el lobo!")
			(w.get("_chat") as ChatPanel).add_message("party", "Lumen", "Voy a curar")
			await _shot("world_hud")
		if _wanted("area_spell"):
			_dispatch("CastStarted", {"casterId": 9, "spellId": "foreman_slam", "targetPos": {"x": 760.0, "y": 600.0}, "durationMs": 1500})
			w.call("_start_aiming", Content.spell("mage_flame_burst"))
			var reticle := w.get("_reticle") as AoeReticle
			w.set_process(false)
			reticle.aim_pos = Vector2(845, 630)
			reticle.aim_in_range = true
			w.call("_update_area_preview")
			await _shot("area_spell")
			w.set_process(true)
			w.call("_stop_aiming")
		if _wanted("spellbook_tooltip"):
			var book := w.get("_spellbook") as SpellbookWindow
			book.toggle()
			await get_tree().process_frame
			var list := book.get("_list") as VBoxContainer
			book.call("_show_tip", list.get_child(2))
			await _shot("spellbook_tooltip")
		w.queue_free()
		await get_tree().process_frame


# --- Combate (HU-090, HU-091) y menú (HU-015): docs/screenshots/combat/ -----------------------------------------------------

func _wait_ms(ms: int) -> void:
	await get_tree().create_timer(ms / 1000.0).timeout


## Recorte ampliado alrededor de un punto del mundo (para ver la animación de cerca).
func _crop(img: Image, w: Node2D, world_pos: Vector2, name: String, half: Vector2 = Vector2(36, 30)) -> void:
	var scale := float(img.get_width()) / UiTheme.base_size().x
	var center := (w.get_viewport().get_canvas_transform() * world_pos) * scale
	var r := Rect2i(Vector2i((center - half * scale).round()), Vector2i((half * 2.0 * scale).round()))
	r = r.intersection(Rect2i(Vector2i.ZERO, img.get_size()))
	if r.size.x <= 0 or r.size.y <= 0:
		return
	var part := img.get_region(r)
	part.resize(part.get_width() * 2, part.get_height() * 2, Image.INTERPOLATE_NEAREST)
	part.save_png(ProjectSettings.globalize_path(OUT_COMBAT + name + ".png"))
	print("recorte: ", name)


func _spawn(id: int, kind: String, template: String, name: String, at: Vector2, dir: String, level: int = 3, hp_pct: int = 100) -> void:
	var d := {"id": id, "kind": kind, "templateId": template, "name": name, "x": at.x, "y": at.y, "dir": dir, "level": level, "hpPct": hp_pct, "flags": 0}
	if kind == "player":
		d["classId"] = template
	_dispatch("EntitySpawn", d)


func _hit(src: int, dst: int, amount: int, spell: Variant = null, kind: String = "dmg", crit: bool = false) -> Dictionary:
	return {"src": src, "dst": dst, "spellId": spell, "kind": kind, "amount": amount, "crit": crit, "school": "physical" if spell == null else "magic"}


func _combat_shots() -> void:
	var any := false
	for n: String in COMBAT_SHOTS:
		any = any or _wanted(n)
	if not any:
		return
	var self_at := Vector2(780, 640)
	var w := await _new_world(self_at.x, self_at.y)
	await _wait_ms(100)
	w.set("_zone_fade_left", 0.0)  # el rótulo "Campos" tapaba el centro de las capturas
	(w.get("_zone_group") as CanvasGroup).self_modulate.a = 0.0
	if _wanted("status_effects"):
		# HU-098: estados sobre cada entidad sin seleccionarla (aturdido, inmovilizado, ralentizado, escudo, curación).
		var states := [[40, "slime", "Slime", Vector2(700, 600), "warrior_charge_stun"], [41, "boar", "Jabalí", Vector2(860, 600), "mage_frost_nova_root"],
			[42, "wolf", "Lobo", Vector2(700, 690), "mage_chill"], [43, "priest", "Lumen", Vector2(860, 690), "priest_power_shield_aura"]]
		for e: Array in states:
			_spawn(int(e[0]), "player" if e[1] == "priest" else "monster", str(e[1]), str(e[2]), e[3] as Vector2, "w", 3)
			_dispatch("AuraApplied", {"targetId": e[0], "auraId": e[4], "casterId": 1, "stacks": 1, "durationMs": 8000})
		_dispatch("AuraApplied", {"targetId": 43, "auraId": "priest_renew_hot", "casterId": 43, "stacks": 1, "durationMs": 8000})
		_dispatch("AuraApplied", {"targetId": 40, "auraId": "rogue_poison", "casterId": 1, "stacks": 1, "durationMs": 8000})
		await _wait_ms(150)
		var img := await _shot("status_effects", OUT_COMBAT, 2)
		for e: Array in states:
			_crop(img, w, (e[3] as Vector2) + Vector2(0, -14), "status_" + str(e[1]), Vector2(30, 26))
		await _clear(w)
	if _wanted("range_ring"):
		# HU-096: mantener Espacio muestra el alcance del básico (varita, 7 casillas); el objetivo está fuera: rojo.
		_spawn(44, "monster", "slime", "Slime", self_at + Vector2(140, 0), "w", 2)
		w.call("_select", 44)
		Input.action_press("basic_attack")
		await _shot("range_ring", OUT_COMBAT, 3)
		Input.action_release("basic_attack")
		await _clear(w)
	if _wanted("attack_classes"):
		# Las cuatro clases golpeando a la vez (ataque básico cuerpo a cuerpo; el mago con su bastón a 1 casilla).
		var attackers := {"warrior": Vector2(700, 600), "rogue": Vector2(845, 600), "mage": Vector2(700, 690), "priest": Vector2(845, 690)}
		var names := {"warrior": "Guerrero", "rogue": "Pícara", "mage": "Maga", "priest": "Sacerdote"}
		var id := 30
		var hits: Array = []
		for cls: String in attackers:
			var at: Vector2 = attackers[cls]
			_spawn(id, "player", cls, names[cls], at, "e", 4)
			_spawn(id + 1, "monster", "boar" if cls != "mage" else "slime", "Jabalí" if cls != "mage" else "Slime", at + Vector2(30, 0), "w", 2)
			hits.append(_hit(id, id + 1, 9 + id % 7))
			id += 2
		await _wait_ms(150)
		_dispatch("CombatEvents", {"tick": 30, "e": hits})
		await _wait_ms(170)  # tercer cuadro del ataque (el golpe), a 12 fps
		var img := await _shot("attack_classes", OUT_COMBAT, 1)
		for cls: String in attackers:
			_crop(img, w, (attackers[cls] as Vector2) + Vector2(14, -10), "attack_" + cls)
		await _clear(w)
	if _wanted("monster_attacks"):
		var line := [["slime", Vector2(575, 590)], ["boar", Vector2(690, 590)], ["skeleton_warrior", Vector2(825, 590)], ["bandit", Vector2(940, 590)],
			["goblin_archer", Vector2(860, 655)], ["foreman_grask", Vector2(740, 725)], ["lesser_lich_king", Vector2(905, 725)]]
		var hits: Array = []
		var id := 50
		for e: Array in line:
			var at: Vector2 = e[1]
			_spawn(id, "monster", e[0], Content.monster(str(e[0])).get("name", e[0]), at, "e", 4)
			var target_at := at + (Vector2(26, 0) if e[0] != "goblin_archer" else Vector2(60, 0))
			if e[0] in ["foreman_grask", "lesser_lich_king"]:
				target_at.x += 10  # los jefes ocupan 64×64
			_spawn(id + 1, "player", "warrior", "Tanque", target_at, "w", 4)
			hits.append(_hit(id, id + 1, 7))
			id += 2
		await _wait_ms(150)
		_dispatch("CombatEvents", {"tick": 31, "e": hits})
		await _wait_ms(100)
		var img := await _shot("monster_attacks", OUT_COMBAT, 1)
		_crop(img, w, Vector2(780, 705), "monster_attacks_foreman", Vector2(60, 44))
		_crop(img, w, Vector2(945, 705), "monster_attacks_lich", Vector2(60, 44))
		await _clear(w)
	if _wanted("cast_glow") or _wanted("projectile") or _wanted("impact"):
		_spawn(60, "monster", "wolf", "Lobo de las colinas", self_at + Vector2(90, -20), "w", 4)
		_spawn(61, "player", "priest", "Lumen", self_at + Vector2(-50, 30), "e", 4)
		_spawn(62, "player", "warrior", "Diego", self_at + Vector2(-40, -10), "e", 5)
		await _wait_ms(150)
		_dispatch("CastStarted", {"casterId": 1, "spellId": "mage_fireball", "targetId": 60, "durationMs": 2000})
		_dispatch("CastStarted", {"casterId": 61, "spellId": "priest_heal", "targetId": 62, "durationMs": 1500})
		await _wait_ms(400)
		if _wanted("cast_glow"):
			var img := await _shot("cast_glow", OUT_COMBAT, 1)
			_crop(img, w, self_at + Vector2(-25, 0), "cast_glow_closeup", Vector2(60, 34))
		_dispatch("CastEnded", {"casterId": 1, "spellId": "mage_fireball", "result": "done"})
		_dispatch("CastEnded", {"casterId": 61, "spellId": "priest_heal", "result": "done"})
		await _wait_ms(230)
		if _wanted("projectile"):
			var img := await _shot("projectile", OUT_COMBAT, 1)
			_crop(img, w, self_at + Vector2(45, -18), "projectile_closeup", Vector2(60, 34))
		_dispatch("CombatEvents", {"tick": 40, "e": [_hit(1, 60, 38, "mage_fireball", "dmg", true), _hit(61, 62, 24, "priest_heal", "heal")]})
		# La bola tarda distancia / velocidad (≈ 470 ms aquí) desde el CastEnded: la captura cae justo tras el impacto.
		await _wait_ms(320)
		if _wanted("impact"):
			var img := await _shot("impact", OUT_COMBAT, 1)
			_crop(img, w, self_at + Vector2(90, -34), "impact_closeup", Vector2(50, 34))
		await _clear(w)
	if _wanted("area_resolve"):
		for i: int in 4:
			_spawn(70 + i, "monster", "slime", "Slime", Vector2(860, 610) + Vector2(i % 2 * 22 - 11, i / 2 * 18 - 9), "w", 2)
		await _wait_ms(120)
		_dispatch("CastStarted", {"casterId": 1, "spellId": "mage_flame_burst", "targetPos": {"x": 860.0, "y": 610.0}, "durationMs": 1500})
		await _wait_ms(300)
		_dispatch("CastEnded", {"casterId": 1, "spellId": "mage_flame_burst", "result": "done"})
		_dispatch("CombatEvents", {"tick": 50, "e": [_hit(1, 70, 21, "mage_flame_burst"), _hit(1, 71, 19, "mage_flame_burst"), _hit(1, 72, 24, "mage_flame_burst"), _hit(1, 73, 20, "mage_flame_burst")]})
		await _wait_ms(120)
		var img := await _shot("area_resolve", OUT_COMBAT, 1)
		_crop(img, w, Vector2(850, 605), "area_resolve_closeup", Vector2(60, 40))
		await _clear(w)
	if _wanted("dead_monster"):
		_spawn(80, "monster", "boar", "Jabalí", self_at + Vector2(50, 0), "w", 2)
		_spawn(81, "monster", "wolf", "Lobo", self_at + Vector2(-50, 10), "e", 3)
		_spawn(82, "monster", "skeleton_warrior", "Esqueleto", self_at + Vector2(10, 40), "n", 5)
		await _wait_ms(150)
		_snapshot(self_at, [{"id": 80, "x": self_at.x + 50, "y": self_at.y, "dir": "w", "hpPct": 0, "anim": "dead"},
			{"id": 81, "x": self_at.x - 50, "y": self_at.y + 10, "dir": "e", "hpPct": 0, "anim": "dead"},
			{"id": 82, "x": self_at.x + 10, "y": self_at.y + 40, "dir": "n", "hpPct": 0, "anim": "dead"}])
		await _wait_ms(800)
		var img := await _shot("dead_monster", OUT_COMBAT, 1)
		_crop(img, w, self_at + Vector2(0, 14), "dead_monster_closeup", Vector2(76, 40))
		await _clear(w)
	if _wanted("esc_menu") or _wanted("logout_in_combat"):
		_spawn(90, "monster", "wolf", "Lobo de las colinas", self_at + Vector2(40, -10), "w", 4)
		await _wait_ms(100)
		w.call("_open_game_menu")
		if _wanted("esc_menu"):
			await _shot("esc_menu", OUT_COMBAT)
		if _wanted("logout_in_combat"):
			w.set("_logout_req_id", 99)
			w.set("_logout_after", "select")
			_dispatch("Error", {"code": "in_combat", "reqId": 99})
			await _shot("logout_in_combat", OUT_COMBAT)
		(w.get("_game_menu") as GameMenu).close()
	w.queue_free()
	await get_tree().process_frame


## Quita las entidades de la captura anterior (EntityDespawn como el servidor) y los efectos que queden.
func _clear(w: Node2D) -> void:
	for id: Variant in (w.get("_remotes") as Dictionary).keys():
		_dispatch("EntityDespawn", {"id": id, "reason": "left"})
	(w.get("_presenter") as CombatPresenter).clear()
	await get_tree().process_frame
