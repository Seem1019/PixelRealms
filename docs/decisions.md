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

## ADR-003 · Combate tab-target — Reemplazada por ADR-015
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

## ADR-013 · MVP en 3 fases con tope de nivel por fase
- **Contexto:** el MVP completo va del nivel 1 al 15 en 3 tiers. Se quiere algo jugable pronto, no un MVP eterno.
- **Decisión:** el MVP se construye en 3 fases, una por tier; cada fase termina jugable y probada con amigos antes de empezar
  la siguiente. `rules.world.currentPhase` indica la fase activa y `rules.progression.levelCapByPhase` (`[6, 10, 15]`) el tope
  de nivel efectivo. `maxLevel` (15) sigue siendo el tope absoluto que usan el validador y la BD.
- **Consecuencias:** abrir una fase = subir `currentPhase` y añadir su contenido, sin migración. Lo que solo sirve a fases
  futuras no se generaliza antes de tiempo (pilar 6 del GDD).

## ADR-014 · Barra 4+4, hechizos por rangos y maná por golpe
- **Contexto:** pilar 3 (pocos hechizos que mejoran, builds por elección) y pilar 4 (ningún caster se queda sin maná a mitad
  de un jefe).
- **Decisión:** máximo 8 hechizos por clase; se equipan 4 más 4 utilizables (`rules.loadout`). Los hechizos suben de rango con
  el nivel: automático en la Fase 1, mejora 1-de-2 desde la Fase 2 y una forma de reiniciarlas antes de cerrar el MVP. Todo
  personaje con maná recupera `maxMana · manaPerBasicHitPctPerSec · swingMs / 1000` por básico que impacta, con cualquier arma;
  el swing se pausa mientras se castea.
- **Alternativas descartadas:** hechizo canalizado de maná (ocupa uno de los 4 huecos); solo pociones (una por combate de jefe);
  maná solo con varita o bastón (castigaba el equipo libre).
- **Consecuencias:** `SetHotbar` pasa a slots 0–7, `character_hotbar.slot` a 0..7, `LevelUp` gana `rankUps` (opcional).

## ADR-015 · Combate híbrido: tab-target para un objetivo, áreas apuntadas
- **Contexto:** se quiere posicionamiento y esquiva, al estilo de Albion Online, sin perder la robustez del tab-target en servidor.
- **Decisión:** el ataque básico, los hechizos de un objetivo (daño, cura, control), Provocar y Carga siguen siendo tab-target.
  Los hechizos de área se apuntan a un punto del suelo (`ground_aoe_enemies|allies|all`) o se lanzan alrededor del lanzador
  (`self_aoe_*`). El cliente envía `targetPos`; el servidor valida alcance y LOS, fija el punto en `CastStarted` (todos ven la
  marca) y resuelve con quien esté dentro al terminar el casteo. Los monstruos siguen la misma regla. Sin fuego amigo. Los
  proyectiles siguen siendo solo visuales (como en ADR-003).
- **Alternativas descartadas:** todo con skillshots (compensación de latencia e hitboxes: mucho más código); tab-target puro
  (áreas sin esquiva ni posicionamiento).
- **Consecuencias:** `target_aoe_enemies` desaparece (HU-086); `CastSpell` y `CastStarted` ganan campos opcionales; los kits se
  rediseñan (daño en área para Guerrero y Pícaro). Un área apuntada con `castMs = 0` no se puede esquivar: las de daño llevan casteo.

## ADR-016 · Habilidades por clase; de Albion solo el combate híbrido
- **Contexto:** se evaluó Albion Online como referencia (habilidades por arma y por pieza de armadura, energía común,
  casillas por tipo de habilidad).
- **Decisión:** las habilidades salen de la **clase**, que define el rol (pilar 4). De Albion solo se toma el combate de
  acción de ADR-015, ampliado con **formas** de área (`shape`: círculo, cono, línea) y **saltos a un punto** (`leap`). Cada clase
  tiene un grupo de hasta 8 habilidades y el jugador equipa 4 **libremente**, sin casillas con tipo. Los saltos los mueve el
  servidor; el cliente no los predice y solo suaviza la posición recibida.
- **Alternativas descartadas:** habilidades por arma (choca con "clase = rol" y multiplica los kits), habilidades de casco,
  pecho y botas, energía común para todas las clases, casillas golpe/control/definitiva/rol.
- **Consecuencias:** el schema define ya `shape` y el efecto `leap`; el círculo se implementa en la Fase 1 y el cono y la línea
  cuando un hechizo los use (HU-086, HU-087). El balance entre clases se hace por clase, no por casilla.

## ADR-017 · Curva de XP por tiempo
- **Contexto:** con una curva fijada en XP, las horas del 1 al 15 dependían de algo no controlado (tiempo por kill) y salían
  4–7 h de combate frente al objetivo de 20–30 h.
- **Decisión:** la curva se diseña en **minutos por nivel** (`rules.progression.minutesPerLevel`, 25,2 h en total) y la XP se
  calcula con `killCycleSecTarget` (30 s) y la XP del monstruo normal del nivel. `xpRate` es un multiplicador global. Reemplaza
  a `xpCurveK`, `xpCurveKByLevel` y `xpCurveExponent`.
- **Consecuencias:** si el tiempo real por kill cambia, se ajusta un número. El contenido futuro que da XP se presupuesta en
  minutos equivalentes. Un tier nuevo añade filas a la tabla.

## ADR-018 · Estabilidad y rendimiento del combate
- **Contexto:** formas, saltos y muchas áreas superpuestas sobre un servidor de 20 Hz con un solo hilo de mundo. Objetivos que
  ya existían: tick p99 < 10 ms con 50 jugadores y 300 monstruos, < 30 KB/s por cliente.
- **Decisión:**
  - **Áreas:** se buscan con la rejilla AOI de la instancia (celdas de 16×16 casillas): rectángulo envolvente → celdas →
    candidatos → prueba de forma. Una rejilla más fina (4×4) solo si el benchmark la pide. Pruebas sin raíces ni
    trigonometría, con radios y cosenos precalculados al cargar el contenido: círculo por distancia al cuadrado; cono por
    producto escalar (`dot ≥ 0` y `dot² ≥ |d|²·cos²(α/2)`); línea por proyección (`0 ≤ t ≤ L`, distancia lateral ≤ ancho/2).
    Línea de visión desde el centro solo para los candidatos que pasan la forma, ordenados por cercanía y cortados en
    `maxTargets`. Las áreas instantáneas se evalúan una vez al resolverse; las que duran, cada `persistentAreaTickMs` (500 ms)
    repartidas entre ticks. **Las áreas solo interactúan con entidades, nunca entre sí.**
  - **Límites (`rules.limits`):** 1 casteo por lanzador; 2 áreas duraderas por lanzador (la tercera reemplaza a la más
    antigua); 128 áreas por instancia (al tope, el jugador recibe `area_limit` y el monstruo elige otra acción); 256 impactos
    pendientes por instancia; tope global de 10 objetivos por área. **El tope de auras por entidad está pendiente de revisión.**
  - **Memoria y tick:** reservas de capacidad fija para áreas, impactos y auras; eventos del tick como estructuras en un buffer
    circular; listas de resultados reutilizadas (256 ids); sin LINQ ni closures en los sistemas. Presupuesto: tick p99 ≤ 10 ms;
    combate (casteo, auras, áreas, IA) ≤ 4 ms p99 por instancia; aviso > 25 ms; fallo si algún tick > 50 ms. Memoria nueva
    ≤ 1 MB/s bajo carga y ninguna recolección completa (Gen2) durante la prueba.
  - **Red:** `CastStarted` lleva `targetPos` y `dir`; forma y tamaño salen del contenido del cliente. Si llegan áreas
    duraderas: `AreaSpawn{areaId, spellId, pos, dir, expiresInMs}` / `AreaDespawn{areaId}`. Nada por tick para las áreas.
    Un `CombatEvents{tick, e:[…]}` por observador y tick (máx. 64 entradas) en lugar de un mensaje por golpe; solo a quien ve
    al atacante o al objetivo. Presupuesto: ≤ 30 KB/s por cliente (p95), picos ≤ 40 KB/s.
  - **Guardado:** vida, recurso y posición al salir, cambiar de mapa, subir de nivel, completar un intercambio, cambiar de
    clase y morir, y cada 60 s si hubo cambios; nunca por tick. No se guardan cooldowns, auras ni casteos. Un personaje
    desconectado en combate sigue en el mundo hasta salir de combate, máx. `linkdeadInCombatMaxSec` (30 s), y puede morir.
  - **Cliente:** reservas precreadas (32 marcas de área, 64 proyectiles, 48 textos flotantes, 32 impactos) y máximo visible
    de 24 marcas, 48 proyectiles y 40 textos, con prioridad para lo propio y del grupo y las áreas enemigas que alcanzan al
    jugador (esas nunca se ocultan). Ticks de una misma aura agrupados; más de 6 números por entidad y segundo → uno sumado.
    Objetivo: 60 FPS en la versión web en un equipo modesto.
- **Verificación (HU-089):** escenario "Mina llena" (una instancia, 30 bots + 300 monstruos en 60×60 casillas, 40 áreas
  superpuestas, ~200 auras, un hechizo por GCD con la mitad de áreas, 5 min) y prueba de resistencia de 30 min. Fallo si:
  tick p99 > 15 ms o algún tick > 50 ms; combate p99 > 6 ms; memoria nueva > 2 MB/s o alguna Gen2; memoria +10 % en 30 min;
  salida p95 > 40 KB/s por cliente; FPS web p5 < 45. Microbenchmarks: 1 millón de pruebas de forma < 5 ms; consulta de área
  con 100 candidatos < 20 µs. Se ejecuta en cada HU que toque áreas o auras y al cerrar M2 y M5.
- **Consecuencias:** `CombatEvent` pasa a `CombatEvents` (cambio de protocolo antes de implementarlo). HU-088 y HU-089 nuevas.

## ADR-019 · Básico por arma y casteo en movimiento
- **Decisión:** el ataque básico lo da el arma equipada y no ocupa ninguna de las 4 casillas. Cada tipo de arma define alcance,
  animación y proyectil (`rules.weapons`); velocidad y daño están en el item; todos los tipos comparten presupuesto de daño
  por nivel y rareza y los de distancia rinden un 20 % menos. El básico sigue solo; un hechizo instantáneo no reinicia su
  temporizador pero abre un bloqueo de 250 ms; GCD de 1 s en los hechizos de clase; los hechizos instantáneos de clase tienen
  como mínimo 2 s de cooldown. Castear ralentiza al 50 % en vez de inmovilizar; solo cortan un casteo los controles que
  impiden castear y el efecto `interrupt` (bloqueo de 1,5 s). Otra habilidad o un salto cancela el casteo propio.
- **Alternativas descartadas:** el básico como una de las 4 habilidades (dejaba al Sacerdote sin daño al nivel 1); reiniciar el
  básico con cada hechizo (castiga a los melee); castear inmóvil (sin kiting en el combate de acción).
- **Consecuencias:** cambian HU-032, HU-033 y HU-022 (casos de movimiento a velocidad reducida) y el protocolo (`CastEnded`
  con `failed` y motivo).

## ADR-020 · Balance por pentagrama y regla 40/75
- **Decisión:** cada clase se define por 5 puntas (mono-objetivo, área, control, movilidad, armadura) con 250 puntos y un
  máximo de 100 por punta (`rules.balanceTargets.pentagram`). Se mide con métricas concretas y el arma de referencia de la
  clase; el aporte de una habilidad es lo que suma sobre la base. Regla 40/75: una habilidad suma como mucho 40 puntos, una
  combinación de 4 + base no pasa del 75 % del presupuesto y ninguna supera el valor de la clase en ninguna punta. Detalle,
  armas de referencia y los 32 hechizos en `docs/design/class-kits.md`.
- **Alternativas descartadas:** casillas con tipo (balance por casilla); límites en tiempo real (etiquetas, cooldowns
  compartidos), por simplicidad (pilar 3).
- **Consecuencias:** el `content-designer` fija los números con un script que comprueba las 70 combinaciones por clase.
  `spellUnlockLevels` pasa a 1, 2, 3, 5, 7, 9, 11, 13.

## ADR-021 · Topes de auras y controles del mismo tipo
- **Contexto:** la regla inicial de ADR-018 (al tope, el aura nueva reemplaza a otra del mismo tipo) dejaba inmune a efectos
  negativos a quien tuviera 16 beneficiosas.
- **Decisión:** topes separados de 16 beneficiosas y 16 perjudiciales por entidad (`rules.limits.maxBuffsPerEntity`,
  `maxDebuffsPerEntity`). Un aura nueva siempre se aplica; si su grupo está lleno, sale la de ese grupo con menos tiempo
  restante. Los controles (`rules.combat.controlAuraKinds`) no cuentan para ningún tope. Los controles del mismo tipo no se
  suman: manda la ralentización más fuerte y el aturdimiento (o raíz) más largo.
- **Consecuencias:** en el peor caso realista una entidad lleva ~9 beneficiosas y ~1–3 perjudiciales que cuentan, así que los
  topes son una red de seguridad. Cambian HU-035 y HU-088. Se eliminan las auras huérfanas Desgarro y Escudo de maná y se
  crean las 14 del kit nuevo.
