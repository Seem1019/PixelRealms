# E4 · Progresión

### HU-040 · Ganar experiencia
**Como** jugador **quiero** ganar XP al matar monstruos **para** progresar.
- Prioridad: Must · Estimación: S · Estado: Hecha
- Dependencias: HU-037
- Skills: `combat-system`, `dotnet-server`

**Criterios de aceptación**
1. **Dado** que mato a un monstruo **entonces** recibo `xp = round((5 · nivel + 1) · tipo) · mod` con `tipo` de `monsters[].type` y `mod = 1 + 0.1 · clamp(diff, −4, 4)` (0 si `diff ≤ −5`), todo leído de `rules.progression`, y `XpGain`.
1b. **Dado** un Slime (nv 1, normal), un Goblin arquero (nv 5, hard), un Gólem (nv 6, elite) y el Capataz (nv 6, boss) matados por un nivel 5 **entonces** dan 4 (diff −4 → ×0.6), 31, 102 y 341 XP; un Slime para un nivel 6 da 0 (gris). Tests exactos.
1c. **Dado** `rules.progression.xpRate` distinto de 1.0 **entonces** toda la XP ganada se multiplica por él (test con 3.0).
2. **Dado** la barra de XP en el HUD **entonces** muestra `xp / xpNext` y texto al pasar el ratón.
3. **Dado** un monstruo que otro jugador (fuera de mi grupo) taggeó primero **entonces** no recibo XP ni botín.
4. **Dado** el tope de nivel de la fase activa (`rules.progression.levelCapByPhase[world.currentPhase − 1]`, 6 en la Fase 1) **entonces** no se acumula XP y la barra muestra "Nivel máximo".

**Notas de implementación**
- `Progression/ProgressionSystem` (tras la muerte en el tick): XP con `XpCurve.SoloKillXp` (fórmula y modificador por diferencia de `rules.progression`, `xpRate`) al jugador que taggeó al monstruo (`Monster.TaggedBy`: primer jugador que le hizo daño; el reparto en grupo llega con HU-062 por el hook `XpRecipients`), `XpGain{amount, sourceId}`; en el tope de la fase (`levelCapByPhase[currentPhase − 1]`) no se acumula. Cliente: barra de XP con tooltip `xp / xpNext` y "Nivel máximo".
- Tests: `XpCurveTests` (CA1b exactos), `ProgressionSystemTests` (tag, xpRate ×3, tope).

---
### HU-041 · Subir de nivel y desbloquear hechizos
**Como** jugador **quiero** subir de nivel y aprender hechizos **para** sentir que mi personaje se hace más fuerte.
- Prioridad: Must · Estimación: M · Estado: Parcial
- Dependencias: HU-040
- Skills: `combat-system`, `godot-client`

**Criterios de aceptación**
1. **Dado** que alcanzo la XP necesaria (curva por tiempo, ADR-017: `round(minutesPerLevel[L] · (60 / killCycleSecTarget) · xpMonstruoNormal(L))`, todo de `rules.progression`) **entonces** subo de nivel (el sobrante se conserva; puede subir varios niveles de golpe), stats +`statsPerLevel`, vida y recurso llenos.
2. **Dado** un nivel que desbloquea hechizos **entonces** `LevelUp{newSpells}` y el cliente los coloca en la primera casilla libre de la hotbar con un aviso.
3. **Dado** la subida **entonces** los demás en la AOI ven un efecto visual y el nivel actualizado sobre mi nombre.
3b. **Dado** un nivel de `rules.progression.spellRankLevels` (4, 8 y 12; ADR-024) **entonces** todos los hechizos de clase conocidos suben un rango y cada rango añade `spellRankBonusPct` (+15 %) sobre el valor base del hechizo; en la Fase 1 el rango sube solo, `LevelUp{rankUps}` lo informa y el cliente muestra un aviso. En la Fase 1 (tope 6) solo se alcanza el rango del nivel 4 (test: nivel 4 → `rankUps` con los 3 hechizos conocidos hasta entonces; nivel 5 → `newSpells` y sin `rankUps`). El +15 % se aplica al `base` de los efectos numéricos del hechizo (daño, cura, escudo, cantidad de aura); qué más escala y si los rangos se acumulan de forma lineal o compuesta solo importa desde la Fase 2 (ADR-024).
4. **Dado** tests **entonces** cubren la tabla de XP de niveles 1→15 con los valores exactos del GDD (100, 367, 933 … 24 850; si cambia `killCycleSecTarget` o `minutesPerLevel`, la tabla cambia sin tocar código) y los niveles de desbloqueo 1, 2, 3, 5, 7, 9, 11, 13 (`rules.progression.spellUnlockLevels`) y de rango 4, 8, 12 (`spellRankLevels`).

**Notas de implementación**
- Subida con sobrante y varios niveles de golpe, stats recalculados (+`statsPerLevel` vía `StatCalculator`), vida y recurso llenos, hechizos de `spellUnlockLevels` aprendidos (`LevelUp{newSpells}`), rangos en `spellRankLevels` (`LevelUp{rankUps}`; `SpellRanks.BaseMultiplier` aplica +`spellRankBonusPct` al `base` de daño/cura/escudo/auras de hechizos de clase), `StatsUpdate` al jugador, `EntitySpawn` renovado a la AOI (nivel sobre el nombre) y guardado inmediato. Cliente: hechizo nuevo a la primera casilla libre (`SetHotbar`, handler con validación de casillas 0–3 hechizos / 4–7 consumibles) y avisos de nivel/rango.
- Tests: `XpCurveTests.Table_MatchesGdd_1To14` (100, 367, 933 … 24 850), `ProgressionSystemTests` (sobrante, nivel 4 → rankUps de los 3 conocidos; nivel 5 → newSpells sin rankUps; niveles de desbloqueo y rango de rules). El efecto visual de subida (CA3) llega con el arte.
- 2026-10-02 (rama `fix/phase1-audit-blockers`): Los demás ya reciben el nivel nuevo (`EntitySpawn` renovado). Falta CA3: el efecto visual de subida para los demás (estético).

---
### HU-042 · Panel de personaje
**Como** jugador **quiero** ver mis estadísticas y equipo **para** entender cómo mejora mi personaje.
- Prioridad: Should · Estimación: M · Estado: Hecha
- Dependencias: HU-041, HU-052
- Skills: `godot-client`

**Criterios de aceptación**
1. **Dado** la tecla C **entonces** se abre el panel con el muñeco de equipo (9 slots), stats primarios y derivados (vida, recurso, poder de ataque, poder de hechizo, crítico %, esquiva %, armadura, % de mitigación contra un nivel igual y velocidad de ataque).
2. **Dado** un stat **cuando** paso el ratón **entonces** un tooltip explica de dónde viene ("Base 14 + Equipo 3 (afinidad media ×0.85) + Auras 0").
3. **Dado** un cambio de equipo o aura **entonces** el panel se actualiza en vivo con `StatsUpdate`.

**Notas de implementación**
- `client/scripts/ui/character_panel.gd` (tecla C): muñeco de 9 slots (arrastrar desde la bolsa equipa, clic derecho desequipa), stats primarios y derivados de `StatsUpdate` (vida, recurso, poder de ataque/hechizo, crítico, esquiva, armadura y mitigación contra un nivel igual, velocidad de ataque); se refresca con cada StatsUpdate/InventoryUpdate. Sin arte (texto).
- 2026-10-02: CA2 con un tooltip por stat (`CharacterPanel.stat_origin`): "Base 9 + Equipo 1.7 (Guantes de bandido: afinidad
  media ×0.85) + Auras 0.3"; las auras son lo que no explican base y equipo, porque el servidor solo manda el total. CA3: el
  servidor emite `StatsUpdate` también al aplicar o quitar un aura con stats (`AuraSystem`), no solo al cambiar equipo.

---
### HU-043 · Libro de hechizos y barra (4 hechizos + 4 utilizables)
**Como** jugador **quiero** ver mis hechizos y elegir cuáles llevo en la barra **para** armar mi build.
- Prioridad: Must · Estimación: M · Estado: Hecha
- Dependencias: HU-041
- Skills: `godot-client`, `net-protocol`

**Criterios de aceptación**
1. **Dado** la tecla P **entonces** se abre el libro con todos los hechizos de mi clase; los no aprendidos en gris con "Nivel X".
2. **Dado** un hechizo aprendido **cuando** lo arrastro a una de las 4 casillas de hechizo (teclas 1–4) **entonces** se asigna (`SetHotbar`) y persiste entre sesiones.
3. **Dado** un consumible **cuando** lo arrastro a una de las 4 casillas de utilizables (teclas 5–8) **entonces** la casilla muestra la cantidad total en bolsa y lo usa al pulsarla.
4. **Dado** Shift + arrastrar fuera de la barra **entonces** se quita.
5. **Dado** un hechizo en una casilla de utilizables o un consumible en una de hechizo **entonces** el servidor lo rechaza (`invalid_payload`).

**Notas de implementación**
- `client/scripts/ui/spellbook_window.gd` (tecla P): hechizos de la clase, no aprendidos en gris con "Nivel X", arrastrar a casillas 1–4 → `SetHotbar`; consumibles de la bolsa a 5–8 con la cantidad total en bolsa; Shift+arrastrar fuera quita. Servidor `SetHotbarHandler` rechaza hechizo en casilla de utilizables o consumible en casilla de hechizo (`invalid_payload`); persiste en `character_hotbar`.

---
### HU-044 · Cambio de clase en NPC (Fases 1–2)
**Como** jugador **quiero** cambiar la clase de mi personaje en la Aldea **para** probar otra clase sin empezar de cero.
- Prioridad: Must · Estimación: M · Estado: Hecha
- Dependencias: HU-041, HU-043, HU-052
- Skills: `dotnet-server`, `net-protocol`, `godot-client`, `world-maps`

**Criterios de aceptación**
1. **Dado** el NPC de cambio de clase de la Aldea **cuando** hago clic a ≤ 3 tiles fuera de combate **entonces** veo las otras 3 clases (rol, recurso y descripción) y puedo elegir una.
2. **Dado** que confirmo **entonces** conservo nivel, XP, items, equipo y oro; cambian la clase, los stats base, el recurso (lleno) y los hechizos (los de la nueva clase hasta mi nivel); la barra de hechizos se rellena con los nuevos y el equipo se recalcula con la afinidad de la nueva clase.
3. **Dado** que estoy en combate, muerto, en duelo o en un intercambio **entonces** `Error{in_combat|is_dead|duel_busy|trade_busy}`.
4. **Dado** `rules.world.currentPhase` mayor que `rules.progression.classChange.npcUntilPhase` **entonces** el NPC no ofrece el cambio (en la Fase 3 lo sustituyen el bono de XP `altCatchUp` y el almacén compartido, que se diseñan en esa fase).
5. **Dado** el cambio **entonces** el personaje se guarda de inmediato y se loguea la clase anterior y la nueva.

**Notas técnicas**
- Mensaje nuevo C→S para pedir el cambio (skill `net-protocol`) y un tipo de NPC nuevo en la capa `npcs` (skill `world-maps`).

**Notas de implementación**
- NPC `class_change` ("Maestro Aldo", añadido a `maps/meadow.tmj` junto a Marta) y mensaje `ChangeClass{npcId, classId, reqId}`; `Social/ClassChangeService`: ≤ 3 casillas, vivo, fuera de combate, sin duelo/intercambio, solo mientras `world.currentPhase ≤ progression.classChange.npcUntilPhase`; conserva nivel/XP/items/equipo/oro, cambia clase, stats (afinidad nueva), recurso lleno, hechizos y barra; guardado inmediato y log. El cliente recibe un `Welcome` renovado. Cliente: ventana con las otras 3 clases (rol, recurso, descripción).
- Tests: `SocialTests.ClassChange_*`, `SocialFlowTests.ChangeClass_AtNpc_NewWelcome_AndSaved`. Nota: la skill `world-maps` solo documenta `vendorId`; el objeto de la capa `npcs` lleva `kind: class_change` (ya previsto en `NpcDef.Kind`).
- 2026-10-02 (rama `fix/phase1-audit-blockers`): CA3: `trade_busy` propio (antes daba `duel_busy`). CA4: `SocialPanels.class_change_available()` oculta la opción si `currentPhase > npcUntilPhase`. CA5: el handler loguea la clase anterior y la nueva.
