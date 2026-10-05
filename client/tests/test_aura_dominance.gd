extends GutTest
## ADR-022 / HU-038 CA4b: en los marcos, el modificador que no manda se ve en gris; mismo criterio que `AuraSet.IsDominant`.


func _ids(list: Array) -> Array:
	return CombatHud._dominant_ids(list).keys()


func test_the_strongest_damage_done_wins_and_the_other_is_grey() -> void:
	var ids := _ids([{"auraId": "foreman_rally_aura"}, {"auraId": "rogue_shadowstep_empower"}])  # 0,25 y 0,6
	assert_has(ids, "rogue_shadowstep_empower")
	assert_does_not_have(ids, "foreman_rally_aura")


func test_different_modifier_types_do_not_compete() -> void:
	var ids := _ids([{"auraId": "warrior_shield_block_aura"}, {"auraId": "foreman_rally_aura"}])  # recibido −0,5 y hecho 0,25
	assert_has(ids, "warrior_shield_block_aura")
	assert_has(ids, "foreman_rally_aura")


func test_slows_keep_working() -> void:
	var ids := _ids([{"auraId": "priest_pulse_slow"}, {"auraId": "mage_chill"}])  # 0,3 y 0,4
	assert_has(ids, "mage_chill")
	assert_does_not_have(ids, "priest_pulse_slow")
	assert_has(_ids([{"auraId": "mage_chill"}, {"auraId": "mage_chill"}]), "mage_chill", "dos iguales: mandan los dos")
