extends GutTest
## Carga todos los scripts del proyecto (fuera de addons) para que un error de parseo falle la suite aunque la escena no se abra.

const DIRS := ["res://autoload", "res://scenes", "res://scripts"]


func test_all_scripts_parse() -> void:
	var count := 0
	for dir: String in DIRS:
		for path: String in _collect(dir):
			var script: Variant = load(path)
			assert_not_null(script, "no carga: %s" % path)
			if script is GDScript:
				assert_true((script as GDScript).can_instantiate() or (script as GDScript).is_abstract(), "no compila: %s" % path)
			count += 1
	assert_gt(count, 5)


func _collect(dir_path: String) -> Array[String]:
	var out: Array[String] = []
	var dir := DirAccess.open(dir_path)
	if dir == null:
		return out
	dir.list_dir_begin()
	var name := dir.get_next()
	while name != "":
		var full := dir_path.path_join(name)
		if dir.current_is_dir():
			out.append_array(_collect(full))
		elif name.ends_with(".gd"):
			out.append(full)
		name = dir.get_next()
	dir.list_dir_end()
	return out
