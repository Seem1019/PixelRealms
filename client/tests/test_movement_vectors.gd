extends GutTest
## Regla 6: los vectores de shared/test-vectors/movement.json (copiados a tests/vectors/) pasan en movement_step.gd.

const VECTORS_PATH := "res://tests/vectors/movement.json"


func _load_cases() -> Array:
	assert_true(FileAccess.file_exists(VECTORS_PATH), "faltan los vectores en %s" % VECTORS_PATH)
	if not FileAccess.file_exists(VECTORS_PATH):
		return []
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(VECTORS_PATH))
	return (parsed as Dictionary).get("cases", [])


func test_all_vectors() -> void:
	var cases := _load_cases()
	assert_gt(cases.size(), 0)
	for c: Variant in cases:
		var cd: Dictionary = c
		var rows: Array = cd["grid"]
		var grid := CollisionGrid.new(str(rows[0]).length(), rows.size())
		for y in rows.size():
			var row := str(rows[y])
			for x in row.length():
				if row[x] == "#":
					grid.set_solid(x, y)
		var start: Dictionary = cd["start"]
		var pos := Vector2(float(start["x"]), float(start["y"]))
		var speed := float(cd["speed"])
		for inp: Variant in cd["inputs"]:
			var idict: Dictionary = inp
			for _t in int(idict["ticks"]):
				pos = MovementStep.step(pos.x, pos.y, int(idict["dx"]), int(idict["dy"]), speed, grid)
		var expected: Dictionary = cd["expected"]
		assert_almost_eq(pos.x, float(expected["x"]), 0.001, str(cd["name"]) + " x")
		assert_almost_eq(pos.y, float(expected["y"]), 0.001, str(cd["name"]) + " y")


func test_round2_half_away_from_zero() -> void:
	assert_almost_eq(MovementStep.round2(2.345), 2.35, 0.0001)
	assert_almost_eq(MovementStep.round2(-2.345), -2.35, 0.0001)
	assert_almost_eq(MovementStep.round2(40.0), 40.0, 0.0001)
