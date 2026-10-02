extends Node
## Capturas del rediseño visual (docs/screenshots/redesign/). Sin servidor: instancia las escenas y les inyecta mensajes como
## los tests. Necesita render (no --headless):
##   xvfb-run -a -s "-screen 0 1440x810x24" godot --path client --rendering-driver opengl3 -s res://tools/screenshots.gd
## Argumento opcional tras `--`: nombres de captura separados por comas (p. ej. `-- world_hud,npc_dialog`).
## (tools/screenshots.gd arranca este nodo cuando ya existen los autoloads.)

const OUT := "res://../docs/screenshots/redesign/"
const WORLD := "res://scenes/world/world.tscn"

var _only: PackedStringArray = []


func _ready() -> void:
	var args := OS.get_cmdline_user_args()
	if not args.is_empty():
		_only = args[0].split(",")
	await get_tree().process_frame
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(OUT))
	await _login()
	await _character_select()
	await _world_shots()
	get_tree().quit()


func _wanted(name: String) -> bool:
	return _only.is_empty() or _only.has(name)


func _shot(name: String) -> void:
	for i: int in 4:
		await get_tree().process_frame
	await RenderingServer.frame_post_draw
	var img := get_tree().root.get_texture().get_image()
	img.save_png(ProjectSettings.globalize_path(OUT + name + ".png"))
	print("captura: ", name, " ", img.get_size())


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
			w.call("_update_area_preview", Vector2(845, 630))
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
