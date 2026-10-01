# E8 · Contenido y arte

### HU-080 · Mapa "meadow" completo (Tier 1)
**Como** jugador **quiero** un mundo variado con zonas por nivel **para** explorar mientras progreso.
- Prioridad: Must · Estimación: L · Estado: Pendiente
- Dependencias: HU-031, HU-055
- Skills: `world-maps`, `pixel-art-assets`

**Criterios de aceptación**
1. **Dado** `maps/meadow.tmj` **entonces** contiene las zonas del Tier 1 del GDD (Aldea Robledal `safe`, Campos 1–3, Colinas 3–5 y la entrada a la Mina) con capas y propiedades de la skill `world-maps`. Cada zona se cruza a pie en 60–90 s (`rules.world.zoneCrossTimeSecTarget`, ~100×100 tiles útiles), tiene un punto de referencia visible, un sendero principal, 2–3 campamentos con subniveles (más bajos cerca de la entrada) y al menos una rama lateral con recompensa.
2. **Dado** los spawns **entonces** hay suficientes monstruos para que 5 jugadores suban del 1 al 5 sin esperar respawns (≥ 20 slimes, 20 jabalíes, 15 bandidos, 20 lobos, 15 goblins).
3. **Dado** el mapa **entonces** hay un punto seguro (`graveyards`) por zona (aldea, campos, colinas), el vendedor en la aldea y el portal a `mine` al final de las Colinas (`minLevel: 4`).
4. **Dado** un recorrido a pie **entonces** no hay zonas inaccesibles ni huecos en las colisiones (verificado con un test de flood-fill desde el pueblo).

---

### HU-081 · Arte de clases y monstruos
**Como** jugador **quiero** personajes y monstruos con animaciones **para** que el juego se vea bien.
- Prioridad: Must · Estimación: L · Estado: Pendiente
- Dependencias: HU-024
- Skills: `pixel-art-assets`

**Criterios de aceptación**
1. **Dado** las 4 clases y los 8 monstruos del Tier 1 (`monsters.json`: Slime, Jabalí, Bandido, Lobo, Goblin arquero, Kóbold, Gólem y Capataz Grask) **entonces** cada uno tiene `idle`, `walk`, `attack` o `cast`, `hurt`, `death` en las direcciones de la skill.
2. **Dado** el script `build_sprite_frames.gd` **entonces** genera los `SpriteFrames` desde hoja + JSON de metadatos.
3. **Dado** `client/assets/CREDITS.md` **entonces** lista origen y licencia de cada asset.

---

### HU-082 · Íconos de items y hechizos
**Como** jugador **quiero** íconos claros **para** reconocer mis hechizos y objetos de un vistazo.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-038, HU-051
- Skills: `pixel-art-assets`, `game-content`

**Criterios de aceptación**
1. **Dado** cada `icon` referenciado en `content/*.json` **entonces** existe su PNG 16×16 (test que recorre el contenido y comprueba archivos).
2. **Dado** un ícono faltante en tiempo de ejecución **entonces** se muestra un ícono "?" y se loguea un warning (no crashea).

---

### HU-083 · Mina Abandonada y jefe Capataz Grask
**Como** grupo de nivel 4–6 **queremos** una cueva con jefe **para** tener el objetivo final del Tier 1 y botín raro.
- Prioridad: Must · Estimación: L · Estado: Pendiente
- Dependencias: HU-027, HU-036, HU-062, HU-080, HU-086
- Skills: `world-maps`, `combat-system`, `game-content`

**Criterios de aceptación**
1. **Dado** `maps/mine.tmj` (mapa aparte, ADR-007) **entonces** tiene 3–5 salas: entrada → Sala 1 (kóbolds) → Sala 2 (kóbolds + puzle simple: palancas) → rama lateral con la sala del jefe, y Sala 3 (Gólem élite) → salida hacia el Tier 2 (cerrada en la Fase 1). Recorrido 5–10 min; paleta marrón del tileset interior.
2. **Dado** el jefe **entonces** usa Golpe de pico (área marcada en el suelo sobre la posición de su objetivo, esquivable; HU-086) cada 10 s, Latigazo (sangrado) sobre alguien que no sea el tanque cada 8 s, y ¡A trabajar! (+25 % daño) bajo el 50 %; es inmune a aturdir, raíz y ralentizar.
3. **Dado** 3 jugadores de nivel 4 con equipo verde **entonces** el combate dura 60–100 s y lo ganan con un sanador o con pociones (el jefe tiene 1 400 de vida: con 2 000 el modelo de `tools/balance/` daba 117–137 s; ver `balance-notes.md` §4); 2 jugadores de nivel 6 también lo ganan (el caso de un nivel 7 con uno de 5 se valida al abrir la Fase 2, porque el tope de la Fase 1 es 6) (`rules.boss`; verificado por `content-designer` y una partida de prueba).
4. **Dado** su muerte **entonces** suelta exactamente un raro de su `groups` (Pico, Peto o Amuleto), asignado al azar a un miembro, y se anuncia en el chat global con el nombre del ganador.

---

### HU-084 · Pasada de balance
**Como** diseñador **quiero** revisar números con datos **para** que ninguna clase sea inútil o rota.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-041, HU-052, HU-062
- Skills: `game-content`, `combat-system`

**Criterios de aceptación**
1. **Dado** el subagente `content-designer` **entonces** produce `docs/design/balance-report.md` con las tablas de su definición, incluida la de afinidad (piso de viabilidad 55–65 % daño / 50–60 % aguante) el triángulo PvP (duelos simulados 1 vs 1 con equipo igual: la clase favorecida gana 60–75 %), la XP por hora en solitario de cada clase contando descansos (diferencia ≤ 15 %), si el Mago y el Sacerdote llegan con maná al final del jefe, y las horas estimadas del 1 al 15 (objetivo 20–30 h). Todos los márgenes salen de `rules.balanceTargets`.
2. **Dado** el informe **entonces** las desviaciones fuera de rango se corrigen en `content/` con commit `content(balance): ...` justificado.
3. **Dado** una sesión de juego con amigos **entonces** se cronometra, para un jugador nuevo, el tiempo desde abrir el enlace hasta la primera pelea en grupo con un amigo (objetivo ≤ 5 min), se recogen sensaciones en `docs/design/playtest-notes.md` y se crean HUs para lo que requiera código.
4. **Dado** los pendientes del modelo de la Fase 1 (`balance-report.md`, 2026-09-30) **entonces** se contrastan jugando y se cierran o se abren HUs, sin cambiar sus números antes:
   - El **triángulo de duelos** y la **tabla del Sacerdote** de `balance-notes.md` §6 (y los cooldowns citados en `gdd.md` §Triángulo) usan números anteriores al balance de la Fase 1; se miden con duelos reales o simulados con movimiento.
   - El **Mago solo con básicos** pierde el 65 % de la vida contra el Kóbold minero (piso: 50 %); el modelo no cuenta que se aleje mientras castea. **Primera palanca:** +10 de vida base del Mago (`classes.json`), que también
     sube su aguante fuera de rol (hoy 49 % del Guerrero con placas y escudo, bajo el piso del 50 % de `offRoleSurvivalPct`) y su
     punta de armadura en el pentagrama (medida 23, objetivo 25).
   - El **Capataz con Guerrero + Sacerdote de nivel 6** dura ~101 s (objetivo 60–100 s) y **3 de nivel 4 sin sanador** ~54 s; ver si la vida de 1 400 se queda.
   - Confirmar que el **ciclo real por monstruo** es de ~36 s (`killCycleSecTarget`); si no, se cambia ese valor y la curva de XP se recalcula sola (ADR-017).
