# client/ — reglas locales

Antes de editar aquí, carga la skill `godot-client` (y `pixel-art-assets` para assets).

- GDScript con tipado estático en todo. Nada de rutas `get_node("../..")`; usar `%UniqueName` y señales.
- El cliente no decide resultados: solo envía intenciones por `Net` y refleja lo que llega en `GameState`.
- `scripts/net/movement_step.gd` es espejo de `MovementStep.cs`: si cambias uno, cambias el otro y los vectores.
- `client/content/` es una copia generada de `../content/`: no la edites.
- Tests GUT: `godot --path client --headless -s addons/gut/gut_cmdln.gd -gdir=res://tests -gexit`.
