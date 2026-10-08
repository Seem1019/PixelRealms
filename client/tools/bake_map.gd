extends SceneTree
## Hornea un mapa con TerrainBaker como lo ve el juego (suelo y copas, sin entidades), guarda la vista en PNG y mide el
## horneado en frío (HU-111: docs/screenshots/tier2/forest.png). Sin render: vale con --headless. Antes, tools/sync_content.gd.
##   godot --path client --headless -s res://tools/bake_map.gd -- <mapId> <salida.png> [escala]
## p. ej. `-- forest ../docs/screenshots/tier2/forest.png 0.5` (una salida relativa lo es a client/).


func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 2:
		push_error("Uso: -- <mapId> <salida.png> [escala]")
		quit(1)
		return
	var t0 := Time.get_ticks_usec()
	var map := TmjMap.load_from("res://maps/%s.tmj" % args[0])
	if map == null:
		quit(1)
		return
	var t1 := Time.get_ticks_usec()
	var baked := TerrainBaker.bake(map)
	var t2 := Time.get_ticks_usec()
	if baked.is_empty():
		push_error("Falta el atlas de %s" % TerrainBaker.atlas_path_for(map))
		quit(1)
		return
	print("%s %dx%d · leer el .tmj %.0f ms · hornear en frío %.0f ms (atlas %s)" % [map.map_id, map.width, map.height,
		(t1 - t0) / 1000.0, (t2 - t1) / 1000.0, TerrainBaker.atlas_path_for(map).get_file()])
	var img := (baked["ground"] as Image).duplicate() as Image
	var above := baked["above"] as Image
	img.blend_rect(above, Rect2i(Vector2i.ZERO, above.get_size()), Vector2i.ZERO)
	var scale := float(args[2]) if args.size() > 2 else 1.0
	if not is_equal_approx(scale, 1.0):
		img.resize(roundi(img.get_width() * scale), roundi(img.get_height() * scale), Image.INTERPOLATE_NEAREST)  # pixel art: sin mezclar colores
	var out := args[1] if args[1].is_absolute_path() else ProjectSettings.globalize_path("res://").path_join(args[1])
	img.save_png(out)
	print("vista: %s %s" % [out.simplify_path(), img.get_size()])
	quit(0)
