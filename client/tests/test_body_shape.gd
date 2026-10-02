extends GutTest
## Punto 4 de la prueba de juego: el cuadro que se dibuja es el que usa el servidor para decidir a quién alcanza un área
## (TargetResolver.DistanceSquaredToBody). Mismos casos que GroundArea_HitsWhenTheCircleTouchesTheDrawnBody_NotOnlyTheFeet.


func test_body_rect_comes_from_rules() -> void:
	var r := BodyShape.rect_px(Vector2(100, 100))
	assert_eq(r, Rect2(94, 88, 12, 16), "0,375 de medio ancho, 0,75 por encima de los pies y 0,25 por debajo (×16 px)")


func test_circle_touches_the_body_like_the_server() -> void:
	var center := Vector2(10, 10) * 16.0
	var radius := 2.5 * 16.0
	assert_true(BodyShape.circle_touches(center, radius, Vector2(10, 12.6) * 16.0), "pies fuera, parte de arriba dentro")
	assert_true(BodyShape.circle_touches(center, radius, Vector2(12.8, 10) * 16.0), "pies fuera, lado izquierdo dentro")
	assert_false(BodyShape.circle_touches(center, radius, Vector2(10, 7.2) * 16.0), "el cuadro queda por encima del círculo")


func test_area_hits_respects_max_targets_order_and_walls() -> void:
	var grid := CollisionGrid.new(40, 40)
	var center := Vector2(10.5, 10.5) * 16.0
	var candidates: Array[Dictionary] = [
		{"id": 3, "feet_px": Vector2(12.0, 10.5) * 16.0}, {"id": 1, "feet_px": Vector2(11.0, 10.5) * 16.0}, {"id": 2, "feet_px": Vector2(13.5, 10.5) * 16.0}]
	assert_eq(BodyShape.area_hits(center, 2.5 * 16.0, 2, candidates, grid), [1, 3] as Array[int], "los dos más cercanos")
	grid.set_blocks_sight(12, 10)
	assert_eq(BodyShape.area_hits(center, 2.5 * 16.0, 3, candidates, grid), [1, 3] as Array[int], "el muro tapa al 2 (el 3 está en la casilla del muro: destino)")
	assert_true(BodyShape.area_hits(Vector2(12.5, 10.5) * 16.0, 2.5 * 16.0, 3, candidates, grid).is_empty(), "apuntar dentro del muro: no_los")
