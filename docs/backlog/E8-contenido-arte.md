# E8 · Contenido y arte

### HU-080 · Mapa "meadow" completo (Tier 1)
**Como** jugador **quiero** un mundo variado con zonas por nivel **para** explorar mientras progreso.
- Prioridad: Must · Estimación: L · Estado: Hecha
- Dependencias: HU-031, HU-055
- Skills: `world-maps`, `pixel-art-assets`

**Criterios de aceptación**
1. **Dado** `maps/meadow.tmj` **entonces** contiene las zonas del Tier 1 del GDD (Aldea Robledal `safe`, Campos 1–3, Colinas 3–5 y la entrada a la Mina) con capas y propiedades de la skill `world-maps`. Cada zona se cruza a pie en 25–40 s (`rules.world.zoneCrossTimeSecTarget`, ~100×100 tiles útiles a 4 tiles/s; enmendado 2026-10-03: antes decía 60–90 s, que no cuadra con ese tamaño, y un mapa compacto junta a los amigos), tiene un punto de referencia visible, un sendero principal, 2–3 campamentos con subniveles (más bajos cerca de la entrada) y al menos una rama lateral con recompensa.
2. **Dado** los spawns **entonces** hay suficientes monstruos para que 5 jugadores suban del 1 al 5 sin esperar respawns (≥ 20 slimes, 20 jabalíes, 15 bandidos, 20 lobos, 15 goblins).
3. **Dado** el mapa **entonces** hay un punto seguro (`graveyards`) por zona (aldea, campos, colinas), el vendedor en la aldea y el portal a `mine` al final de las Colinas (`minLevel: 4`).
4. **Dado** un recorrido a pie **entonces** no hay zonas inaccesibles ni huecos en las colisiones (verificado con un test de flood-fill desde el pueblo).

**Notas de implementación**
- Mapa generado con `tools/maps/gen_tier1_maps.py` (determinista, seed fija): 250×110 casillas = Aldea Robledal 42×50 (`safe`, valla con puerta, pozo = landmark, Marta y Maestro Aldo, `gy_village`) + Campos 100×106 (nv 1–3, Molino, `gy_fields`) + Colinas 100×106 (nv 3–5, Roble centenario, `gy_hills`) separados por una cresta con paso de 4 casillas; sendero principal de 3 casillas con curvas y ramales de tierra a cada campamento y punto seguro; bosquecillos, arbustos, charcas y rocas aleatorios.
- Spawns: 21 slimes (3 campamentos al oeste), 21 jabalíes, 20 bandidos (2 campamentos + escondite en rama lateral), 21 lobos, 20 goblins (2 + atalaya en rama lateral); subniveles de oeste a este. Portal `meadow_to_mine` (`minLevel: 4`) en la boca de mina al final de las Colinas.
- CA4: el generador rellena cualquier bolsa inaccesible y `FloodFill_FromDefaultGraveyard_ReachesEveryWalkableTile_AndEveryObject` comprueba desde `gy_village` que toda casilla transitable, cada cementerio, NPC, portal y ≥ `count` casillas de cada spawn son alcanzables.
- **Pendiente de validar en el editor**: la skill pide ~100×100 útiles por zona, que a 4 casillas/s se cruzan en ~25 s, no 60–90 s (contradicción documentada en el resumen). El arte sigue siendo el tileset placeholder (HU-082). El nombre del landmark de las Colinas ("Roble centenario") no está en el GDD: decisión provisional.
- 2026-10-03 (rama `feat/phase1-close-out`): CA1 completo: objetivo de cruce enmendado a 25–40 s (`rules.world.zoneCrossTimeSecTarget`) y una recompensa en cada rama lateral: el "Jabalí de guerra" (`boar_alpha`, élite nv 3, 330 de vida) en el escondite de bandidos de Campos y el "Huargo de la atalaya" (`wolf_alpha`, élite nv 5, 500 de vida) en la atalaya goblin de Colinas, con botín garantizado propio (`lt_boar_alpha`, `lt_wolf_alpha`), reaparición de 10 min y el sprite de su especie (el arte propio queda para el compañero de arte). Spawns añadidos al final del generador (`tools/maps/gen_tier1_maps.py`) para no mover nada más del mapa. Números en `docs/design/balance-report.md`.
- 2026-10-04 (rama `feat/duel-zone-and-polish`): los dos élites tienen hoja propia (`monsters/boar_alpha`, `monsters/wolf_alpha`): la silueta de su especie con otro pelaje, ojos que brillan, cresta y montura de acero o arnés de cuero (`gen_chars.beast_extras`, sin cambios en las hojas existentes).

---
### HU-081 · Arte de clases y monstruos
**Como** jugador **quiero** personajes y monstruos con animaciones **para** que el juego se vea bien.
- Prioridad: Must · Estimación: L · Estado: Parcial
- Dependencias: HU-024
- Skills: `pixel-art-assets`

**Criterios de aceptación**
1. **Dado** las 4 clases y los 8 monstruos del Tier 1 (`monsters.json`: Slime, Jabalí, Bandido, Lobo, Goblin arquero, Kóbold, Gólem y Capataz Grask) **entonces** cada uno tiene `idle`, `walk`, `attack` o `cast`, `hurt`, `death` en las direcciones de la skill.
2. **Dado** una hoja `<ref>.png` con su `<ref>.json` (`frameSize`, `anims`) **entonces** `scripts/world/entity_sprites.gd` genera los `SpriteFrames` al cargarla (enmendado el 2026-10-04: desde ADR-026 se hace al cargar y el script `build_sprite_frames.gd` no se llegó a crear).
3. **Dado** `client/assets/CREDITS.md` **entonces** lista origen y licencia de cada asset.
- 2026-10-04 (rama `feat/duel-zone-and-polish`): CA1 lo cubren HU-090/HU-099 (`test_combat_animations.gd`) y CA2 queda enmendado. Falta CA3: el origen y la licencia de las hojas de referencia `tools/art/refs/*_sheet.webp` en `CREDITS.md` (lo cierra el compañero de arte).

---

### HU-082 · Íconos de items y hechizos
**Como** jugador **quiero** íconos claros **para** reconocer mis hechizos y objetos de un vistazo.
- Prioridad: Must · Estimación: M · Estado: Hecha
- Dependencias: HU-038, HU-051
- Skills: `pixel-art-assets`, `game-content`

**Criterios de aceptación**
1. **Dado** cada `icon` referenciado en `content/*.json` **entonces** existe su PNG 16×16 (test que recorre el contenido y comprueba archivos).
2. **Dado** un ícono faltante en tiempo de ejecución **entonces** se muestra un ícono "?" y se loguea un warning (no crashea).
- 2026-10-04 (rama `feat/duel-zone-and-polish`): CA2: `UiTheme.icon` devuelve un "?" de 16×16 dibujado en código y avisa una vez por referencia en el log cuando falta el PNG, sin casillas vacías que parezcan otra cosa (`test_ui_theme.gd`). CA1 ya lo cubría `test_visual_redesign.gd`.

---

### HU-083 · Mina Abandonada y jefe Capataz Grask
**Como** grupo de nivel 4–6 **queremos** una cueva con jefe **para** tener el objetivo final del Tier 1 y botín raro.
- Prioridad: Must · Estimación: L · Estado: Hecha
- Dependencias: HU-027, HU-036, HU-062, HU-080, HU-086
- Skills: `world-maps`, `combat-system`, `game-content`

**Criterios de aceptación**
1. **Dado** `maps/mine.tmj` (mapa aparte, ADR-007) **entonces** tiene 3–5 salas: entrada → Sala 1 (kóbolds) → Sala 2 (kóbolds + puzle simple: palancas) → rama lateral con la sala del jefe, y Sala 3 (Gólem élite) → salida hacia el Tier 2 (cerrada en la Fase 1). Recorrido 5–10 min; paleta marrón del tileset interior.
2. **Dado** el jefe **entonces** usa Golpe de pico (área marcada en el suelo sobre la posición de su objetivo, esquivable; HU-086) cada 10 s, Latigazo (sangrado) sobre alguien que no sea el tanque cada 8 s, y ¡A trabajar! (+25 % daño) bajo el 50 %; es inmune a aturdir, raíz y ralentizar.
3. **Dado** 3 jugadores de nivel 4 con equipo verde **entonces** el combate dura 60–100 s y lo ganan con un sanador o con pociones (el jefe tiene 1 400 de vida: con 2 000 el modelo de `tools/balance/` daba 117–137 s; ver `balance-notes.md` §4); 2 jugadores de nivel 6 también lo ganan (el caso de un nivel 7 con uno de 5 se valida al abrir la Fase 2, porque el tope de la Fase 1 es 6) (`rules.boss`; verificado por `content-designer` y una partida de prueba).
4. **Dado** su muerte **entonces** suelta exactamente un raro de su `groups` (Pico, Peto o Amuleto), asignado al azar a un miembro, y se anuncia en el chat global con el nombre del ganador.

**Notas de implementación (parcial)**
- CA1 **parcial**: `maps/mine.tmj` regenerado (90×60, `tools/maps/gen_tier1_maps.py`): entrada (portal + `gy_entrance`) → Sala 1 (6 kóbolds) → Sala 2 (6 kóbolds + 4 pilares) → rama lateral sur: sala del Capataz (spawn fijo) · Sala 3 (gólem élite + 3 kóbolds) → hornacina cerrada hacia el Tier 2. El **puzle de palancas no existe** (no hay mecánica de palancas en el motor: decidir si se hace HU propia); recorrido/paleta sin validar jugando; tileset placeholder.
- CA2: hechizos y objetivos del jefe (`foreman_slam` área marcada, `foreman_whip` a `random_not_top_threat`, `foreman_rally` bajo 50 %) e inmunidad a aturdir/raíz/ralentizar ya estaban en HU-036/HU-088 (`rules.combat.bossImmuneToAuraKinds`).
- CA3: **sin validar jugando** (HU-084/partida de prueba); el modelo de `tools/balance/` es la única referencia.
- CA4: hecho con HU-062 CA4: `LootAnnouncedEvent` → `ChatMessage{global}` a todos los conectados cuando el monstruo es `boss` (test `UncommonPlus_EmitsLootAnnounced_GlobalForBoss_PartyOtherwise`). El grupo de la tabla `lt_foreman` garantiza exactamente 1 raro (`Foreman_AlwaysDropsExactlyOneGroupItem`).
- 2026-10-03 (rama `feat/phase1-close-out`): CA1 completo: puzle de la Sala 2 con dos palancas (`mine_lever_west`, `mine_lever_east`) que abren la puerta del pasillo de la sala del jefe (`mine_boss_door`); se cierra sola a los `rules.world.doorResetSec` (600 s) si no hay nadie debajo. Capas `levers`/`doors` en Tiled (generador), `MapObjectSystem` en el servidor (colisión propia de la instancia, puertas cerradas sólidas y opacas), mensajes `Interact` y `MapObjects`, y en el cliente `TmjMap` las aplica a la predicción y `MapObjectsLayer` las dibuja (placeholder hasta tener sprites). CA3 con el modelo de `tools/balance/` (1 400 de vida): 3 de nivel 4 con sanador 81–86 s, sin sanador 54 s (el tanque necesita poción y pan), 2 de nivel 6 ganan en 53–105 s; ver `balance-report.md`. **Falta** la partida de prueba que pide CA3.
- 2026-10-03 (revisión de autoridad): un grupo podía quedarse encerrado con el jefe si la puerta se cerraba con él dentro. Ahora hay una palanca dentro de la sala que la abre sola (`mine_lever_inside`, `opensAlone`), tirar de una palanca con la puerta abierta renueva el plazo, la puerta no se cierra con alguien saltando a través y el cargador rechaza ids repetidos (`MapObjectSystemTests`, `TiledMapLoaderTests.RepeatedLeverOrDoorId_Fails`).
- 2026-10-04 (rama `feat/duel-zone-and-polish`): palancas y puerta con sprites (`sprites/objects/lever.png` y `door.png`, `tools/art/gen_objects.py`) en lugar del dibujo provisional; `MapObjectsLayer` lo conserva solo si faltan las hojas (`test_map_objects.gd`).
- 2026-10-05 (prueba local, rama `feat/duel-zone-and-polish`): en la prueba, la puerta se abría lejos y sin avisar, y la abierta apenas se ve: parecía que las palancas no hacían nada. Ahora la primera palanca dice "Palanca activada (1/2): falta otra para abrir la puerta", al abrirse se oye "Se oye un mecanismo: se ha abierto una puerta" (con polvo en la puerta si se ve) y al cerrarse "Una puerta se ha cerrado"; el estado al entrar en el mapa no se anuncia (`GameState.map_object_toggled`, `test_world_scene.gd`, `test_map_objects.gd`). `TiledMapLoaderTests.Mine_ClosedBossDoor_IsTheOnlyWayToTheBoss` comprueba que la puerta cerrada tapa el único camino al jefe.
- 2026-10-05 (prueba local): las palancas estaban en la fila 22 y la puerta en la 38, a más de una pantalla: nadie las relacionaba. Ahora la puerta está en la boca del pasillo (fila 36) y las palancas a cada lado, en (51.5, 32.5) y (62.5, 32.5), visibles a la vez (`gen_tier1_maps.py`; `Mine_ClosedBossDoor_IsTheOnlyWayToTheBoss` sigue en verde).
- 2026-10-06: cerrada con la partida de prueba con amigos (Diego): el jefe y el puzle de la Mina (CA3), OK jugando; no se apuntaron tiempos ni cifras (`docs/design/playtest-notes.md`).

---

### HU-084 · Pasada de balance
**Como** diseñador **quiero** revisar números con datos **para** que ninguna clase sea inútil o rota.
- Prioridad: Must · Estimación: M · Estado: Hecha
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
- 2026-10-03 (rama `feat/phase1-close-out`): CA1 y CA2 hechos por `content-designer`: `balance-report.md` (pasada de HU-084) con afinidad, XP por hora, pentagrama, economía y el triángulo PvP simulado (1 000 duelos por pareja a nivel 6 con equipo verde). Con `classAdvantage` todo en 1.0 los duelos salían 0–100 %; la matriz nueva (`content/rules.json`) deja a cada favorito en 63–68 %. Depende de supuestos (kiteo, ejecución): confirmarlo jugando. Commit propuesto: `content(balance): tune PvP classAdvantage so duel favourites win 60-75% (HU-084)`. **Faltan** CA3 y CA4 (partida con amigos).
- 2026-10-04 (rama `feat/duel-zone-and-polish`): plantilla de la sesión con amigos en `docs/design/playtest-notes.md` (CA3 y CA4, con HU-083 CA3 y HU-089 CA3).
- 2026-10-06: cerrada con la partida de prueba con amigos (Diego): el primer contacto, las sensaciones y los pendientes del modelo (CA3 y CA4), OK jugando; no se apuntaron tiempos ni cifras (`docs/design/playtest-notes.md`).

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
- Prioridad: Should · Estimación: S · Estado: Hecha
- Dependencias: —
- Skills: `godot-client`, `pixel-art-assets`

**Criterios de aceptación**
1. **Dado** el tema de la interfaz **entonces** el texto usa Alegreya Sans (Medium; Bold en negritas) con suavizado y los títulos (`TitleLabel`, `HeadlineLabel`) Alegreya SC; licencia OFL anotada en `assets/CREDITS.md`.
2. **Dado** el cambio **entonces** paneles 9-slice, paleta, sprites y tamaños de panel siguen igual (la altura de mayúscula a 8 px lógicos es la de la fuente anterior).
3. **Dado** texto sobre el mundo (nombres, números, chat) **entonces** lleva contorno oscuro proporcionado a la letra suave (`OUTLINE_THIN` / `OUTLINE_THICK`).

**Notas de implementación** (escritas en la auditoría del 2026-10-02, a partir de `2e4988d`)
- `scripts/ui/ui_theme.gd` carga Alegreya Sans y Alegreya SC de `assets/fonts/` con antialias gris; reemplaza a Tiny5 de
  ADR-026. Los contornos del texto del mundo usan `OUTLINE_THIN` / `OUTLINE_THICK`.
- Test: `test_ui_theme.gd::test_theme_uses_the_hd_font_smoothed`.

### HU-093 · Guerrero y mago con hojas dibujadas
**Como** jugador **quiero** que el guerrero y el mago tengan el aspecto de las hojas de referencia **para** que los héroes se vean más detallados.
- Prioridad: Should · Estimación: M · Estado: Hecha
- Dependencias: HU-090
- Skills: `pixel-art-assets`, `godot-client`

**Criterios de aceptación**
1. **Dado** `tools/art/refs/{warrior,mage}_sheet.webp` **cuando** se ejecuta `tools/art/import_heroes.py` (también desde `generate_all.py`, después de `gen_chars`) **entonces** escribe `characters/{warrior,mage}.png/.json` con el mismo formato: 32×32, 19 columnas (reposo, caminar, ataque, casteo, golpe, muerte) × filas s, n, e; pies en y=28.
2. **Dado** cada cuadro **entonces** el fondo negro es transparente, el personaje mide 26 px de pie, los pies están centrados y lleva contorno de 1 px `#2e222f`.
3. **Dado** que las hojas no traen ataque ni casteo de espaldas **entonces** el norte repite cuadros de espaldas (limitación conocida).

**Notas de implementación**
- La asignación cuadro → animación está en la tabla `FRAMES` de `import_heroes.py`; los colores son los de la hoja, no Resurrect 64.

### HU-099 · Héroes en alta resolución
**Como** jugador **quiero** que los cuatro héroes se vean con el detalle y las animaciones de sus hojas dibujadas **para** que no pierdan calidad al reducirlos a 32 px.
- Prioridad: Should · Estimación: M · Estado: Hecha
- Dependencias: HU-093
- Skills: `pixel-art-assets`, `godot-client`

**Criterios de aceptación**
1. **Dado** `tools/art/refs/<clase>_sheet.webp` **cuando** se ejecuta `import_heroes.py` **entonces** escribe una hoja HD: cuadros de 120×120 (40×40 lógicos), pies en y=108, personaje de 78 px (26 lógicos) y `pixelScale: 3` en el `.json`.
2. **Dado** una hoja con `pixelScale` **entonces** el cliente la dibuja a 1/3: mide lo mismo en el mundo que antes, con el detalle de la ventana de 1440×810; la placa queda a la altura de siempre y los retratos cubren el mismo área lógica.
3. **Dado** cada héroe **entonces** tiene reposo (4 cuadros), caminar (8), ataque (6), casteo (6), golpe (2) y muerte (4) en s, n y e.
4. **Dado** una clase sin hoja de referencia **entonces** sigue con la procedural a escala 1.

**Notas de implementación**
- Detección de cuadros robusta a brillos que unen cuadros vecinos (parte el tramo por su línea menos densa) y fondo encerrado por efectos (negro casi puro y grande) transparente.
- El pícaro usa un umbral de fondo más bajo (`BG_LEVEL_BY_CLASS`): su ropa casi negra se borraba con el fondo.
- La fila norte del sacerdote sale de PixelLab (`refs/priest_pixellab/`, cuadros `pl:` en `FRAMES`), escalada por su propio reposo.
- El ataque y el casteo de espaldas del guerrero y el mago salen de PixelLab animando desde un cuadro de espaldas de su propia hoja (`custom_start_frame`), así conservan capa y sombrero; ya están a escala de la hoja HD.

---

### HU-106 · Números de los hechizos de nivel 7 y 9
**Como** diseñador **quiero** fijar los números de los 8 hechizos nuevos de la Fase 2 **para** que entren medidos y no provisionales.
- Prioridad: Must · Estimación: M · Estado: Hecha
- Dependencias: HU-102
- Skills: `game-content`, `combat-system` (subagente `content-designer`)

**Criterios de aceptación**
1. **Dado** el modelo de `tools/balance/` ampliado al nivel 10 **entonces** las 15 combinaciones de 4 de los 6 hechizos de cada clase cumplen la regla 40/75 y ninguna supera el valor de la clase en una punta (ADR-020).
2. **Dado** Eviscerar, Cuchillas arrojadizas, Campo ardiente, Parpadeo, Bloqueo con escudo, Tajo amplio, Renovar y Sendero de luz **entonces** pasan a `"provisional": false` con su medida en `docs/design/balance-report.md`.
3. **Dado** el segundo rango (nivel 8) **entonces** el modelo lo aplica según ADR-027 (D3) y el informe dice si el rango se sigue notando con el equipo de nivel 9; si no, propone otro `spellRankBonusPct`.
4. **Dado** las áreas de daño sin casteo **entonces** Tajo amplio y Cuchillas arrojadizas cumplen ADR-027 (D4) y el validador deja de avisar de ellas.
5. **Dado** el Mago **entonces** su área llega al objetivo de su pentagrama con Campo ardiente ("su área madura en la Fase 2", `class-kits.md` §Riesgos).

**Notas técnicas**
- Los íconos de los 8 ya existen (HU-082). Campo ardiente es instantáneo: no necesita áreas duraderas (HU-100).

**Notas de implementación**
- Modelo nuevo `tools/balance/phase2.py`: pentagrama al nivel 10 con el equipo aproximado de `tier2.py` y el segundo rango (+30 % del `base`, D3), con referencias del nivel 10 definidas como las del 6. Las 15 combinaciones de cada clase cumplen la regla 40/75 (máximo: Guerrero, 177 de 187) y también del nivel 7 al 10. Números y medidas en `docs/design/balance-report.md` §HU-106.
- CA4: Cuchillas arrojadizas pasa a cono de radio 3 sin casteo (pegado al objetivo toca lo mismo que con radio 4; un casteo frenaba al Pícaro). El validador ya no avisa de ella ni de Tajo amplio, y con la Fase 2 abierta no da errores. CA5: área del Mago 56 (objetivo 55) con Campo ardiente en 26.
- CA3: con el equipo del nivel 9 el segundo rango sube un 4,4 % de mediana (4,0 % con el verde de nivel 9 que hay en `items.json`): no se nota. Propuesta no aplicada: `spellRankBonusPct` 0,20, el máximo que deja a Pulso sagrado en 40 puntos.
- Eviscerar se queda en mono 11 (objetivo 20): con más daño el Pícaro farmea mucho más rápido. Aun así, la XP por hora entre clases queda en 17–19 % entre los niveles 7 y 9 (15 % es el tope); queda para HU-119 con el equipo real, con las palancas medidas en el informe.
- Tests ajustados a las Cuchillas de radio 3: `ContentLoaderTests.InstantDamageCones_WarnOnlyBeyondMeleeRange` ya no espera su aviso (lo da Cono de frío, radio 5, nivel 11) e `InstantDamageArea_ThatTheActivePhaseReaches_IsAnError` las vuelve a poner a 4 en su copia del contenido; `SpellUpgradeTests` y `SpellUpgradeNetTests` ya no las parchean para abrir la Fase 2.

---

### HU-107 · Mejoras de los hechizos
**Como** jugador **quiero** que cada hechizo tenga dos mejoras con sentido **para** elegir entre estilos (más daño o más control, más alcance o menos recarga).
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-104, HU-106
- Skills: `game-content` (subagente `content-designer`)

**Criterios de aceptación**
1. **Dado** los 6 hechizos de cada clase de los niveles 1 a 9 (24 en total) **entonces** cada uno tiene 2 mejoras en `content/spells.json`, con una descripción en español que dice el número.
2. **Dado** las dos mejoras de un hechizo **entonces** cambian cosas distintas (no "+10 % de daño" contra "+12 % de daño") y ninguna es estrictamente mejor: el modelo mide las dos sobre el pentagrama.
3. **Dado** el modelo con mejoras **entonces** ninguna combinación de 4 hechizos con sus mejoras rompe la regla 40/75 ni supera el valor de la clase en una punta, y el informe muestra la build más fuerte y la más débil de cada clase.
4. **Dado** una idea de mejora que necesita un modificador que HU-104 no tiene **entonces** se anota en `class-kits.md` y no entra (pilar 6).

**Notas técnicas**
- Ejemplos orientativos: Bola de fuego (−0,5 s de casteo / +20 % de daño); Provocar (+1 s / +3 tiles de alcance); Renovar (+2 s / cura al instante un 30 % del total).
- Los hechizos de los niveles 11 y 13 reciben sus mejoras en la Fase 3.

---

### HU-108 · Tileset del Bosque y paleta de la Cripta
**Como** jugador **quiero** que el Bosque se vea distinto de la Pradera y la Cripta distinta de la Mina **para** notar que cambié de tier.
- Prioridad: Must · Estimación: M · Estado: Hecha
- Dependencias: HU-080, HU-083
- Skills: `pixel-art-assets`, `world-maps`, `godot-client`

**Criterios de aceptación**
1. **Dado** `tools/art/gen_tiles.py` **entonces** genera también el atlas del Bosque con la misma disposición de piezas que `terrain.png` (hierba oscura y musgo, tierra húmeda, agua de pantano, copas oscuras, raíces), en Resurrect 64 y de forma determinista.
2. **Dado** un mapa con la propiedad `biome` (`meadow` o `forest`) **entonces** `TerrainBaker` usa su atlas; sin la propiedad usa el de la Pradera, así que los mapas actuales no cambian.
3. **Dado** las cuevas **entonces** comparten las piezas de interior con una paleta por mapa (`palette`: Mina marrón, Cripta verde) y la Mina se ve igual que hoy.
4. **Dado** el cambio de mapa al Bosque **entonces** no tarda más que entrar a la Pradera.

**Notas técnicas**
- Un tileset por tier (GDD §Mundo). La paleta se aplica al generar el atlas (un PNG por paleta), sin shader.

**Notas de implementación**
- `tools/art/gen_tiles.py` genera tres atlas con la disposición de `terrain.png`: `terrain_forest.png` (hierba oscura con musgo, tierra húmeda, sendero, pantano con juncos y nenúfares, peñascos con musgo, copas verde azulado, helechos, raíces y tronco con raíces; copia del de la Pradera lo que no cambia) y `terrain_crypt.png` (el del Bosque con las piezas de interior recoloreadas por `CAVE_PALETTES["crypt"]`). `terrain.png` sale con la paleta `mine` (identidad): mismos píxeles que antes.
- `TmjMap` lee las propiedades de mapa `biome` y `palette`; `TerrainBaker.atlas_path_for` elige el atlas (`palette` manda; sin propiedad o con un valor desconocido, `terrain.png`) y la caché del horneado va por mapa y atlas. `meadow.tmj` y `mine.tmj` no cambian; el servidor ignora las propiedades nuevas.
- CA3: la Mina horneada antes y después es idéntica píxel a píxel (suelo y capa `above`), igual que la Pradera. CA4: hornear la Pradera con el atlas del Bosque tarda lo mismo que con el suyo (850–930 ms frío, 3 tandas; cargar un atlas ~2 ms; en caché ~0,05 ms): el coste depende del tamaño del mapa, no del bioma.
- Tests: `test_terrain_biomes.gd` (lectura de las propiedades, misma disposición de piezas, la Cripta solo recolorea el interior, horneado y caché del Bosque). Capturas en `docs/screenshots/tier2/` (atlas y Pradera/Mina horneadas con los atlas nuevos); `forest.tmj` y `crypt.tmj` llegan con HU-111 y HU-115.

---

### HU-109 · Monstruos del Bosque y de la Cripta
**Como** jugador de nivel 6 a 10 **quiero** monstruos nuevos con mecánicas reconocibles **para** que subir en el Bosque no sea pegar a lobos más grandes.
- Prioridad: Must · Estimación: M · Estado: Hecha
- Dependencias: HU-036, HU-084
- Skills: `game-content`, `combat-system` (subagente `content-designer`)

**Criterios de aceptación**
1. **Dado** `content/monsters.json` **entonces** el Linde (6–8) y el Pantano (8–10) tienen 3 monstruos de campamento cada uno, con al menos uno `hard` (a distancia o con mecánica), y 1 élite cada uno (nivel 8 y nivel 10); la Cripta tiene 2 monstruos de nivel 10, 1 élite de nivel 11 y las plantas trampa (inmóviles, enraízan en un área marcada a quien pasa).
2. **Dado** sus hechizos **entonces** usan solo efectos y auras que ya existen (raíz, ralentización, veneno, área marcada) y cada mecánica se ve antes de doler (casteo o marca).
3. **Dado** el modelo de `tools/balance/` **entonces** cualquier clase mata en solitario a un monstruo normal de su nivel dentro de `killCycleSecTarget`, la XP por hora entre clases queda en ±15 % y los élites piden un grupo de 2–3 (nadie los mata solo a nivel equivalente).
4. **Dado** las tablas de XP **entonces** salen de la fórmula de `rules.progression` (nivel y tipo), sin números a mano.

**Notas técnicas**
- Propuesta de nombres para el `content-designer`: Linde (lobo del bosque, araña tejedora, leñador bandido; élite: oso viejo), Pantano (hombre lagarto, fuego fatuo, sapo gigante; élite: bruja del pantano), Cripta (esqueleto de raíces, espíritu del musgo; élite: guardián de la cripta).
- `skeleton_warrior` y `lesser_lich_king` (niveles 13 y 15) siguen esperando a la Fase 3.

**Notas de implementación**
- 12 monstruos, 16 hechizos y 11 auras de monstruo con efectos existentes (solo círculo); cada mecánica tiene casteo o área marcada. Bestiario, números y medidas en `docs/design/balance-report.md` §Fase 2.
- Modelo nuevo `tools/balance/tier2.py`: ciclo de 27–36 s con cualquier clase, XP por hora en ±14 % y ningún élite se mata solo (ni con pociones ni esquivando); las parejas con Guerrero o Sacerdote los matan. El equipo de nivel 7 a 9 es una aproximación (`gear_factor`) hasta HU-110.
- Planta trampa con `speed: 0`: el schema bajó el mínimo de 0,5 a 0 y la IA lo soporta sin código (`combat.md` §Monstruos); su spawn va con `wanderRadius` 0 (HU-115).
- Botín mínimo (cobre y chatarra existente) que completa HU-110. Las hojas `monsters/<id>` las dibuja HU-114 (CA1), que entra antes que estos monstruos: sin ellas, el cliente pintaba el marcador de color y los dos tests GUT de hojas y animaciones fallaban.
- Pendiente de decidir (no aplicado): Mago solo con básicos al 55–61 % (piso 50 %); propuesta `classScaling.mage.hpPerSta` 12 en el informe.

---

### HU-110 · Equipo, botín y vendedor de los niveles 6 a 10
**Como** jugador **quiero** equipo nuevo que se note y un sitio donde comprar pociones en el Bosque **para** que avanzar compense y no tenga que volver a la Aldea.
- Prioridad: Must · Estimación: M · Estado: Hecha
- Dependencias: HU-050, HU-055, HU-109
- Skills: `inventory-items`, `game-content` (subagente `content-designer`)

**Criterios de aceptación**
1. **Dado** `content/items.json` **entonces** hay equipo de nivel 7 y de nivel 9 para cada casilla y cada tipo de arma y armadura que ya existe, común y poco común, y el comparador del tooltip (HU-053) lo muestra mejor que el de nivel 4–6.
2. **Dado** las tablas de botín **entonces** los normales sueltan cobre, chatarra y a veces un verde, y los élites garantizan un verde y a veces un raro (`rules.loot`).
3. **Dado** el punto seguro del Linde **entonces** un vendedor vende los consumibles del tier (poción de vida mayor y comida) y compra cualquier cosa; Marta no cambia.
4. **Dado** el modelo **entonces** el oro que se gana del nivel 6 al 10 alcanza para las pociones sin farmear aparte.

**Notas técnicas**
- El botín del Árbol Podrido va en HU-117. Los objetos de nivel 13 y 15 que ya existen siguen para la Fase 3.

**Notas de implementación**
- 120 objetos de nivel 7 y 9 (un blanco y un verde por casilla y tipo; el verde de joyería, físico y de lanzador), 3 raros de élite y 8 chatarras. El comparador los da mejores que los del Tier 1 de su casilla y tipo en las 94 comparaciones; contra los raros del Capataz quedan a la par. Stats un punto por debajo de la guía: con las 9 casillas cubiertas, la guía dejaba a los élites al alcance del Guerrero solo.
- Botín de los 12 monstruos: los normales sueltan cobre, su chatarra, ~11 % de blancos y ~7 % de verdes (el Linde, nivel 7; el Pantano y la Cripta, nivel 9); los élites, un verde garantizado (`groups`) y su raro al 10 %.
- Vendedor `forest_camp` (Brena la trampera) que compra de todo; Marta no cambia. **Falta (Parcial):** la poción mayor de vida y el Venado ahumado necesitan 2 hechizos en `spells.json` y 1 aura en `auras.json`, que esta HU no podía tocar; hasta entonces vende las pociones menores y pan.
- `tools/balance/gear.py`: ciclo ≤ 36 s salvo el Guerrero recién llegado al 7 (36,3 s); oro del 6 al 10 de sobra para pociones y comida; XP por hora 15,4 % con el equipo esperado. Propuestas (no aplicadas) de `hpRegenDelaySec` 5 y élites ×1,3 en `balance-report.md` §HU-110.
- 2026-10-07: aplicados los consumibles que faltaban (`item_greater_heal`, `item_eat_smoked_venison` y el aura `smoked_venison_hot` en `spells.json`/`auras.json`; Poción mayor de vida y Venado ahumado en `items.json`); Brena vende poción mayor, venado y poción menor de maná y las tablas del Tier 2 sueltan los nuevos. CA3 cumplido: HU hecha. Los íconos propios de los 131 objetos van con HU-114.

---

### HU-111 · Mapa del Bosque: Linde y Pantano
**Como** jugador **quiero** un Bosque con dos zonas para subir del 6 al 10 **para** tener un sitio nuevo que explorar después de la Mina.
- Prioridad: Must · Estimación: L · Estado: Pendiente
- Dependencias: HU-108, HU-109, HU-110
- Skills: `world-maps`

**Criterios de aceptación**
1. **Dado** `maps/forest.tmj` (generado con `tools/maps/`, `biome: forest`) **entonces** tiene dos zonas abiertas en un solo mapa, Linde del Bosque (6–8) y Pantano (8–10), separadas por un cuello de botella; cruzar cada una lleva 25–40 s (`rules.world.zoneCrossTimeSecTarget`).
2. **Dado** cada zona **entonces** tiene un punto de referencia visible (Linde: un árbol enorme; Pantano: una torre hundida), un sendero principal obvio, 2–3 campamentos con subniveles (los bajos en la entrada, los altos hacia la salida) y una rama lateral con su élite.
3. **Dado** cada zona **entonces** tiene su punto seguro, y morir en el Bosque te devuelve al de la zona, no a la Aldea.
4. **Dado** el mapa **entonces** desde la Mina se llega al Linde, al fondo del Pantano está la entrada de la Cripta (HU-115) y en el Linde está el lado alto del puente (HU-113).
5. **Dado** `TiledMapLoaderTests` **entonces** todos los spawns son alcanzables desde la entrada, no quedan islas y los bordes son sólidos.

**Notas técnicas**
- Generador determinista como `gen_tier1_maps.py`. Regenerar el Tier 1 deja `meadow.tmj` con cambios de fin de línea: se restaura con `git checkout`.
- Densidad: un spawn cada ~8×8 tiles en las zonas de farmeo (skill `world-maps`).

---

### HU-114 · Arte del Tier 2: monstruos, jefe e íconos
**Como** jugador **quiero** que los monstruos y el equipo nuevos tengan su propio dibujo **para** reconocerlos de un vistazo.
- Prioridad: Must · Estimación: M · Estado: Parcial
- Dependencias: HU-090, HU-109, HU-110
- Skills: `pixel-art-assets`

**Criterios de aceptación**
1. **Dado** cada monstruo nuevo **entonces** tiene su hoja con las animaciones de HU-090 (reposo, andar, atacar, recibir golpe y morir) y se distingue a distancia de combate.
2. **Dado** el Árbol Podrido **entonces** tiene una hoja a escala de jefe, como el Capataz.
3. **Dado** los objetos nuevos **entonces** tienen su ícono, del mismo estilo que los de HU-082.
4. **Dado** las hojas generadas **entonces** salen de `tools/art/` de forma determinista y lo que venga de fuera queda en `client/assets/CREDITS.md`.

**Notas de implementación**
- CA1 hecho (2026-10-07): las hojas de los 12 monstruos del Tier 2 (HU-109) las dibuja `tools/art/gen_monsters_t2.py` (`build()`, con `gen_chars.sheet` y `write_meta`; `generate_all.py` lo llama) con las animaciones de HU-090, silueta propia y paleta de su bioma; los élites, más grandes. `gen_chars.py` gana armas y poses aditivas sin cambiar las hojas existentes (comprobado píxel a píxel, y dos ejecuciones dan los mismos bytes). Lámina: `docs/screenshots/tier2/monsters.png`.
- Falta: CA2, la hoja del Árbol Podrido (llega con HU-117), y CA3, los íconos del equipo de nivel 7 y 9 (HU-110; mientras, esos objetos usan íconos que ya existen).

---

### HU-115 · Cripta de Raíces
**Como** grupo de nivel 9–10 **queremos** una cueva con trampa, élite y jefe **para** cerrar el Tier 2.
- Prioridad: Must · Estimación: L · Estado: Pendiente
- Dependencias: HU-108, HU-109, HU-111
- Skills: `world-maps`, `game-content`

**Criterios de aceptación**
1. **Dado** `maps/crypt.tmj` (mapa aparte con `palette: crypt`, ADR-007) **entonces** se entra por un portal al fondo del Pantano y tiene 3–5 salas para 5–10 minutos: entrada con punto seguro → salas de monstruos → sala de la trampa → rama lateral con la sala del jefe, y la sala del élite antes de la salida al Tier 3.
2. **Dado** la sala de la trampa **entonces** las plantas trampa (HU-109) obligan a cruzar con cuidado o a limpiarlas, y se ven antes de activarse.
3. **Dado** la salida al Tier 3 **entonces** está cerrada en la Fase 2 (`minPhase: 3`, HU-112).
4. **Dado** `TiledMapLoaderTests` **entonces** todas las salas son alcanzables desde la entrada y morir dentro te deja en la entrada.

**Notas técnicas**
- Una sola copia compartida, como la Mina (ADR-007). El puzle de palancas ya está en la Mina: aquí la variedad es la trampa.

---

### HU-117 · Jefe Árbol Podrido
**Como** grupo **queremos** un jefe que obligue a moverse y a reorganizarse **para** tener el objetivo final del Tier 2 y botín raro.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-100, HU-115, HU-116
- Skills: `combat-system`, `game-content`

**Criterios de aceptación**
1. **Dado** la sala del jefe **entonces** el Árbol Podrido (nivel 11, `boss`, inmóvil) usa Raíces (área marcada sobre un jugador que lo enraíza, esquivable), Esporas (área duradera que daña en el tiempo y obliga a moverse, HU-100) y, bajo el 50 % de vida, invoca retoños que van a por quien menos amenaza tiene (HU-116).
2. **Dado** `rules.boss` con nivel B = 11 y el tope 10 de la Fase 2 **entonces** 3 jugadores de nivel 9 con equipo de su nivel lo matan en 60–100 s; los casos de 2 de nivel 11 y de 12 + 10 se validan al abrir la Fase 3, y nadie lo mata solo.
3. **Dado** su muerte **entonces** suelta exactamente un raro de su `groups` (una pieza por rol), asignado al azar a un miembro, y se anuncia en el chat global.
4. **Dado** el jefe **entonces** es inmune a aturdir, enraizar y ralentizar (`rules.combat.bossImmuneToAuraKinds`).

**Notas técnicas**
- Inmóvil (`speed: 0`): quedarse lejos no es seguro porque Raíces y Esporas apuntan a jugadores a distancia.

---

### HU-119 · Pasada de balance y partida de prueba de la Fase 2
**Como** diseñador **quiero** medir la Fase 2 entera y jugarla con amigos **para** cerrarla con datos, como la Fase 1.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-106, HU-107, HU-109, HU-110, HU-117, HU-118
- Skills: `game-content`, `combat-system` (subagente `content-designer`)

**Criterios de aceptación**
1. **Dado** el modelo de `tools/balance/` **entonces** subir del 6 al 10 lleva unas 7,2 h (GDD §Progresión) y la XP por hora entre clases queda en ±15 %.
2. **Dado** el Capataz **entonces** se valida el caso de 1 jugador de nivel 7 con 1 de nivel 5 que la Fase 1 no podía probar (GDD §Jefes).
3. **Dado** los duelos con las builds nuevas **entonces** el favorito de cada pareja de clases gana entre el 60 % y el 75 % con nivel y equipo iguales (pilar 7).
4. **Dado** la partida de prueba con amigos **entonces** sus notas quedan en `docs/design/playtest-notes.md` (Bosque, Cripta, jefe, mejoras y rendimiento) y lo que salga se convierte en HU.
