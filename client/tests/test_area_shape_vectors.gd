extends GutTest
## HU-102: los vectores de shared/test-vectors/area_shapes.json (copiados a tests/vectors/) pasan en area_geometry.gd, como en
## AreaShape.cs: la vista previa del cono y la línea marca lo mismo que alcanza el servidor.

const VECTORS_PATH := "res://tests/vectors/area_shapes.json"


func _load() -> Dictionary:
	assert_true(FileAccess.file_exists(VECTORS_PATH), "faltan los vectores en %s" % VECTORS_PATH)
	if not FileAccess.file_exists(VECTORS_PATH):
		return {}
	return JSON.parse_string(FileAccess.get_file_as_string(VECTORS_PATH)) as Dictionary


func _v(a: Variant) -> Vector2:
	var arr: Array = a
	return Vector2(float(arr[0]), float(arr[1]))


func test_shape_vectors() -> void:
	var data := _load()
	var body: Dictionary = data.get("body", {})
	var half_w := float(body.get("halfWidth", 0))
	var above := float(body.get("above", 0))
	var below := float(body.get("below", 0))
	var cases: Array = data.get("cases", [])
	assert_gt(cases.size(), 0)
	for c: Variant in cases:
		var cd: Dictionary = c
		var dir := _v(cd["dir"]).normalized()
		var hit: bool
		if str(cd["shape"]) == "cone":
			hit = AreaGeometry.cone_touches(_v(cd["origin"]), dir, float(cd["radius"]), float(cd["angleDeg"]), _v(cd["feet"]), half_w, above, below)
		else:
			hit = AreaGeometry.line_touches(_v(cd["origin"]), dir, float(cd["length"]), float(cd["width"]), _v(cd["feet"]), half_w, above, below)
		assert_eq(hit, bool(cd["hit"]), str(cd["name"]))


func test_clear_distance_vectors() -> void:
	var cases: Array = _load().get("clear", [])
	assert_gt(cases.size(), 0)
	for c: Variant in cases:
		var cd: Dictionary = c
		var grid := CollisionGrid.new(30, 30)
		for w: Variant in cd["walls"]:
			grid.set_blocks_sight(int((w as Array)[0]), int((w as Array)[1]))
		var got := AreaGeometry.clear_distance(grid, _v(cd["from"]), _v(cd["dir"]).normalized(), float(cd["max"]))
		assert_almost_eq(got, float(cd["expected"]), 0.001, str(cd["name"]))
