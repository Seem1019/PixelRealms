class_name PlaceholderMapRenderer
extends Node2D
## Dibuja el mapa como rectángulos de color por GID mientras no hay tileset ni YATI (skill pixel-art-assets §Placeholders).
## Las capas ground/detail/walls se pintan aquí; `above` la pinta un nodo hermano con z_index mayor que las entidades.

const COLORS := {
	1: Color("4f8a3a"),  # pasto
	2: Color("8a6b3a"),  # tierra
	3: Color("b59a66"),  # camino
	4: Color("4a4a55"),  # muro
	5: Color("2f6b2a"),  # arbusto
	6: Color("2b5f9e"),  # agua
	7: Color("6b6b6b"),  # roca
	8: Color("5a4a3a"),  # suelo interior
	9: Color(1, 0, 0, 0.0),  # collision (invisible)
}

var map: TmjMap
var layer_names: Array[String] = ["ground", "detail", "walls"]


func setup(tmj: TmjMap, layers_to_draw: Array[String]) -> void:
	map = tmj
	layer_names = layers_to_draw
	queue_redraw()


func _draw() -> void:
	if map == null:
		return
	var ts := map.tile_size
	for name: String in layer_names:
		if not map.layers.has(name):
			continue
		for y: int in map.height:
			for x: int in map.width:
				var gid := map.gid_at(name, x, y)
				if gid == 0:
					continue
				var color: Color = COLORS.get(gid, Color.MAGENTA)
				if color.a > 0.0:
					draw_rect(Rect2(x * ts, y * ts, ts, ts), color)
