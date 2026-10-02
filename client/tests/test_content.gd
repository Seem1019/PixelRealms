extends GutTest
## HU-005 CA4/CA5: el autoload Content carga res://content/*.json y resuelve ids.


func test_content_loaded() -> void:
	assert_true(Content.loaded, "client/content/ debe existir (tools/sync_content.gd)")


func test_spell_fireball_name() -> void:
	assert_eq(Content.spell("mage_fireball").get("name"), "Bola de fuego")


func test_rule_lookup() -> void:
	assert_eq(int(Content.rule("combat", "gcdMs")), 1000)


func test_unknown_id_is_empty() -> void:
	assert_true(Content.item("nope").is_empty())


func test_four_classes() -> void:
	assert_eq(Content.classes().size(), 4)


func test_has_monster_tells_player_classes_apart_without_warning() -> void:
	# El presentador de combate pregunta por el arquetipo del atacante: de un jugador llega su clase ("rogue").
	assert_true(Content.has_monster("slime"))
	assert_false(Content.has_monster("rogue"))
