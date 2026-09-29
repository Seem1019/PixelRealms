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

## ADR-007 · Varios mapas desde el inicio; mazmorras como mapa aparte con portal
- **Contexto:** el mundo crece a 3 tiers con cuevas de transición. Se quiere poder pasar a instancias por grupo sin rediseñar.
- **Decisión:** el servidor separa **datos estáticos del mapa** (`MapData`: colisión, spawns, portales, zonas; inmutable,
  compartido) del **estado dinámico** (`MapInstance`: monstruos vivos, jugadores, loot). El `GameLoop` recorre una
  colección de `MapInstance` activas. Las zonas abiertas de un tier viven en un solo mapa; cada mazmorra es un `mapId`
  propio al que se entra por portal (`ChangeMap`: sacar al jugador de una instancia, meterlo en otra, `Welcome`-like con el mapa nuevo).
  MVP: una sola `MapInstance` por `mapId`. Instancias por grupo = permitir N `MapInstance` del mismo `MapData` (post-MVP).
- **Plan B:** si el portal se encarece, la mazmorra va como región aislada del mapa principal (solo la entrada es caminable) y se migra copiando la región.
- **Consecuencias:** AOI, amenaza y chat `say` están acotados por instancia; `characters.map_id` ya existe; `EntitySpawn` y `Snapshot` no cambian.

## ADR-008 · Toda constante numérica en `content/rules.json`
- **Contexto:** GCD, curva de XP, bonos de grupo, fórmulas de derivados, afinidades y reglas de PvP estaban repartidos entre docs y (futuro) código.
- **Decisión:** un único `rules.json` validado por `rules.schema.json`, cargado como `RulesDb` inmutable e inyectado en
  los sistemas; el cliente lo lee para predicción y UI. Recarga en caliente con `/reload rules` (admin). Se prefiere
  archivo sobre tabla en BD: mismo pipeline que el resto del contenido, versionado en git, sin UI extra.
- **Consecuencias:** ningún número mágico en `PixelRealms.Game`; los tests de fórmulas leen las constantes del mismo archivo.

## ADR-009 · Equipamiento libre con afinidad y matriz de conversión por clase
- **Contexto:** se quiere que cualquier clase pueda equipar cualquier cosa y jugar sola, pero peor que el especialista.
- **Decisión:** se eliminan las restricciones de tipo por clase. Dos mecanismos, ambos en `rules.json`:
  (1) **afinidad** alta/media/baja por clase y tipo de item, que multiplica toda la contribución del item (1.0/0.85/0.7);
  (2) **matriz de conversión** stat → `attackPower`/`spellPower`, `hpPerSta`, `armorMult`, `haste` por clase.
  El ataque básico usa el poder que corresponde al `scaling` del arma (str/agi físico, int mágico).
- **Alternativas descartadas:** solo afinidad (insuficiente: las bases de stats hacen que un Sacerdote con espada rinda
  el 30 % de un Pícaro); penalizar con `-X %` plano por "fuera de rol" (opaco para el jugador).
- **Consecuencias:** desaparecen `wrong_class`/`cannot_equip`; el tooltip muestra la afinidad; el validador comprueba que la matriz cubre todos los tipos.

## ADR-010 · Una sola escuela mágica en el MVP
- **Decisión:** `school ∈ {physical, magic}`. Los nombres de los hechizos conservan el sabor (fuego, escarcha, sombras)
  pero no hay resistencias por elemento. Añadir elementos después = ampliar el enum y las auras de resistencia; el
  motor no cambia.

## ADR-011 · PvP por `PvpRuleset`
- **Decisión:** toda regla de PvP (consentimiento, fin de combate, penalizaciones, zonas) es un `PvpRuleset` en
  `rules.pvp`. El servidor decide si A puede dañar a B preguntando `PvpService.CanAttack(a, b) → ruleset?`; el combate
  reutiliza el pipeline normal con `classAdvantage` aplicado. MVP: `duel` (consentimiento, fin al 1 % de vida,
  restauración, sin pérdidas, permitido en zonas seguras). PvP grupal/abierto/facciones = rulesets nuevos.

## ADR-012 · Botín asignado por item al azar, con cadáver visible para todos
- **Decisión:** cada item que cae se asigna (uniforme) a un miembro elegible del grupo; el cadáver brilla solo para
  quien ganó algo, todos pueden ver el contenido, solo el dueño toma su item; tras `exclusiveSec` queda libre.
  Botín garantizado de jefes mediante `groups` (uno-de-N por peso). El intercambio entre jugadores entra en el MVP.
- **Alternativas descartadas:** botín libre (peleas por el item), turnos/round-robin y "al que menos ha recibido" (previsibles, menos emoción).
