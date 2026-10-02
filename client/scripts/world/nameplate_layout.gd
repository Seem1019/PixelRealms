class_name NameplateLayout
## Reparte las placas de nombre para que no se pisen ni se salgan de la vista: de delante hacia atrás (más abajo en
## pantalla primero), cada placa que choca con una ya colocada sube lo justo para quedar encima; después se mete dentro
## del rectángulo visible. Solo cambia dónde se dibuja la placa, nunca la entidad.

const GAP := 1.0


## `plates`: [{id, rect: Rect2 (anclada, coords del mundo)}]; `view`: rectángulo visible del mundo.
## Devuelve id → desplazamiento (Vector2) a aplicar a cada placa. Las placas de entidades fuera de la vista no se mueven
## (no tiene sentido traer a la pantalla el nombre de algo que no se ve).
static func solve(plates: Array[Dictionary], view: Rect2) -> Dictionary:
	var order: Array[Dictionary] = []
	var out := {}
	for p: Dictionary in plates:
		if view.intersects(p["rect"]):
			order.append(p)
		else:
			out[p["id"]] = Vector2.ZERO
	order.sort_custom(func(a: Dictionary, b: Dictionary) -> bool:
		var ra: Rect2 = a["rect"]
		var rb: Rect2 = b["rect"]
		return ra.end.y > rb.end.y if not is_equal_approx(ra.end.y, rb.end.y) else int(a["id"]) < int(b["id"]))
	var placed: Array[Rect2] = []
	for p: Dictionary in order:
		var r: Rect2 = p["rect"]
		var moved := r
		var guard := 0
		var hit := true
		while hit and guard < 16:
			hit = false
			guard += 1
			for q: Rect2 in placed:
				if moved.grow(GAP * 0.5).intersects(q.grow(GAP * 0.5)):
					moved.position.y = q.position.y - moved.size.y - GAP
					hit = true
		# Dentro de la vista (con un pequeño margen), sin tapar el borde.
		moved.position.x = clampf(moved.position.x, view.position.x + 1.0, maxf(view.position.x + 1.0, view.end.x - moved.size.x - 1.0))
		moved.position.y = clampf(moved.position.y, view.position.y + 1.0, maxf(view.position.y + 1.0, view.end.y - moved.size.y - 1.0))
		placed.append(moved)
		out[p["id"]] = (moved.position - r.position).round()
	return out
