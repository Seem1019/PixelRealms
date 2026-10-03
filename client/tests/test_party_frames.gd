extends GutTest
## HU-062 CA1 / HU-027 CA5: el marco de un compañero muestra vida y recurso y, si está en otro mapa, cuál.


func _member(extra: Dictionary) -> Dictionary:
	var md := {"name": "Bob", "classId": "mage", "level": 4, "hpPct": 80, "online": true, "mapId": "meadow"}
	md.merge(extra, true)
	return md


func test_shows_health_and_resource_in_percent() -> void:
	var text := SocialPanels.party_frame_text(_member({"resPct": 45}), "meadow")
	assert_string_contains(text, "Bob")
	assert_string_contains(text, "nv4")
	assert_string_contains(text, "80%")
	assert_string_contains(text, "45% maná")


func test_shows_the_map_only_when_it_is_not_mine() -> void:
	assert_false(SocialPanels.party_frame_text(_member({}), "meadow").contains("Mina"))
	assert_string_contains(SocialPanels.party_frame_text(_member({"mapId": "mine"}), "meadow"), "Mina Abandonada")


func test_offline_member_without_resource() -> void:
	var text := SocialPanels.party_frame_text(_member({"online": false}), "meadow")
	assert_string_contains(text, "(desc.)")
	assert_false(text.contains("·"), "sin resPct (compañero fuera del mundo) no se inventa un recurso")
