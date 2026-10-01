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
