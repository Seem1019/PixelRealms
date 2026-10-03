extends GutTest
## HU-053 CA4: tooltip con 3 items de ejemplo (arma, armadura, consumible) y comparación.


func test_weapon_tooltip_mage_low_affinity() -> void:
	var sword := Content.item("iron_sword")
	var text := TooltipBuilder.build(sword, 1, "mage", 3)
	assert_string_contains(text, "Espada de hierro")
	assert_string_contains(text, "Afinidad: baja (×0.7)")
	assert_string_contains(text, "Daño 4.2–7.7")  # 6–11 × 0.7
	assert_string_contains(text, "(base 6–11")
	assert_string_contains(text, "+1.4 Fuerza (base 2)")
	assert_string_contains(text, "Requiere nivel 4")
	assert_string_contains(text, "#ff5555]Requiere")  # nivel insuficiente en rojo
	assert_string_contains(text, "básico físico")


func test_armor_tooltip_warrior_high_affinity_and_compare() -> void:
	var mail := Content.item("recruit_mail_shirt")
	var text := TooltipBuilder.build(mail, 1, "warrior", 1)
	assert_string_contains(text, "Afinidad: alta (×1)")
	assert_string_contains(text, "Armadura 14")
	assert_false(text.contains("(base 14)"))
	var robe := Content.item("novice_robe")
	var cmp := TooltipBuilder.compare(mail, robe, "warrior")  # 14 vs 3 × 0.7 = 2.1 → ▲ +11.9
	assert_eq(cmp.size(), 1)
	assert_string_contains(cmp[0], "▲ +11.9 Armadura")


func test_consumable_tooltip_and_money() -> void:
	var potion := Content.item("minor_healing_potion")
	var text := TooltipBuilder.build(potion, 5, "priest", 1)
	assert_string_contains(text, "Poción menor de vida")
	assert_string_contains(text, "×5")
	assert_string_contains(text, "Venta: 5c")
	assert_false(text.contains("Afinidad"))
	assert_eq(MoneyFormat.format(123456), "12o 34p 56c")
	assert_eq(MoneyFormat.format(56), "56c")
	assert_eq(MoneyFormat.format(10000), "1o 0p 0c")


func test_dps_uses_haste() -> void:
	var dagger := Content.item("worn_dagger")  # 2–4, 1600 ms; Pícaro haste 1.15 → 3 / (1.6 / 1.15) = 2.156
	assert_almost_eq(TooltipBuilder.dps(dagger, 1.0, 1.15), 3.0 / (1.6 / 1.15), 0.001)


func test_medium_affinity_shows_two_decimals() -> void:
	# HU-053 CA3b: ×0.85, no ×0.8 (antes se redondeaba a un decimal).
	var dagger := Content.item("worn_dagger")
	assert_string_contains(TooltipBuilder.build(dagger, 1, "warrior", 1), "Afinidad: media (×0.85)")


func test_weapon_compare_includes_dps() -> void:
	# HU-053 CA2: comparar armas incluye la diferencia de DPS (con afinidad y haste de la clase).
	var iron := Content.item("iron_sword")
	var worn := Content.item("worn_sword")
	var cmp := TooltipBuilder.compare(iron, worn, "warrior")
	assert_true(cmp.any(func(line: String) -> bool: return line.contains("DPS") and line.contains("▲")), str(cmp))


## HU-042 CA2: el tooltip de cada stat de la ficha desglosa base + equipo (con la afinidad de cada pieza) + auras.
func test_stat_origin_breaks_down_base_equipment_affinity_and_auras() -> void:
	var equipment: Array = [{"templateId": "iron_sword"}, {"templateId": "bandit_gloves"}, null]
	# Agilidad del guerrero nivel 3: 7 + 1 × 2 = 9; guantes de cuero (afinidad media) 2 × 0.85 = 1.7; el resto, auras.
	assert_eq(CharacterPanel.stat_origin("agi", "warrior", 3, equipment, 11), "Base 9 + Equipo 1.7 (Guantes de bandido: afinidad media ×0.85) + Auras 0.3")
	assert_eq(CharacterPanel.stat_origin("sta", "warrior", 3, equipment, 17), "Base 16 + Equipo 1 (Espada de hierro: afinidad alta ×1) + Auras 0")
	assert_eq(CharacterPanel.stat_origin("int", "warrior", 1, [], 3), "Base 3 + Equipo 0 + Auras 0")
