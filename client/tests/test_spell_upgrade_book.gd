extends GutTest
## HU-105: el libro enseña las dos mejoras de cada hechizo con lo que cambian, señala las que faltan por elegir (también en la
## barra), elige fuera de combate y, con la mejora confirmada por el servidor, los números del hechizo pasan a ser los mejorados.
## El test pone dos mejoras de prueba en Bola de fuego en lugar de las del contenido (HU-107), con números fijos, y deja las del
## contenido al acabar.

const WORLD := preload("res://scenes/world/world.tscn")
const UPGRADES := [
	{"id": "fireball_quick", "name": "Llama rápida", "description": "Casteo 0,5 s más corto.", "mods": [{"stat": "castMs", "add": -500}]},
	{"id": "fireball_hot", "name": "Llama intensa", "description": "+20 % de daño.", "mods": [{"effect": "damage", "mult": 1.2}]},
]

var _content_upgrades: Dictionary = {}  ## spell_id -> mejoras del contenido, para dejarlas como estaban


func _replace_upgrades(spell_id: String, upgrades: Array) -> void:
	var spell: Dictionary = Content.spell(spell_id)
	if not _content_upgrades.has(spell_id):
		_content_upgrades[spell_id] = spell.get("upgrades")
	spell["upgrades"] = upgrades


func before_each() -> void:
	_replace_upgrades("mage_fireball", UPGRADES.duplicate(true))


func after_each() -> void:
	for spell_id: String in _content_upgrades:
		var spell: Dictionary = Content.spell(spell_id)
		if _content_upgrades[spell_id] == null:
			spell.erase("upgrades")
		else:
			spell["upgrades"] = _content_upgrades[spell_id]
	_content_upgrades.clear()
	GameState.spell_upgrades = {}
	GameState.last_combat_ms = -1000000


func _dispatch(t: String, d: Dictionary) -> void:
	Net._dispatch(JSON.stringify({"t": t, "d": d}))


## Mago de nivel `level` con Bola de fuego en la casilla 1.
func _world(level: int, upgrades: Dictionary = {}) -> Node2D:
	Settings._config.set_value("net", "server_url", "http://127.0.0.1:1")
	GameState.pending_ticket = "t"
	GameState.pending_character_id = "c"
	GameState.last_combat_ms = -1000000
	var w := WORLD.instantiate() as Node2D
	add_child_autofree(w)
	await get_tree().process_frame
	Net.disconnect_from_server()
	var welcome := {
		"selfId": 1, "tick": 10, "tickRate": 20, "snapshotRate": 10, "mapId": "meadow",
		"self": {"x": 300.0, "y": 800.0, "level": level, "xp": 0, "xpNext": 100, "hp": 80, "maxHp": 100, "res": 40, "maxRes": 60, "resource": "mana", "classId": "mage", "name": "Ana"},
		"inventory": [], "equipment": [], "hotbar": [{"slot": 0, "kind": "spell", "ref": "mage_fireball"}],
		"knownSpells": ["mage_fireball", "mage_frostbolt"], "rulesHash": "x"}
	if not upgrades.is_empty():
		welcome["spellUpgrades"] = upgrades
	_dispatch("Welcome", welcome)
	for i: int in 3:
		await get_tree().process_frame
	return w


## Botones de mejora del libro; con `spell_id`, solo los de ese hechizo.
func _upgrade_buttons(book: SpellbookWindow, spell_id: String = "") -> Array[SpellbookWindow.UpgradeButton]:
	var out: Array[SpellbookWindow.UpgradeButton] = []
	var list: VBoxContainer = book.get("_list")
	for row: Node in list.get_children():
		if row is HBoxContainer:
			for b: Node in row.get_children():
				if b is SpellbookWindow.UpgradeButton and (spell_id == "" or (b as SpellbookWindow.UpgradeButton).spell_id == spell_id):
					out.append(b)
	return out


func _entry(book: SpellbookWindow, spell_id: String) -> SpellbookWindow.SpellEntry:
	for row: Node in (book.get("_list") as VBoxContainer).get_children():
		if row is SpellbookWindow.SpellEntry and (row as SpellbookWindow.SpellEntry).spell_id == spell_id:
			return row
	return null


func test_below_level_eight_there_is_nothing_to_choose() -> void:
	var w := await _world(7)
	var book: SpellbookWindow = w.get("_spellbook")
	book.toggle()
	await get_tree().process_frame
	assert_eq(_upgrade_buttons(book).size(), 0)
	assert_false(_entry(book, "mage_fireball").pending)


func test_the_book_shows_both_upgrades_with_what_they_change_and_marks_the_pending_one() -> void: # CA1, CA2
	var w := await _world(8)
	var book: SpellbookWindow = w.get("_spellbook")
	book.toggle()
	await get_tree().process_frame
	assert_eq(_upgrade_buttons(book).size(), 4, "Bola de fuego con las de prueba y Descarga de escarcha con las del contenido")
	var buttons := _upgrade_buttons(book, "mage_fireball")
	assert_eq(buttons.size(), 2)
	assert_eq(buttons[0].text, "Llama rápida")
	assert_string_contains(buttons[0].tooltip_bbcode, "Lanzamiento 2 s → 1.5 s")
	assert_string_contains(buttons[1].tooltip_bbcode, "+20 % de daño")
	assert_false(buttons[0].button_pressed or buttons[1].button_pressed, "ninguna elegida")
	assert_true(_entry(book, "mage_fireball").pending)
	var hud: CombatHud = w.get("_hud")
	var slot: Variant = (hud.get("_slots") as Array)[0]
	assert_eq((slot.get("_count") as Label).text, "!", "la barra también lo señala")


func test_choosing_sends_the_request_and_the_confirmed_upgrade_changes_the_numbers() -> void: # CA3, CA4
	var w := await _world(8)
	var book: SpellbookWindow = w.get("_spellbook")
	book.toggle()
	await get_tree().process_frame
	assert_true(GameState.choose_upgrade("mage_fireball", "fireball_quick"), "fuera de combate se pide")
	_dispatch("SpellUpgradesUpdate", {"upgrades": {"mage_fireball": "fireball_quick"}, "reqId": 3})
	await get_tree().process_frame
	assert_eq(int(GameState.effective_spell("mage_fireball")["castMs"]), 1500)
	assert_string_contains(_entry(book, "mage_fireball").tooltip_bbcode, "Lanzamiento 1.5 s")
	assert_false(_entry(book, "mage_fireball").pending)
	var buttons := _upgrade_buttons(book, "mage_fireball")
	assert_true(buttons[0].button_pressed)
	assert_false(buttons[1].button_pressed)
	_dispatch("SpellUpgradesUpdate", {"upgrades": {"mage_fireball": "fireball_hot"}, "reqId": 4}) # cambiar es gratis
	await get_tree().process_frame
	assert_eq(int(GameState.effective_spell("mage_fireball")["castMs"]), 2000)
	assert_true(_upgrade_buttons(book, "mage_fireball")[1].button_pressed)


func test_in_combat_the_upgrades_are_disabled_and_say_why() -> void: # CA3
	var w := await _world(8)
	GameState.last_combat_ms = Time.get_ticks_msec()
	var book: SpellbookWindow = w.get("_spellbook")
	book.toggle()
	await get_tree().process_frame
	var buttons := _upgrade_buttons(book, "mage_fireball")
	assert_true(buttons[0].disabled)
	assert_string_contains(buttons[0].tooltip_bbcode, "No puedes cambiar mejoras en combate")
	assert_false(GameState.choose_upgrade("mage_fireball", "fireball_quick"))


func test_reaching_level_eight_says_the_upgrades_can_be_chosen() -> void: # CA2
	var w := await _world(7)
	_dispatch("LevelUp", {"level": 8, "newSpells": [], "upgradesUnlocked": ["mage_fireball"]})
	var hud: CombatHud = w.get("_hud")
	assert_string_contains((hud.get("_notice_label") as Label).text, "Ya puedes elegir las mejoras de tus hechizos (P)")


func test_a_remote_cast_with_an_upgrade_draws_the_upgraded_shape() -> void: # revisión de autoridad de HU-104
	_replace_upgrades("warrior_cleave", [{"id": "cleave_wide", "name": "Abierto", "description": "x", "mods": [{"stat": "aoeAngleDeg", "add": 30}]}])
	var w := await _world(8)
	var reticle: AoeReticle = w.get("_reticle")
	_dispatch("CastStarted", {"casterId": 9, "spellId": "warrior_cleave", "targetPos": {"x": 340.0, "y": 800.0}, "origin": {"x": 300.0, "y": 800.0}, "durationMs": 500, "upgradeId": "cleave_wide"})
	var marks: Dictionary = reticle.get("_marks")
	assert_almost_eq(float((marks[9]["area"] as Dictionary)["angle_deg"]), 120.0, 0.01)
