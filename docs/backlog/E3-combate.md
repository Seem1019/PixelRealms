# E3 · Combate

### HU-030 · Seleccionar objetivo
**Como** jugador **quiero** seleccionar enemigos y aliados **para** dirigirles mis ataques y curas.
- Prioridad: Must · Estimación: S · Estado: Pendiente
- Dependencias: HU-023
- Skills: `godot-client`, `net-protocol`

**Criterios de aceptación**
1. **Dado** una entidad visible **cuando** hago clic sobre ella **entonces** queda seleccionada (círculo bajo los pies: rojo hostil, verde aliado) y aparece su marco de objetivo.
2. **Dado** Tab **entonces** cicla entre enemigos vivos visibles a ≤ 12 tiles, del más cercano al más lejano.
3. **Dado** Esc o clic en el suelo **entonces** se deselecciona.
4. **Dado** la selección **entonces** se envía `SelectTarget{targetId}` y el servidor la guarda (la usan otros jugadores como "objetivo de mi objetivo" y los monstruos no).
5. **Dado** que el objetivo sale de la AOI o muere **entonces** se deselecciona (muerto: se mantiene para lootear, ver HU-050).

---

### HU-031 · Monstruos: spawn, patrulla y respawn
**Como** jugador **quiero** encontrar monstruos en el mundo **para** tener algo que combatir.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-020, HU-023
- Skills: `world-maps`, `combat-system`, `dotnet-server`

**Criterios de aceptación**
1. **Dado** los objetos de `spawns` del mapa **entonces** al arrancar se crean los monstruos (`count` por spawn) con los datos de `monsters.json`.
2. **Dado** un monstruo en `Idle` **entonces** deambula aleatoriamente dentro de `wanderRadius` con pausas de 2–6 s.
3. **Dado** un monstruo muerto **entonces** reaparece en su spawn tras `respawnSec`.
4. **Dado** el cliente **entonces** los monstruos se ven con su sprite (o placeholder), nombre y nivel coloreado según diferencia con el mío (gris ≤ −5, verde −3..−4, amarillo ±2, naranja +3..+4, rojo ≥ +5).

---

### HU-032 · Auto-ataque
**Como** jugador **quiero** atacar automáticamente con mi arma **para** hacer daño básico sin gastar recursos.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-030, HU-031
- Skills: `combat-system`, `net-protocol`

**Criterios de aceptación**
1. **Dado** un enemigo seleccionado **cuando** hago clic derecho sobre él o pulso la acción "Atacar" **entonces** se envía `AutoAttack{on:true}` y mi personaje golpea cada `speedMs` del arma mientras esté a ≤ 1.5 tiles.
2. **Dado** que me alejo **entonces** el swing se pausa y se reanuda al volver al rango (sin reiniciar el temporizador si no pasó el tiempo).
3. **Dado** un golpe **entonces** se calcula con la fórmula física de `docs/design/combat.md` (tabla de impacto, armadura, crit) y se envía `CombatEvent`.
4. **Dado** tests con `FixedRng` **entonces** cubren hit, crit, miss, dodge y mitigación por armadura con números exactos.

---

### HU-033 · Lanzar hechizos (casteo, GCD, CD, recurso)
**Como** jugador **quiero** lanzar los hechizos de mi clase **para** combatir y apoyar a mi grupo.
- Prioridad: Must · Estimación: L · Estado: Pendiente
- Dependencias: HU-032, HU-039
- Skills: `combat-system`, `net-protocol`, `game-content`

**Criterios de aceptación**
1. **Dado** Bola de fuego (2 s) **cuando** la lanzo sobre un enemigo en rango **entonces** todos en la AOI reciben `CastStarted`; tras 2 s se resuelve y se descuentan 20 de maná.
2. **Dado** que me muevo durante el casteo **entonces** se interrumpe (`CastEnded{interrupted}`), no se gasta maná y el GCD sigue corriendo.
3. **Dado** un hechizo en GCD o CD **entonces** `Error{on_gcd|on_cooldown}` y la hotbar muestra el barrido correcto.
4. **Dado** cada validación de `combat-system` (rango, LOS, recurso, objetivo inválido, aturdido, silenciado, muerto, casteando) **entonces** hay test por código de error.
5. **Dado** un hechizo con `projectile` **entonces** el impacto ocurre `distancia / speed` después y el cliente dibuja el proyectil viajando hacia el objetivo.
6. **Dado** un hechizo `ally` sin objetivo aliado **entonces** se lanza sobre mí.

---

### HU-034 · Resolución de efectos y fórmulas
**Como** diseñador **quiero** que todos los hechizos se resuelvan con un único motor de efectos **para** crear contenido sin programar.
- Prioridad: Must · Estimación: L · Estado: Pendiente
- Dependencias: HU-033
- Skills: `combat-system`, `game-content`

**Criterios de aceptación**
1. **Dado** los efectos `damage`, `heal`, `restore_resource`, `apply_aura`, `taunt` **entonces** `EffectResolver` los aplica en orden sobre los objetivos que devuelve `TargetResolver`.
2. **Dado** cada `targeting` (`self`, `enemy`, `ally`, `self_aoe_enemies`, `self_aoe_allies`, `target_aoe_enemies`) **entonces** hay test con posiciones concretas (dentro/fuera de radio, `maxTargets` respetado, más cercanos primero).
3. **Dado** `StatCalculator` **entonces** calcula todos los derivados de `docs/design/combat.md` con tests exactos para las 4 clases a nivel 1 y 10.
4. **Dado** los 20 hechizos de clase del contenido **entonces** un test paramétrico los lanza todos sobre un maniquí y verifica que no lanzan excepción y producen al menos un evento.

---

### HU-035 · Auras
**Como** jugador **quiero** aplicar efectos en el tiempo (venenos, curas periódicas, escudos, aturdimientos) **para** tener un combate con más profundidad.
- Prioridad: Must · Estimación: L · Estado: Pendiente
- Dependencias: HU-034
- Skills: `combat-system`

**Criterios de aceptación**
1. **Dado** Desgarrar (`dot` 12 s, tick 3 s) **entonces** produce exactamente 4 ticks de daño y luego `AuraRemoved`.
2. **Dado** Veneno (`maxStacks: 3`) aplicado 4 veces **entonces** tiene 3 stacks, daño por tick ×3 y duración refrescada.
3. **Dado** `stun` **entonces** el objetivo no se mueve, no castea (interrumpe el casteo actual) ni auto-ataca; `root` solo impide moverse; `silence` impide hechizos no físicos; `slow` reduce velocidad.
4. **Dado** `shield` de 50 y un golpe de 70 **entonces** se absorben 50, entran 20, el escudo desaparece y el evento reporta `absorb: 50`.
5. **Dado** `stat_mod` (Carrera +50 % velocidad) **entonces** la velocidad cambia al aplicar y vuelve al expirar (el cliente predice con la velocidad del `Snapshot.self.speed`).
6. **Dado** el cliente **entonces** muestra íconos de auras en marcos propio/objetivo con tiempo restante y stacks.

---

### HU-036 · IA de monstruos: aggro, persecución, amenaza, evadir
**Como** jugador **quiero** que los monstruos reaccionen, me persigan y respeten al tanque **para** que el combate en grupo tenga roles.
- Prioridad: Must · Estimación: L · Estado: Pendiente
- Dependencias: HU-032, HU-035
- Skills: `combat-system`, `world-maps`

**Criterios de aceptación**
1. **Dado** un jabalí (`aggroRange: 4`) **cuando** entro a ≤ 4 tiles con línea de visión **entonces** me ataca; un slime (`aggroRange: 0`) solo responde si le pego.
2. **Dado** un monstruo persiguiéndome **entonces** rodea obstáculos (A*) y no atraviesa paredes.
3. **Dado** un guerrero con más amenaza **cuando** un mago le supera en < 130 % **entonces** el monstruo sigue con el guerrero; al superar 130 % cambia al mago. Provocar fija al guerrero 3 s.
4. **Dado** que arrastro al monstruo a más de `leashRange` de su spawn **entonces** entra en `Evade`: vuelve, es inmune, recupera toda la vida y olvida la amenaza.
5. **Dado** el goblin arquero **entonces** se queda a distancia y usa `goblin_shoot` cuando está listo.
6. **Dado** 300 monstruos **entonces** la IA completa cuesta < 3 ms por tick (benchmark en tests o `LoadBot`).

---

### HU-037 · Muerte y reaparición
**Como** jugador **quiero** reaparecer tras morir **para** volver a la acción.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-032
- Skills: `combat-system`, `godot-client`

**Criterios de aceptación**
1. **Dado** que mi vida llega a 0 **entonces** mi personaje muestra la animación `death`, pierdo mis auras, los monstruos me olvidan y recibo `Died`.
2. **Dado** la pantalla "Has muerto" **cuando** pulso "Reaparecer" **entonces** aparezco en el cementerio más cercano con 50 % de vida y recurso.
3. **Dado** que estoy muerto **entonces** no puedo moverme, castear, usar items ni lootear (errores `is_dead`).
4. **Dado** un monstruo muerto **entonces** su cadáver permanece 60 s (o hasta ser saqueado) y luego desaparece.

---

### HU-038 · HUD de combate
**Como** jugador **quiero** ver mi vida, recurso, objetivo, casteos y daño **para** tomar decisiones en combate.
- Prioridad: Must · Estimación: L · Estado: Pendiente
- Dependencias: HU-033, HU-035
- Skills: `godot-client`, `pixel-art-assets`

**Criterios de aceptación**
1. **Dado** el HUD **entonces** veo: marco propio (retrato/clase, nombre, nivel, barra de vida roja, barra de recurso con color por tipo: maná azul, ira roja oscura, energía amarilla), marco de objetivo, barra de casteo, hotbar de 10 casillas con teclas 1–0.
2. **Dado** un `CombatEvent` **entonces** aparecen números flotantes sobre la entidad (colores de la skill `combat-system`) que suben y se desvanecen en 1 s.
3. **Dado** la hotbar **entonces** cada casilla muestra ícono, tecla, barrido de CD/GCD, y se oscurece si no hay recurso o el objetivo está fuera de rango (rango calculado en cliente, solo visual).
4. **Dado** un error del servidor **entonces** se muestra en rojo en el centro-arriba 2 s ("No tienes suficiente maná").

---

### HU-039 · Recursos: maná, ira, energía y regeneración
**Como** jugador **quiero** que mi recurso de clase funcione de forma distinta **para** que cada clase se sienta única.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-032
- Skills: `combat-system`

**Criterios de aceptación**
1. **Dado** un mago **entonces** regenera maná según `spi`/`int` cada 1 s, reducido al 30 % durante 5 s tras gastar maná.
2. **Dado** un guerrero **entonces** gana ira al golpear y al recibir daño según la fórmula, y pierde 2/s fuera de combate; empieza en 0 al entrar.
3. **Dado** un pícaro **entonces** gana 10 de energía/s hasta 100.
4. **Dado** fuera de combate 6 s **entonces** todos regeneran vida según `spi` y `sta`.
5. **Dado** tests con `FakeClock` **entonces** cubren cada fórmula con números exactos.
