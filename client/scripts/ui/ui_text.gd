class_name UiText
## Textos visibles en español para los identificadores del contenido (tipos de item, de arma y armadura, clases, roles y
## recursos). Si falta una traducción se muestra el identificador tal cual: así se nota y se añade aquí.

const ITEM_TYPES := {"weapon": "Arma", "armor": "Armadura", "consumable": "Consumible", "junk": "Basura", "material": "Material"}
const GEAR_TYPES := {
	"sword": "Espada", "dagger": "Daga", "staff": "Bastón", "mace": "Maza", "wand": "Varita", "axe": "Hacha",
	"cloth": "Tela", "leather": "Cuero", "mail": "Malla", "plate": "Placas", "shield": "Escudo", "jewelry": "Joya",
}
const ROLES := {"tank": "Tanque", "melee_dps": "Daño cuerpo a cuerpo", "ranged_dps": "Daño a distancia", "healer": "Sanador"}
const RESOURCES := {"mana": "Maná", "rage": "Ira", "energy": "Energía"}
## Mapas (`mapId` de Tiled) con el nombre del GDD: cada tier vive en un mapa (`meadow`, `forest`) y cada cueva en el suyo.
const MAPS := {"meadow": "Robledal", "mine": "Mina Abandonada", "forest": "Bosque", "crypt": "Cripta de Raíces"}


static func item_type(id: String) -> String:
	return str(ITEM_TYPES.get(id, id))


static func gear_type(id: String) -> String:
	return str(GEAR_TYPES.get(id, id))


static func role(id: String) -> String:
	return str(ROLES.get(id, id))


static func resource(id: String) -> String:
	return str(RESOURCES.get(id, id))


static func map_name(id: String) -> String:
	return str(MAPS.get(id, id))


## Nombre de la clase desde classes.json (Guerrero, Mago…).
static func class_name_of(class_id: String) -> String:
	return str(Content.character_class(class_id).get("name", class_id))


## Etiqueta corta para una casilla pequeña: la primera palabra completa (nunca cortada a media palabra). El nombre entero va
## en el tooltip.
static func short_name(full_name: String) -> String:
	var first := full_name.get_slice(" ", 0)
	return first if not first.is_empty() else full_name
