# E8 · Contenido y arte

### HU-080 · Mapa "meadow" completo
**Como** jugador **quiero** un mundo variado con zonas por nivel **para** explorar mientras progreso.
- Prioridad: Must · Estimación: L · Estado: Pendiente
- Dependencias: HU-031, HU-055
- Skills: `world-maps`, `pixel-art-assets`

**Criterios de aceptación**
1. **Dado** `maps/meadow.tmj` 128×128 **entonces** contiene las zonas del GDD (Pueblo Robledal seguro, Pradera, Bosque Sombrío, Ruinas, entrada a la Cripta) con capas y propiedades de la skill `world-maps`.
2. **Dado** los spawns **entonces** hay suficientes monstruos para que 5 jugadores suban del 1 al 10 sin esperar respawns (≥ 20 slimes, 20 jabalíes, 20 lobos, 15 goblins, 15 esqueletos).
3. **Dado** el mapa **entonces** hay 2 cementerios (pueblo y ruinas) y el vendedor en el pueblo.
4. **Dado** un recorrido a pie **entonces** no hay zonas inaccesibles ni huecos en las colisiones (verificado con un test de flood-fill desde el pueblo).

---

### HU-081 · Arte de clases y monstruos
**Como** jugador **quiero** personajes y monstruos con animaciones **para** que el juego se vea bien.
- Prioridad: Must · Estimación: L · Estado: Pendiente
- Dependencias: HU-024
- Skills: `pixel-art-assets`

**Criterios de aceptación**
1. **Dado** las 4 clases y 6 monstruos **entonces** cada uno tiene `idle`, `walk`, `attack` o `cast`, `hurt`, `death` en las direcciones de la skill.
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

### HU-083 · Mazmorra y jefe Rey Liche Menor
**Como** grupo de nivel 10 **queremos** un jefe desafiante **para** tener un objetivo final y botín épico.
- Prioridad: Should · Estimación: L · Estado: Pendiente
- Dependencias: HU-036, HU-062, HU-080
- Skills: `world-maps`, `combat-system`, `game-content`

**Criterios de aceptación**
1. **Dado** la Cripta (zona del mapa o mapa `crypt.tmj` con portal) **entonces** tiene 6–10 esqueletos antes del jefe.
2. **Dado** el jefe **entonces** usa Descarga de sombras cada 4 s sobre un objetivo aleatorio que no sea el tanque, y Nova gélida bajo el 70 % de vida.
3. **Dado** 5 jugadores nivel 10 con equipo verde **entonces** el combate dura 90–150 s (verificado por `content-designer` y una partida de prueba).
4. **Dado** su muerte **entonces** suelta al menos un épico garantizado (ajustar `loot_tables.json` si hace falta) y se anuncia en el chat global.

---

### HU-084 · Pasada de balance
**Como** diseñador **quiero** revisar números con datos **para** que ninguna clase sea inútil o rota.
- Prioridad: Should · Estimación: M · Estado: Pendiente
- Dependencias: HU-041, HU-052, HU-062
- Skills: `game-content`, `combat-system`

**Criterios de aceptación**
1. **Dado** el subagente `content-designer` **entonces** produce `docs/design/balance-report.md` con las 5 tablas de su definición.
2. **Dado** el informe **entonces** las desviaciones fuera de rango se corrigen en `content/` con commit `content(balance): ...` justificado.
3. **Dado** una sesión de juego con amigos **entonces** se recogen sensaciones en `docs/design/playtest-notes.md` y se crean HUs para lo que requiera código.
