class_name CollisionGrid
extends RefCounted
## Grilla de colisión del cliente, construida desde el mismo .tmj que usa el servidor (skill world-maps §Cliente).
## Espejo de CollisionGrid.cs: fuera del mapa cuenta como sólido.

var width: int = 0
var height: int = 0
var _solid: PackedByteArray = PackedByteArray()
var _blocks_sight: PackedByteArray = PackedByteArray()


func _init(w: int = 1, h: int = 1) -> void:
	width = maxi(w, 1)
	height = maxi(h, 1)
	_solid.resize(width * height)
	_blocks_sight.resize(width * height)
	_solid.fill(0)
	_blocks_sight.fill(0)


func in_bounds(x: int, y: int) -> bool:
	return x >= 0 and y >= 0 and x < width and y < height


func is_solid(x: int, y: int) -> bool:
	return not in_bounds(x, y) or _solid[y * width + x] != 0


func blocks_sight(x: int, y: int) -> bool:
	return not in_bounds(x, y) or _blocks_sight[y * width + x] != 0


func set_solid(x: int, y: int, value: bool = true) -> void:
	if in_bounds(x, y):
		_solid[y * width + x] = 1 if value else 0


func set_blocks_sight(x: int, y: int, value: bool = true) -> void:
	if in_bounds(x, y):
		_blocks_sight[y * width + x] = 1 if value else 0


## Casilla sólida en coordenadas continuas (tiles).
func is_solid_at(x: float, y: float) -> bool:
	return is_solid(int(floor(x)), int(floor(y)))
