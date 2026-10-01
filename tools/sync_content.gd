@tool
extends SceneTree
## Copia ../content/*.json a client/content/ y ../maps/ (.tmj + tilesets) a client/maps/ (carpetas ignoradas por git). HU-005 CA6.
## Uso: godot --path client --headless -s ../tools/sync_content.gd

func _init() -> void:
	var src := ProjectSettings.globalize_path("res://").path_join("../content")
	var dst := ProjectSettings.globalize_path("res://content")
	var copied := _copy_json(src, dst)
	var maps_src := ProjectSettings.globalize_path("res://").path_join("../maps")
	var maps_dst := ProjectSettings.globalize_path("res://maps")
	var maps_copied := _copy_ext(maps_src, maps_dst, "tmj") + _copy_ext(maps_src.path_join("tilesets"), maps_dst.path_join("tilesets"), "tsj")
	print("sync_content: %d archivos de contenido y %d de mapas copiados" % [copied, maps_copied])
	quit(0 if copied > 0 else 1)


func _copy_json(src: String, dst: String) -> int:
	return _copy_ext(src, dst, "json")


func _copy_ext(src: String, dst: String, ext: String) -> int:
	var dir := DirAccess.open(src)
	if dir == null:
		push_error("No existe %s" % src)
		return 0
	DirAccess.make_dir_recursive_absolute(dst)
	var n := 0
	for f: String in dir.get_files():
		if f.ends_with("." + ext):
			var err := DirAccess.copy_absolute(src.path_join(f), dst.path_join(f))
			if err == OK:
				n += 1
	return n
