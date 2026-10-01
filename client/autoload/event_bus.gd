extends Node
## Señales globales de UI (skill godot-client §Autoloads). Solo señales: ningún estado.

## Error devuelto por el servidor (código de docs/protocol.md), para el texto rojo centrado.
signal ui_error(code: String, req_id: int)
## Cambió el objetivo seleccionado.
signal target_changed(entity_id: int)
## Conexión establecida o perdida con el servidor.
signal connection_changed(connected: bool)
## RTT medido por Ping/Pong (ms), para el overlay F3.
signal rtt_updated(rtt_ms: int)
