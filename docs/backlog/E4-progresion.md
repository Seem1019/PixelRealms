# E4 · Progresión

### HU-040 · Ganar experiencia
**Como** jugador **quiero** ganar XP al matar monstruos **para** progresar.
- Prioridad: Must · Estimación: S · Estado: Pendiente
- Dependencias: HU-037
- Skills: `combat-system`, `dotnet-server`

**Criterios de aceptación**
1. **Dado** que mato a un monstruo **entonces** recibo `xp = round((5 · nivel + 1) · tipo) · mod` con `tipo` de `monsters[].type` y `mod = 1 + 0.1 · clamp(diff, −4, 4)` (0 si `diff ≤ −5`), todo leído de `rules.progression`, y `XpGain`.
1b. **Dado** un Slime (nv 1, normal), un Goblin arquero (nv 5, hard), un Gólem (nv 6, elite) y el Capataz (nv 6, boss) matados por un nivel 5 **entonces** dan 4 (diff −4 → ×0.6), 31, 102 y 341 XP; un Slime para un nivel 6 da 0 (gris). Tests exactos.
1c. **Dado** `rules.progression.xpRate` distinto de 1.0 **entonces** toda la XP ganada se multiplica por él (test con 3.0).
2. **Dado** la barra de XP en el HUD **entonces** muestra `xp / xpNext` y texto al pasar el ratón.
3. **Dado** un monstruo que otro jugador (fuera de mi grupo) taggeó primero **entonces** no recibo XP ni botín.
4. **Dado** el tope de nivel de la fase activa (`rules.progression.levelCapByPhase[world.currentPhase − 1]`, 6 en la Fase 1) **entonces** no se acumula XP y la barra muestra "Nivel máximo".

---

### HU-041 · Subir de nivel y desbloquear hechizos
**Como** jugador **quiero** subir de nivel y aprender hechizos **para** sentir que mi personaje se hace más fuerte.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-040
- Skills: `combat-system`, `godot-client`

**Criterios de aceptación**
1. **Dado** que alcanzo la XP necesaria (curva por tiempo, ADR-017: `round(minutesPerLevel[L] · (60 / killCycleSecTarget) · xpMonstruoNormal(L))`, todo de `rules.progression`) **entonces** subo de nivel (el sobrante se conserva; puede subir varios niveles de golpe), stats +`statsPerLevel`, vida y recurso llenos.
2. **Dado** un nivel que desbloquea hechizos **entonces** `LevelUp{newSpells}` y el cliente los coloca en la primera casilla libre de la hotbar con un aviso.
3. **Dado** la subida **entonces** los demás en la AOI ven un efecto visual y el nivel actualizado sobre mi nombre.
3b. **Dado** un nivel de `rules.progression.spellRankLevels` (4, 8 y 12; ADR-024) **entonces** todos los hechizos de clase conocidos suben un rango y cada rango añade `spellRankBonusPct` (+15 %) sobre el valor base del hechizo; en la Fase 1 el rango sube solo, `LevelUp{rankUps}` lo informa y el cliente muestra un aviso. En la Fase 1 (tope 6) solo se alcanza el rango del nivel 4 (test: nivel 4 → `rankUps` con los 3 hechizos conocidos hasta entonces; nivel 5 → `newSpells` y sin `rankUps`). El +15 % se aplica al `base` de los efectos numéricos del hechizo (daño, cura, escudo, cantidad de aura); qué más escala y si los rangos se acumulan de forma lineal o compuesta solo importa desde la Fase 2 (ADR-024).
4. **Dado** tests **entonces** cubren la tabla de XP de niveles 1→15 con los valores exactos del GDD (100, 367, 933 … 24 850; si cambia `killCycleSecTarget` o `minutesPerLevel`, la tabla cambia sin tocar código) y los niveles de desbloqueo 1, 2, 3, 5, 7, 9, 11, 13 (`rules.progression.spellUnlockLevels`) y de rango 4, 8, 12 (`spellRankLevels`).

---

### HU-042 · Panel de personaje
**Como** jugador **quiero** ver mis estadísticas y equipo **para** entender cómo mejora mi personaje.
- Prioridad: Should · Estimación: M · Estado: Pendiente
- Dependencias: HU-041, HU-052
- Skills: `godot-client`

**Criterios de aceptación**
1. **Dado** la tecla C **entonces** se abre el panel con el muñeco de equipo (9 slots), stats primarios y derivados (vida, recurso, poder de ataque, poder de hechizo, crítico %, esquiva %, armadura, % de mitigación contra un nivel igual y velocidad de ataque).
2. **Dado** un stat **cuando** paso el ratón **entonces** un tooltip explica de dónde viene ("Base 14 + Equipo 3 (afinidad media ×0.85) + Auras 0").
3. **Dado** un cambio de equipo o aura **entonces** el panel se actualiza en vivo con `StatsUpdate`.

---

### HU-043 · Libro de hechizos y barra (4 hechizos + 4 utilizables)
**Como** jugador **quiero** ver mis hechizos y elegir cuáles llevo en la barra **para** armar mi build.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-041
- Skills: `godot-client`, `net-protocol`

**Criterios de aceptación**
1. **Dado** la tecla P **entonces** se abre el libro con todos los hechizos de mi clase; los no aprendidos en gris con "Nivel X".
2. **Dado** un hechizo aprendido **cuando** lo arrastro a una de las 4 casillas de hechizo (teclas 1–4) **entonces** se asigna (`SetHotbar`) y persiste entre sesiones.
3. **Dado** un consumible **cuando** lo arrastro a una de las 4 casillas de utilizables (teclas 5–8) **entonces** la casilla muestra la cantidad total en bolsa y lo usa al pulsarla.
4. **Dado** Shift + arrastrar fuera de la barra **entonces** se quita.
5. **Dado** un hechizo en una casilla de utilizables o un consumible en una de hechizo **entonces** el servidor lo rechaza (`invalid_payload`).

---

### HU-044 · Cambio de clase en NPC (Fases 1–2)
**Como** jugador **quiero** cambiar la clase de mi personaje en la Aldea **para** probar otra clase sin empezar de cero.
- Prioridad: Must · Estimación: M · Estado: Pendiente
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
