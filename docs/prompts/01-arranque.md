# Prompts de arranque

Úsalos en orden, cada uno en una conversación nueva (`/clear` entre ellos), empezando en **modo plan**.

---

## P0 · Orientación (solo lectura)
```
Lee CLAUDE.md, docs/architecture.md, docs/protocol.md, docs/decisions.md, docs/design/gdd.md,
docs/backlog/README.md y todas las skills de .claude/skills/. No escribas código todavía.

Luego responde:
1. Un resumen de la arquitectura en 10 líneas con tus propias palabras.
2. Contradicciones, huecos o riesgos que veas entre documentos (con archivo y sección).
3. Qué cambiarías antes de empezar y por qué (máximo 5 puntos, priorizados).
No modifiques archivos; esperaré tu análisis para decidir.
```

## P1 · HU-001 Monorepo y CI
```
Implementa HU-001 siguiendo la skill hu-implementation y la skill dotnet-server.

Contexto extra:
- Uso Windows con Git Bash; los comandos deben funcionar ahí y en Linux (CI).
- Crea la solución con `dotnet new sln` y los proyectos con `dotnet new classlib/web/console/xunit3`
  (si la plantilla xunit3 no está instalada, instálala con `dotnet new install xunit.v3.templates`).
- Central Package Management con versiones estables actuales (verifica con `dotnet package search` o `dotnet add package`
  sin versión y luego muévela a Directory.Packages.props).
- El proyecto Godot aún no existe: el job de GUT en CI debe saltarse si no hay client/project.godot.

Entrega: árbol de archivos creado, salida de build y test, y el contenido del workflow de CI.
```

## P2 · HU-002 + HU-003 Infra y contenido
```
Implementa HU-002 y después HU-003 (en ese orden, un commit por HU) siguiendo hu-implementation.
Para HU-003 lee también la skill game-content y los schemas en content/schemas/.

Importante:
- Los schemas usan $ref entre archivos (common.schema.json). Registra todos en el SchemaRegistry de JsonSchema.Net
  usando su $id.
- Las validaciones cruzadas están listadas en la skill game-content §Procedimiento; implementa todas con un test cada una.
- Si el contenido actual del repo NO pasa alguna validación, no cambies la regla: repórtamelo y propón el arreglo al JSON.
- Al final añade el hook de PostToolUse descrito en las notas técnicas de HU-003 y pruébalo editando un JSON.
```

## P3 · HU-004 Game loop
```
Implementa HU-004 con las skills hu-implementation y dotnet-server.
Quiero ver primero (modo plan) las firmas de: World, TickContext, IGameEvent, IGameClock, IRng, GameLoopService,
y los helpers de test (FakeClock, SeededRng, FixedRng, WorldBuilder, TickRunner).
Explica cómo evitas la espiral de la muerte y cómo mides p50/p99 sin asignar memoria en cada tick.
```

## P4 · HU-005 Proyecto Godot
```
Implementa HU-005 con las skills hu-implementation, godot-client y pixel-art-assets.
- Crea client/project.godot a mano (texto) con los ajustes exactos de la skill; no dependas del editor.
- Instala GUT y YATI copiando sus releases a client/addons/ (descárgalos de sus repos oficiales en GitHub;
  dime versión y licencia de cada uno y anótalo en client/assets/CREDITS.md).
- Si tienes el MCP de godot, úsalo para ejecutar el proyecto y leer errores; si no, usa `godot --headless --quit`
  para comprobar que el proyecto carga sin errores de parseo.
- Fuente pixel libre: propón 2 opciones con licencia y usa la que yo elija (pregúntame).
```

## P5 · HU-006 Protocolo base
```
Implementa HU-006 con las skills hu-implementation, net-protocol, dotnet-server y godot-client.
Al terminar quiero poder: arrancar el servidor, abrir el cliente y ver el RTT en el overlay F3.
Incluye un test de integración con WebApplicationFactory + ClientWebSocket (TestGameClient reutilizable).
Después ejecuta el subagente server-authority-reviewer sobre el diff y corrige lo que encuentre.
```

## P6 · Continuar el hito M1
```
Revisa docs/backlog/README.md y dime cuál es la siguiente HU pendiente del hito M1 cuyas dependencias estén "Hecha".
Muéstrame su resumen, riesgos y un plan. Espera mi OK para implementarla con la skill hu-implementation.
```
