extends Node
## Carga res://content/*.json (copia de ../content, ver tools/sync_content.gd) en diccionarios tipados por id.
## El cliente solo lee contenido para dibujar y predecir; nunca decide resultados.

const CONTENT_DIR := "res://content/"
const FILES := {
	"classes": "classes", "spells": "spells", "auras": "auras", "items": "items",
	"monsters": "monsters", "loot_tables": "lootTables", "vendors": "vendors",
}

var rules: Dictionary = {}
var loaded: bool = false

var _by_id: Dictionary = {}  # tipo → { id → Dictionary }


func _ready() -> void:
	load_all()


## Carga todos los archivos; devuelve false (y deja `loaded` en false) si falta alguno.
func load_all() -> bool:
	_by_id.clear()
	var ok := true
	for file_name: String in FILES.keys():
		var root_key: String = FILES[file_name]
		var data := _read_json(CONTENT_DIR + file_name + ".json")
		var table := {}
		if data.is_empty() or not data.has(root_key):
			push_warning("Contenido no disponible: %s.json (ejecuta tools/sync_content.gd)" % file_name)
			ok = false
		else:
			for entry: Dictionary in data[root_key]:
				table[str(entry["id"])] = entry
		_by_id[file_name] = table
	rules = _read_json(CONTENT_DIR + "rules.json")
	if rules.is_empty():
		ok = false
	loaded = ok
	return ok


func spell(id: String) -> Dictionary:
	return _lookup("spells", id)


func aura(id: String) -> Dictionary:
	return _lookup("auras", id)


func item(id: String) -> Dictionary:
	return _lookup("items", id)


func monster(id: String) -> Dictionary:
	return _lookup("monsters", id)


func character_class(id: String) -> Dictionary:
	return _lookup("classes", id)


func classes() -> Array:
	var table: Dictionary = _by_id.get("classes", {})
	return table.values()


## Lee una constante anidada de rules.json: rule("combat", "gcdMs").
func rule(section: String, key: String, default: Variant = null) -> Variant:
	var sec: Variant = rules.get(section, {})
	if sec is Dictionary:
		return (sec as Dictionary).get(key, default)
	return default


func _lookup(kind: String, id: String) -> Dictionary:
	var table: Dictionary = _by_id.get(kind, {})
	if not table.has(id):
		push_warning("Contenido desconocido: %s '%s'" % [kind, id])
		return {}
	return table[id]


func _read_json(path: String) -> Dictionary:
	if not FileAccess.file_exists(path):
		return {}
	var text := FileAccess.get_file_as_string(path)
	var parsed: Variant = JSON.parse_string(text)
	if parsed is Dictionary:
		return parsed
	push_error("JSON inválido: %s" % path)
	return {}
