class_name TerrainRenderer
extends PlaceholderMapRenderer
## Dibuja el mapa con el tileset real (TerrainBaker): la capa `above` (copas, aleros) o el resto (suelo y sólidos), en
## trozos de CHUNK_PX como Sprite2D para que Godot descarte los que no se ven. Si falta el tileset, vuelve a los
## rectángulos de color de PlaceholderMapRenderer.

const CHUNK_PX := 512

var baked: bool = false


func setup(tmj: TmjMap, layers_to_draw: Array[String]) -> void:
	for c: Node in get_children():
		c.queue_free()
	var result := TerrainBaker.bake(tmj, int(Content.rule("world", "currentPhase", 1)))
	if result.is_empty():
		baked = false
		super.setup(tmj, layers_to_draw)
		return
	baked = true
	map = null
	queue_redraw()
	var img: Image = result["above" if layers_to_draw.has("above") else "ground"]
	var size := img.get_size()
	for cy: int in range(0, size.y, CHUNK_PX):
		for cx: int in range(0, size.x, CHUNK_PX):
			var region := Rect2i(cx, cy, mini(CHUNK_PX, size.x - cx), mini(CHUNK_PX, size.y - cy))
			var part := img.get_region(region)
			if part.is_invisible():
				continue
			var s := Sprite2D.new()
			s.centered = false
			s.position = Vector2(cx, cy)
			s.texture = ImageTexture.create_from_image(part)
			add_child(s)
