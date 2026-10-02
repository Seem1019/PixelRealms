class_name PlaceholderSprite
extends Node2D
## Respaldo cuando falta la hoja de sprites de una entidad (skill pixel-art-assets §Placeholders): rectángulo de color con
## contorno y la inicial del nombre, con los pies en el origen.

var color: Color = Color("cd683d"):
	set(v):
		color = v
		queue_redraw()
var initial: String = "":
	set(v):
		initial = v
		queue_redraw()


func _draw() -> void:
	var r := Rect2(-6, -16, 12, 17)
	draw_rect(r.grow(1.0), UiTheme.OUTLINE)
	draw_rect(r, color)
	draw_rect(Rect2(r.position, Vector2(r.size.x, 1)), color.lightened(0.3))
	var f := UiTheme.font()
	if f != null and not initial.is_empty():
		draw_string(f, Vector2(-2, -5), initial.to_upper(), HORIZONTAL_ALIGNMENT_LEFT, -1, UiTheme.FONT_BODY, UiTheme.OUTLINE)
