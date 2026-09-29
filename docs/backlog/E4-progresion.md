# E4 · Progresión

### HU-040 · Ganar experiencia
**Como** jugador **quiero** ganar XP al matar monstruos **para** progresar.
- Prioridad: Must · Estimación: S · Estado: Pendiente
- Dependencias: HU-037
- Skills: `combat-system`, `dotnet-server`

**Criterios de aceptación**
1. **Dado** que mato a un monstruo que yo taggeé **entonces** recibo XP con la fórmula del GDD (ajuste por diferencia de nivel; 0 si es gris) y `XpGain`.
2. **Dado** la barra de XP en el HUD **entonces** muestra `xp / xpNext` y texto al pasar el ratón.
3. **Dado** un monstruo que otro jugador taggeó primero **entonces** no recibo XP ni botín.
4. **Dado** nivel 10 (máximo) **entonces** no se acumula XP y la barra muestra "Nivel máximo".

---

### HU-041 · Subir de nivel y desbloquear hechizos
**Como** jugador **quiero** subir de nivel y aprender hechizos **para** sentir que mi personaje se hace más fuerte.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-040
- Skills: `combat-system`, `godot-client`

**Criterios de aceptación**
1. **Dado** que alcanzo la XP necesaria (`round(100·L^1.6)`) **entonces** subo de nivel (el sobrante se conserva; puede subir varios niveles de golpe), stats +`statsPerLevel`, vida y recurso llenos.
2. **Dado** un nivel que desbloquea hechizos **entonces** `LevelUp{newSpells}` y el cliente los coloca en la primera casilla libre de la hotbar con un aviso.
3. **Dado** la subida **entonces** los demás en la AOI ven un efecto visual y el nivel actualizado sobre mi nombre.
4. **Dado** tests **entonces** cubren la tabla de XP de niveles 1→10 con los valores exactos del GDD.

---

### HU-042 · Panel de personaje
**Como** jugador **quiero** ver mis estadísticas y equipo **para** entender cómo mejora mi personaje.
- Prioridad: Should · Estimación: M · Estado: Pendiente
- Dependencias: HU-041, HU-052
- Skills: `godot-client`

**Criterios de aceptación**
1. **Dado** la tecla C **entonces** se abre el panel con el muñeco de equipo (9 slots), stats primarios y derivados (vida, recurso, poder de ataque, poder de hechizo, crítico %, esquiva %, armadura y % de mitigación contra un nivel igual).
2. **Dado** un stat **cuando** paso el ratón **entonces** un tooltip explica de dónde viene ("Base 14 + Equipo 3 + Auras 0").
3. **Dado** un cambio de equipo o aura **entonces** el panel se actualiza en vivo con `StatsUpdate`.

---

### HU-043 · Libro de hechizos y hotbar configurable
**Como** jugador **quiero** ver mis hechizos y organizarlos en la barra **para** jugar a mi manera.
- Prioridad: Should · Estimación: M · Estado: Pendiente
- Dependencias: HU-041
- Skills: `godot-client`, `net-protocol`

**Criterios de aceptación**
1. **Dado** la tecla P **entonces** se abre el libro con todos los hechizos de mi clase; los no aprendidos en gris con "Nivel X".
2. **Dado** un hechizo aprendido **cuando** lo arrastro a la hotbar **entonces** se asigna (`SetHotbar`) y persiste entre sesiones.
3. **Dado** un consumible **cuando** lo arrastro a la hotbar **entonces** la casilla muestra la cantidad total en bolsa y lo usa al pulsarla.
4. **Dado** Shift + arrastrar fuera de la barra **entonces** se quita.
