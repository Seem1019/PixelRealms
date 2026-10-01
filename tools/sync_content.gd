@tool
extends SceneTree
## Copia ../content/*.json a client/content/ (carpeta ignorada por git). HU-005 CA6.
## Uso: godot --path client --headless -s ../tools/sync_content.gd

func _init() -> void:
	var src := ProjectSettings.globalize_path("res://").path_join("../content")
	var dst := ProjectSettings.globalize_path("res://content")
	var copied := _copy_json(src, dst)
	print("sync_content: %d archivos copiados a %s" % [copied, dst])
	quit(0 if copied > 0 else 1)


func _copy_json(src: String, dst: String) -> int:
	var dir := DirAccess.open(src)
	if dir == null:
		push_error("No existe %s" % src)
		return 0
	DirAccess.make_dir_recursive_absolute(dst)
	var n := 0
	for f in dir.get_files():
		if f.ends_with(".json"):
			var err := DirAccess.copy_absolute(src.path_join(f), dst.path_join(f))
			if err == OK:
				n += 1
	return n
