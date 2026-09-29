# Registro de decisiones (ADR)

Formato corto: contexto → decisión → consecuencias. Una decisión nueva se agrega al final; no se editan las
antiguas, se marcan como "Reemplazada por ADR-N".

## ADR-001 · Servidor .NET 10 + cliente Godot GDScript
- **Contexto:** el desarrollador domina .NET. Godot C# no tiene export Web oficial (4.6) y la comunidad,
  tutoriales y plugins de Godot son mayoritariamente GDScript.
- **Decisión:** toda la lógica de juego autoritativa en C# (.NET 10 LTS). Cliente delgado en GDScript tipado.
- **Consecuencias:** dos lenguajes; el movimiento se implementa dos veces → mitigado con
  `shared/test-vectors`. El cliente puede exportarse a Web, Windows, Linux y Android.

## ADR-002 · WebSocket + JSON (no UDP, no binario) en v1
- **Contexto:** ≤ 50 jugadores, Web export exige WebSocket, depuración fácil importa más que el ancho de banda.
- **Decisión:** WebSocket de ASP.NET Core, JSON con System.Text.Json source-generated.
- **Consecuencias:** ~25 KB/s por cliente. Si crece, migrar a MessagePack manteniendo el sobre `{t,d}`.

## ADR-003 · Combate tab-target
- **Contexto:** los skillshots requieren compensación de latencia y hitboxes precisas; tab-target es fácil de
  validar en servidor y es el estilo de Heartwood/WoW clásico.
- **Decisión:** selección de objetivo + hotbar; el servidor resuelve impactos (sin proyectiles físicos:
  los proyectiles son solo visuales con `travelMs` calculado por distancia).
- **Consecuencias:** combate menos "de acción"; mucho más robusto y rápido de construir.

## ADR-004 · Tiled como fuente de verdad del mundo
- **Decisión:** mapas en Tiled (`.tmj`). Servidor lee capa `collision` y capa de objetos `spawns`/`npcs`/`graveyards`.
  Cliente importa con el plugin **YATI** (Yet Another Tiled Importer) para Godot 4.
- **Consecuencias:** un solo archivo alimenta ambos lados. Convenciones de capas en skill `world-maps`.

## ADR-005 · Contenido data-driven con JSON Schema
- **Decisión:** clases, hechizos, items, monstruos y botín en `content/*.json`, validados por JSON Schema
  (draft 2020-12) + validación de referencias cruzadas en `tools/ContentValidator`.
- **Consecuencias:** agregar contenido no requiere código; el sistema de efectos debe ser genérico.

## ADR-006 · Un hilo de simulación
- **Decisión:** el mundo lo muta un solo hilo (GameLoop). IO y persistencia se comunican por `Channel<T>` y DTOs
  inmutables.
- **Consecuencias:** cero condiciones de carrera en lógica de juego; handlers de red nunca tocan `World`.
