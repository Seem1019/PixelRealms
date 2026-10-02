# E8 · Contenido y arte

### HU-080 · Mapa "meadow" completo (Tier 1)
**Como** jugador **quiero** un mundo variado con zonas por nivel **para** explorar mientras progreso.
- Prioridad: Must · Estimación: L · Estado: Hecha
- Dependencias: HU-031, HU-055
- Skills: `world-maps`, `pixel-art-assets`

**Criterios de aceptación**
1. **Dado** `maps/meadow.tmj` **entonces** contiene las zonas del Tier 1 del GDD (Aldea Robledal `safe`, Campos 1–3, Colinas 3–5 y la entrada a la Mina) con capas y propiedades de la skill `world-maps`. Cada zona se cruza a pie en 60–90 s (`rules.world.zoneCrossTimeSecTarget`, ~100×100 tiles útiles), tiene un punto de referencia visible, un sendero principal, 2–3 campamentos con subniveles (más bajos cerca de la entrada) y al menos una rama lateral con recompensa.
2. **Dado** los spawns **entonces** hay suficientes monstruos para que 5 jugadores suban del 1 al 5 sin esperar respawns (≥ 20 slimes, 20 jabalíes, 15 bandidos, 20 lobos, 15 goblins).
3. **Dado** el mapa **entonces** hay un punto seguro (`graveyards`) por zona (aldea, campos, colinas), el vendedor en la aldea y el portal a `mine` al final de las Colinas (`minLevel: 4`).
4. **Dado** un recorrido a pie **entonces** no hay zonas inaccesibles ni huecos en las colisiones (verificado con un test de flood-fill desde el pueblo).

**Notas de implementación**
- Mapa generado con `tools/maps/gen_tier1_maps.py` (determinista, seed fija): 250×110 casillas = Aldea Robledal 42×50 (`safe`, valla con puerta, pozo = landmark, Marta y Maestro Aldo, `gy_village`) + Campos 100×106 (nv 1–3, Molino, `gy_fields`) + Colinas 100×106 (nv 3–5, Roble centenario, `gy_hills`) separados por una cresta con paso de 4 casillas; sendero principal de 3 casillas con curvas y ramales de tierra a cada campamento y punto seguro; bosquecillos, arbustos, charcas y rocas aleatorios.
- Spawns: 21 slimes (3 campamentos al oeste), 21 jabalíes, 20 bandidos (2 campamentos + escondite en rama lateral), 21 lobos, 20 goblins (2 + atalaya en rama lateral); subniveles de oeste a este. Portal `meadow_to_mine` (`minLevel: 4`) en la boca de mina al final de las Colinas.
- CA4: el generador rellena cualquier bolsa inaccesible y `FloodFill_FromDefaultGraveyard_ReachesEveryWalkableTile_AndEveryObject` comprueba desde `gy_village` que toda casilla transitable, cada cementerio, NPC, portal y ≥ `count` casillas de cada spawn son alcanzables.
- **Pendiente de validar en el editor**: la skill pide ~100×100 útiles por zona, que a 4 casillas/s se cruzan en ~25 s, no 60–90 s (contradicción documentada en el resumen). El arte sigue siendo el tileset placeholder (HU-082). El nombre del landmark de las Colinas ("Roble centenario") no está en el GDD: decisión provisional.

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

**Notas de implementación (parcial)**
- CA1 **parcial**: `maps/mine.tmj` regenerado (90×60, `tools/maps/gen_tier1_maps.py`): entrada (portal + `gy_entrance`) → Sala 1 (6 kóbolds) → Sala 2 (6 kóbolds + 4 pilares) → rama lateral sur: sala del Capataz (spawn fijo) · Sala 3 (gólem élite + 3 kóbolds) → hornacina cerrada hacia el Tier 2. El **puzle de palancas no existe** (no hay mecánica de palancas en el motor: decidir si se hace HU propia); recorrido/paleta sin validar jugando; tileset placeholder.
- CA2: hechizos y objetivos del jefe (`foreman_slam` área marcada, `foreman_whip` a `random_not_top_threat`, `foreman_rally` bajo 50 %) e inmunidad a aturdir/raíz/ralentizar ya estaban en HU-036/HU-088 (`rules.boss.immuneToAuraKinds`).
- CA3: **sin validar jugando** (HU-084/partida de prueba); el modelo de `tools/balance/` es la única referencia.
- CA4: hecho con HU-062 CA4: `LootAnnouncedEvent` → `ChatMessage{global}` a todos los conectados cuando el monstruo es `boss` (test `UncommonPlus_EmitsLootAnnounced_GlobalForBoss_PartyOtherwise`). El grupo de la tabla `lt_foreman` garantiza exactamente 1 raro (`Foreman_AlwaysDropsExactlyOneGroupItem`).

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

---
### HU-090 · Animaciones de combate del cuerpo
**Como** jugador **quiero** ver a los personajes y monstruos atacar, castear, recibir golpes y caer **para** entender el combate de un vistazo.
- Prioridad: Should · Estimación: L · Estado: Hecha
- Dependencias: HU-081, HU-038
- Skills: `pixel-art-assets`, `godot-client`, `combat-system`

**Criterios de aceptación**
1. **Dado** cualquier hoja de `tools/art/gen_chars.py` **entonces** tiene `attack_<dir>` (4 frames, 12 fps, sin loop), `cast_<dir>` (3 frames en loop, 6 fps), `hurt` (2 frames) y `death` (4 frames, sin loop, queda en el último), con s, n y e (w = e espejado), los pies en `feetY` y el `.json` con las filas nuevas; `generate_all.py` dos veces da los mismos PNG.
2. **Dado** cada clase **entonces** ataca a su manera: Guerrero tajo amplio, Pícaro estocada rápida, Mago golpe de bastón con brillo, Sacerdote golpe de maza con luz; los monstruos según lo que son (slime se aplasta y salta, jabalí y lobo embisten, bandido y esqueleto tajo, arquero tensa el arco, gólem golpe pesado), y los jefes de 64×64 también.
3. **Dado** un `CombatEvents` con `src` = una entidad (o yo) **entonces** reproduce `attack` una vez, mirando a su `dst`; entre `CastStarted` y `CastEnded` reproduce `cast` mirando al objetivo o a `targetPos`; un `dmg` sobre ella reproduce `hurt` con el destello y 1 px de retroceso (no en `miss`, `dodge`, `immune`); `anim:"dead"` o `Died` reproduce `death` y queda tendida hasta despawnear o revivir.
4. **Dado** varias a la vez **entonces** manda `death` > `hurt` > `attack`/`cast` > `walk` > `idle`; una de un solo uso termina y vuelve a la de movimiento; moverse no corta un `attack` a medias.
5. **Dado** una hoja sin alguna animación de combate **entonces** `EntityVisual` cae a `idle` sin errores.

**Notas de implementación**
- Arte: `gen_chars.py` añade 13 columnas (attack 6-9, cast 10-12, hurt 13-14, death 15-18) y la tabla `anims` {column, frames, fps, loop} en cada `.json`; las poses son transformaciones exactas de píxel (desplazar, arrodillar, tumbar, aplastar filas) sobre el mismo cuerpo, con el arma y un brillo propio por clase.
- `EntitySprites` construye cada animación de la tabla (`anim_ms`, `dir_from_vector`); `EntityVisual.pick_base`/`resolve_anim` fijan la prioridad y el respaldo a idle; la muerte queda en el último cuadro y un cadáver que entra en la AOI empieza ya tendido. Sin hoja de muerte, vuelve el cuerpo gris de antes.
- Disparo: `CombatPresenter` (básicos de `CombatEvents` → attack del src; `CastStarted{durationMs>0}` → cast; `CastEnded{done}` → attack, porque el servidor también manda CastStarted+CastEnded en los instantáneos). El jugador propio usa los mismos mensajes.
- Tests: `test_combat_animations.gd` (prioridad, respaldo, hojas completas, mirar al objetivo, muerte, hoja sin muerte).

---
### HU-091 · Efectos visuales de hechizos
**Como** jugador **quiero** ver el brillo del casteo, los proyectiles, los impactos y las áreas resolviéndose **para** que el combate se sienta vivo.
- Prioridad: Should · Estimación: M · Estado: Hecha
- Dependencias: HU-090, HU-086
- Skills: `pixel-art-assets`, `godot-client`, `combat-system`

**Criterios de aceptación**
1. **Dado** `tools/art/gen_vfx.py` (llamado desde `generate_all.py`) **entonces** genera en `client/assets/sprites/vfx/` hojas de 16×16 o 32×32 de 4–6 frames en Resurrect 64: brillo de casteo por escuela, proyectiles (con frames por dirección, sin rotar), impactos físico y mágico por escuela, curación y resolución de área.
2. **Dado** un `CastStarted` **entonces** aparece el brillo bajo los pies del lanzador hasta `CastEnded`; **dado** un básico de arma a distancia **entonces** sale un proyectil del lanzador al objetivo que llega en 150–250 ms; **dado** un hechizo con proyectil **entonces** sale al terminar el casteo y tarda lo mismo que en el servidor (distancia / `projectile.speed`, mínimo 150 ms); en ambos el impacto y el número flotante aparecen a su llegada.
3. **Dado** un daño **entonces** el impacto depende de `school` (físico: tajo/chispa; mágico: estallido del color de la escuela); una cura muestra destellos verdes; un área que se resuelve muestra su efecto sobre la marca de `aoe_reticle.gd`.
4. **Dado** el efecto de un hechizo **entonces** sale de lo que ya define `content/spells.json` (`school`, `targeting`, proyectil, área) y de una tabla opcional del cliente para hechizos concretos, sin campos nuevos en `content/`.
5. **Dado** 64 entradas en un tick **entonces** los efectos se sacan de una reserva fija con tope de simultáneos (descarta los más viejos); los que caen fuera de la pantalla no se crean; se dibujan sobre suelo y entidades pero bajo placas, números y UI.

**Notas de implementación**
- `gen_vfx.py`: `cast_<elem>` (32×16, 4 f), `impact_<elem>` y `heal` (32×32, 5 f), `area_<elem>` (16×16, 5 f), `impact_slash`/`impact_spark`, y proyectiles de 8 filas por dirección (`fireball`, `frostbolt`, `shadow_bolt`, `arrow`, `bolt_arcane`, `bolt_holy`, `blade`); elementos fuego, escarcha, arcano, luz, sombra, naturaleza y acero.
- `VfxCatalog` elige con `projectile.sprite`, `school`, `targeting`, `shape` y `aoeRadius`; la tabla del cliente `ELEMENT_BY_SPELL` solo da el color a hechizos mágicos sin proyectil, y `RANGED_BASIC` el proyectil del básico a distancia (monstruos según su `attackRange`). Sin campos nuevos en `content/`.
- Cambio respecto al pedido: los hechizos con proyectil no se recortan a 150–250 ms, porque el servidor aplica el daño a distancia / velocidad (hasta ~700 ms) y el número quedaría separado del impacto.
- `VfxLayer`: reserva de 64 nodos, tope de 48 activos (recicla el más viejo y entrega su número), recorte por la vista; brillos en `VfxGround` (z −4, bajo los cuerpos) y el resto en `Vfx` (z 12, bajo placas z 40, números y HUD).
- Tests: `test_combat_animations.gd` (todas las hojas existen, tope y recorte de la reserva, número al llegar el proyectil, brillo y ataque al soltar, casteo interrumpido). Capturas en `docs/screenshots/combat/`.


### HU-092 · Fuente HD para la interfaz
**Como** jugador **quiero** leer la interfaz con una letra nítida y suave **para** no forzar la vista con la fuente pixel, sin perder el estilo de madera y pixel art.
- Prioridad: Should · Estimación: S · Estado: En curso
- Dependencias: —
- Skills: `godot-client`, `pixel-art-assets`

**Criterios de aceptación**
1. **Dado** el tema de la interfaz **entonces** el texto usa Alegreya Sans (Medium; Bold en negritas) con suavizado y los títulos (`TitleLabel`, `HeadlineLabel`) Alegreya SC; licencia OFL anotada en `assets/CREDITS.md`.
2. **Dado** el cambio **entonces** paneles 9-slice, paleta, sprites y tamaños de panel siguen igual (la altura de mayúscula a 8 px lógicos es la de la fuente anterior).
3. **Dado** texto sobre el mundo (nombres, números, chat) **entonces** lleva contorno oscuro proporcionado a la letra suave (`OUTLINE_THIN` / `OUTLINE_THICK`).

### HU-093 · Guerrero y mago con hojas dibujadas
**Como** jugador **quiero** que el guerrero y el mago tengan el aspecto de las hojas de referencia **para** que los héroes se vean más detallados.
- Prioridad: Should · Estimación: M · Estado: En curso
- Dependencias: HU-090
- Skills: `pixel-art-assets`, `godot-client`

**Criterios de aceptación**
1. **Dado** `tools/art/refs/{warrior,mage}_sheet.webp` **cuando** se ejecuta `tools/art/import_heroes.py` (también desde `generate_all.py`, después de `gen_chars`) **entonces** escribe `characters/{warrior,mage}.png/.json` con el mismo formato: 32×32, 19 columnas (reposo, caminar, ataque, casteo, golpe, muerte) × filas s, n, e; pies en y=28.
2. **Dado** cada cuadro **entonces** el fondo negro es transparente, el personaje mide 26 px de pie, los pies están centrados y lleva contorno de 1 px `#2e222f`.
3. **Dado** que las hojas no traen ataque ni casteo de espaldas **entonces** el norte repite cuadros de espaldas (limitación conocida).

**Notas de implementación**
- La asignación cuadro → animación está en la tabla `FRAMES` de `import_heroes.py`; los colores son los de la hoja, no Resurrect 64.
