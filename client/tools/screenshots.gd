extends SceneTree
## Arranca tools/capture_screenshots.gd como nodo cuando ya están los autoloads (un script -s no los ve al compilar).


func _init() -> void:
	await process_frame
	var runner := Node.new()
	runner.set_script(load("res://tools/capture_screenshots.gd"))
	root.add_child(runner)
