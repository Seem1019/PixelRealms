# PixelRealms

Mini-MMORPG 2D pixel art para jugar con amigos: 4 clases, hechizos, botín, inventario, grupos y un jefe final.
Cliente **Godot 4 (GDScript)** · Servidor **.NET 10** autoritativo · **PostgreSQL** · WebSocket + JSON.

## Empezar
1. Instala las herramientas de `docs/prompts/README.md` §1.
2. Abre Claude Code en esta carpeta y sigue `docs/prompts/01-arranque.md` (P0 → P6).
3. Antes de tocar `content/`: `dotnet run --project tools/ContentCheck -- content/` (validador base; el definitivo llega con HU-003).

## Mapa de la documentación
| Documento | Contenido |
|---|---|
| `CLAUDE.md` | memoria del proyecto para Claude Code: reglas, comandos, skills |
| `docs/architecture.md` | arquitectura, game loop, red, predicción, persistencia, despliegue |
| `docs/protocol.md` | catálogo de mensajes cliente ⇄ servidor |
| `docs/database.md` | modelo de datos |
| `docs/decisions.md` | decisiones de arquitectura (ADR) |
| `docs/design/gdd.md` · `combat.md` · `class-kits.md` · `balance-report.md` | diseño de juego, fórmulas, pentagrama, hechizos por clase e informe de balance |
| `tools/balance/` · `tools/ContentCheck/` | modelo de balance en Python (pentagrama, solitario, jefe) · validador base de `content/` (schemas + referencias) |
| `docs/backlog/` | épicas e historias de usuario con criterios de aceptación |
| `docs/prompts/` | prompts listos para Claude Code |
| `content/` | clases, hechizos, auras, items, monstruos, botín y vendedores (JSON + schemas) |
| `shared/test-vectors/` | casos que cliente y servidor deben resolver igual |
| `.claude/skills/` · `.claude/agents/` | skills y subagentes del proyecto |
