extends Node
## Aplica el tema único (UiTheme) a toda la interfaz. Los Control dentro de un CanvasLayer (el HUD) no heredan el tema de la
## ventana, así que se fusiona con el tema por defecto del motor, que es el último al que recurre cualquier Control (también
## tooltips y popups).


func _ready() -> void:
	ThemeDB.get_default_theme().merge_with(UiTheme.build())
	ThemeDB.fallback_font_size = UiTheme.FONT_BODY
