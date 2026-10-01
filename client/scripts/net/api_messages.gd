class_name ApiMessages
## Textos en español para los códigos de la API y del protocolo (HU-010 CA4, HU-011 CA5, HU-012 CA3).

const TEXTS := {
	"username_taken": "Ese usuario ya existe",
	"invalid_credentials": "Usuario o contraseña incorrectos",
	"invalid_length": "Longitud no válida",
	"invalid_chars": "Caracteres no permitidos",
	"too_short": "Demasiado corta",
	"reserved": "Nombre reservado",
	"reserved_name": "Ese nombre está reservado",
	"name_taken": "Ese nombre ya está en uso",
	"max_characters": "Ya tienes 4 personajes",
	"invalid_class": "Clase desconocida",
	"not_found": "No encontrado",
	"session_expired": "Tu sesión expiró",
	"http_429": "Demasiados intentos, espera un momento",
	"network": "No se pudo conectar con el servidor",
	"bad_version": "Actualiza el juego",
	"bad_ticket": "No se pudo entrar al mundo, vuelve a intentarlo",
	"disconnected": "Sin conexión con el servidor",
	"out_of_range": "Fuera de alcance",
	"no_los": "Sin línea de visión",
	"on_cooldown": "Aún no está listo",
	"on_gcd": "Aún no está listo",
	"not_enough_resource": "No tienes suficiente recurso",
	"invalid_target": "Objetivo no válido",
	"is_dead": "Estás muerto",
	"stunned": "Estás aturdido",
	"rooted": "Estás enraizado",
	"silenced": "Estás silenciado",
	"locked_out": "No puedes lanzar hechizos todavía",
	"area_limit": "Demasiadas áreas activas",
	"bag_full": "Bolsa llena",
	"not_enough_gold": "No tienes suficiente oro",
	"level_too_low": "Nivel insuficiente",
	"not_owner": "Ese botín no es tuyo",
	"in_combat": "No puedes hacer eso en combate",
	"pvp_not_allowed": "PvP no permitido aquí",
	"duel_busy": "Ya estás en un duelo",
	"trade_busy": "Ya estás en un intercambio",
	"forbidden": "No tienes permiso",
	"invalid_payload": "Petición no válida",
	"rate_limited": "Demasiados mensajes",
}


static func text_for(code: String) -> String:
	return str(TEXTS.get(code, code))


## Mensaje de un error de campo {field, code, message}: usa el del servidor si viene.
static func field_text(field_error: Dictionary) -> String:
	var msg := str(field_error.get("message", ""))
	return msg if not msg.is_empty() else text_for(str(field_error.get("code", "")))
