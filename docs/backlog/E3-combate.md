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

### HU-032 · Ataque básico (todas las clases, melee y varita)
**Como** jugador de cualquier clase **quiero** atacar automáticamente con mi arma **para** hacer daño básico sin gastar recursos.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-030, HU-031
- Skills: `combat-system`, `net-protocol`

**Criterios de aceptación**
1. **Dado** un enemigo seleccionado **cuando** hago clic derecho sobre él o pulso la acción "Atacar" **entonces** se envía `AutoAttack{on:true}` y mi personaje golpea cada `speedMs / haste` (`rules.classScaling.<clase>.haste`) mientras esté en rango: el alcance del tipo de arma en `rules.weapons` (daga 1.25, espada/maza/hacha 1.5, varita 7, bastón 5 tiles). El básico no ocupa ninguna casilla de hechizo (ADR-019).
1b. **Dado** un arma con `scaling: int` **entonces** el golpe básico es de escuela `magic` (usa `spellPower`, no se mitiga por armadura) y dibuja un proyectil visual; con `str`/`agi` es `physical` con `attackPower`.
1c. **Dado** un Sacerdote con varita **entonces** puede matar un Slime solo con básicos sin gastar maná (test de integración).
2. **Dado** que me alejo **entonces** el swing se pausa y se reanuda al volver al rango (sin reiniciar el temporizador si no pasó el tiempo).
3. **Dado** un golpe **entonces** se calcula con la fórmula de ataque básico de `docs/design/combat.md` (afinidad del arma, poder / 14 · swing, tabla de impacto, armadura, crit) y el resultado va en el `CombatEvents` del tick (ADR-018). Un Guerrero gana `ragePerHitDealt` de ira al impactar.
4. **Dado** tests con `FixedRng` **entonces** cubren hit, crit, miss, dodge y mitigación por armadura con números exactos.
5. **Dado** un personaje con maná (cualquier clase y arma) **cuando** un básico impacta **entonces** recupera `maxMana · rules.combat.manaPerBasicHitPctPerSec · (swingMs / 1000)`; un fallo o una esquiva no dan maná. Test: un Mago con espada y un Mago con bastón recuperan el mismo maná por segundo.
6. **Dado** que empiezo un casteo **entonces** el temporizador del básico se pausa y se reanuda al terminar, interrumpir o cancelar el casteo.
7. **Dado** un hechizo instantáneo **cuando** lo lanzo **entonces** el temporizador del básico no se reinicia, pero durante `rules.combat.abilityLockMs` (250) no sale el básico ni otro hechizo; si el básico tocaba en ese hueco, sale al terminar el bloqueo (test con `FakeClock`).

---

### HU-033 · Lanzar hechizos (casteo, GCD, CD, recurso)
**Como** jugador **quiero** lanzar los hechizos de mi clase **para** combatir y apoyar a mi grupo.
- Prioridad: Must · Estimación: L · Estado: Pendiente
- Dependencias: HU-032, HU-039
- Skills: `combat-system`, `net-protocol`, `game-content`

**Criterios de aceptación**
1. **Dado** Bola de fuego (2 s) **cuando** la lanzo sobre un enemigo en rango **entonces** todos en la AOI reciben `CastStarted`; tras 2 s se resuelve y se descuenta su `cost.amount` (8 de maná en `spells.json`; el test lee el valor del contenido).
2. **Dado** que me muevo durante el casteo **entonces** el casteo sigue y mi velocidad es `velocidad · rules.combat.castMoveSpeedMult` (0.5) hasta que termina (ADR-019); recibir daño tampoco lo corta.
2b. **Dado** un casteo **cuando** recibo un `stun`, un `silence` (hechizo `magic`) o el efecto `interrupt` **entonces** se corta (`CastEnded{interrupted}`), no se gasta recurso, el GCD sigue corriendo y no puedo castear durante `interruptLockoutMs` (`Error{locked_out}`). Una `root` o un `slow` no lo cortan.
2c. **Dado** un hechizo a un objetivo **cuando** al terminar el casteo el objetivo está fuera de `range + castRangeToleranceTiles` (1.5) o sin LOS **entonces** `CastEnded{failed, reason}` sin gastar recurso ni cooldown.
2d. **Dado** un casteo de área o de salto **entonces** el punto, el origen y la dirección quedan fijos al empezar y al terminar no se revalida el alcance aunque me haya movido.
2e. **Dado** un casteo en curso **cuando** uso otro hechizo o un salto **entonces** el casteo se cancela (`CastEnded{cancelled}`, sin coste) y se usa el nuevo si el GCD lo permite; usar una poción no lo cancela.
3. **Dado** un hechizo en GCD o CD **entonces** `Error{on_gcd|on_cooldown}` y la hotbar muestra el barrido correcto.
4. **Dado** cada validación de `combat-system` (rango, LOS, recurso, objetivo inválido, aturdido, silenciado, muerto, bloqueo tras interrupción, tope de áreas) **entonces** hay test por código de error.
5. **Dado** un hechizo con `projectile` **entonces** el impacto ocurre `distancia / speed` después y el cliente dibuja el proyectil viajando hacia el objetivo.
6. **Dado** un hechizo `ally` sin objetivo aliado **entonces** se lanza sobre mí.

---

### HU-034 · Resolución de efectos y fórmulas
**Como** diseñador **quiero** que todos los hechizos se resuelvan con un único motor de efectos **para** crear contenido sin programar.
- Prioridad: Must · Estimación: L · Estado: Pendiente
- Dependencias: HU-033
- Skills: `combat-system`, `game-content`

**Criterios de aceptación**
1. **Dado** los efectos `damage`, `heal` (con `bonusBelowHpPct` opcional), `restore_resource`, `apply_aura`, `taunt`, `dash` e `interrupt`, y el campo `applyTo: "self"` por efecto, **entonces** `EffectResolver` los aplica en orden sobre los objetivos que devuelve `TargetResolver`. `dash` (Carga) coloca al lanzador adyacente al objetivo en ≤ 3 ticks y exige `distancia ≥ minRange` y LOS.
2. **Dado** cada `targeting` (`self`, `enemy`, `ally`, `self_aoe_enemies`, `self_aoe_allies`; las áreas `ground_*` van en HU-086) **entonces** hay test con posiciones concretas (dentro/fuera de radio, `maxTargets` respetado, más cercanos primero).
3. **Dado** `StatCalculator` **entonces** calcula todos los derivados de `docs/design/combat.md` leyendo `rules.classScaling` y `rules.affinity` (afinidad multiplica daño, armadura, spellPower y stats de cada item) con tests exactos para las 4 clases a nivel 1, 5 y 15, incluido un Mago con espada y placas.
3b. **Dado** las 4 clases con el mismo equipo (`iron_sword` + `recruit_mail_shirt`) **entonces** el DPS básico del Sacerdote está entre el 55 % y el 65 % del Pícaro, y el aguante del Mago entre el 50 % y el 60 % del Guerrero (márgenes provisionales de `rules.balanceTargets`; tests de balance con los valores de `combat.md` §Referencia).
4. **Dado** los 32 hechizos de clase del contenido (8 por clase, `docs/design/class-kits.md`) **entonces** un test paramétrico los lanza todos sobre un maniquí y verifica que no lanzan excepción y producen al menos un evento.

---

### HU-035 · Auras
**Como** jugador **quiero** aplicar efectos en el tiempo (venenos, curas periódicas, escudos, aturdimientos) **para** tener un combate con más profundidad.
- Prioridad: Must · Estimación: L · Estado: Pendiente
- Dependencias: HU-034
- Skills: `combat-system`

**Criterios de aceptación**
1. **Dado** Renovar (`hot` 12 s, tick 3 s) **entonces** produce exactamente 4 ticks de cura (el 4.º en el mismo tick que la expiración); **dado** Latigazo del Capataz (`dot` físico 9 s, tick 3 s) **entonces** produce 3 ticks de daño y luego `AuraRemoved`; los ticks no fallan ni critican y el DoT físico usa la mitigación calculada al aplicarse.
2. **Dado** Veneno (`maxStacks: 3`) aplicado 4 veces **entonces** tiene 3 stacks, daño por tick ×3 y duración refrescada **sin reiniciar el ritmo de ticks** (ADR-022, ver CA10).
2b. **Dado** Carrera (`removesKinds: [root, slow]`, `immuneKinds: [root, slow]`) sobre un Pícaro congelado **entonces** la raíz desaparece al instante y una Nova durante los 6 s no lo enraíza.
2c. **Dado** un monstruo `boss: true` **entonces** ignora auras de `rules.combat.bossImmuneToAuraKinds` (Gubia sobre el Capataz no lo aturde; el evento reporta `immune`).
3. **Dado** `stun` **entonces** el objetivo no se mueve, no castea (interrumpe el casteo actual) ni ataca; `root` solo impide moverse; `silence` impide hechizos no físicos; `slow` reduce velocidad.
4. **Dado** `shield` de 50 y un golpe de 70 **entonces** se absorben 50, entran 20, el escudo desaparece y el evento reporta `absorb: 50`.
5. **Dado** `stat_mod` (Carrera +50 % velocidad) **entonces** la velocidad cambia al aplicar y vuelve al expirar (el cliente predice con la velocidad del `Snapshot.self.speed`).
6. **Dado** el cliente **entonces** muestra íconos de auras en marcos propio/objetivo con tiempo restante y stacks.
7. **Dado** una entidad con `rules.limits.maxBuffsPerEntity` (16) auras beneficiosas **cuando** recibe una aura perjudicial **entonces** se aplica igual; **dado** su grupo lleno **entonces** sale la de ese grupo con menos tiempo restante y entra la nueva (tests para ambos grupos; ADR-021).
8. **Dado** un control (`rules.combat.controlAuraKinds`) **entonces** no cuenta para ningún tope y se aplica aunque los grupos estén llenos (salvo inmunidad de jefe o la inmunidad tras un control fuerte de CA12, ADR-022).
9. **Dado** dos ralentizaciones activas (20 % y 40 %) **entonces** la velocidad baja un 40 %, no un 52 %; **dado** dos aturdimientos solapados **entonces** dura hasta el que termina más tarde.
10. **Dado** un aura con `maxStacks` 1 reaplicada por el mismo lanzador **entonces** vuelve a su duración completa sin reiniciar el ritmo de ticks; **dado** Veneno de dos Pícaros **entonces** hay dos instancias con sus propias cargas (hasta 3 cada una) y cuentan 2 en el tope (ADR-022).
11. **Dado** dos escudos de lanzadores distintos **entonces** conviven y se gasta primero el que caduca antes; **dado** Carrera (+50 %) y Sendero de luz (+30 %) **entonces** la velocidad sube un 50 % y Sendero se muestra en gris hasta que Carrera termina.
12. **Dado** que termina un `stun`, `root` o `silence` **entonces** el objetivo es inmune a los tres durante `rules.combat.hardControlImmunitySec` (1.5 s) y un control fuerte nuevo da `immune`; un `interrupt` corta el casteo igualmente. Ninguna ralentización supera `maxSlowPct` (0.4).

---

### HU-036 · IA de monstruos: aggro, persecución, amenaza, evadir
**Como** jugador **quiero** que los monstruos reaccionen, me persigan y respeten al tanque **para** que el combate en grupo tenga roles.
- Prioridad: Must · Estimación: L · Estado: Pendiente
- Dependencias: HU-032, HU-035
- Skills: `combat-system`, `world-maps`

**Criterios de aceptación**
1. **Dado** un jabalí (`aggroRange: 4`) **cuando** entro a ≤ 4 tiles con línea de visión **entonces** me ataca; un slime (`aggroRange: 0`) solo responde si le pego.
2. **Dado** un monstruo persiguiéndome **entonces** rodea obstáculos (A*) y no atraviesa paredes.
3. **Dado** un guerrero con más amenaza **cuando** un mago le supera en < 130 % **entonces** el monstruo sigue con el guerrero; al superar 130 % cambia al mago. Provocar fija al guerrero durante el `durationMs` de su efecto `taunt` (2 s en `spells.json`).
4. **Dado** que arrastro al monstruo a más de `leashRange` de su spawn **entonces** entra en `Evade`: vuelve, es inmune, recupera toda la vida y olvida la amenaza.
5. **Dado** el goblin arquero **entonces** se queda a distancia y usa `goblin_shoot` cuando está listo (cooldown y casteo tomados de `spells.json`).
5b. **Dado** el Capataz Grask bajo el 50 % **entonces** lanza `foreman_rally` sobre sí mismo; `foreman_whip` siempre va a alguien que no sea el de mayor amenaza.
5c. **Dado** dos jugadores en duelo **entonces** los monstruos no les hacen aggro ni ellos generan amenaza hasta que el duelo termine.
5d. **Dado** un monstruo no jefe **entonces** sigue la misma inmunidad tras control que los jugadores (ADR-022); un jefe muestra `immune` a aturdir, enraizar y ralentizar.
6. **Dado** 300 monstruos **entonces** la IA completa cuesta < 3 ms por tick (benchmark en tests o `LoadBot`).

---

### HU-037 · Muerte y reaparición
**Como** jugador **quiero** reaparecer tras morir **para** volver a la acción.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-032
- Skills: `combat-system`, `godot-client`

**Criterios de aceptación**
1. **Dado** que mi vida llega a 0 **entonces** mi personaje muestra la animación `death`, pierdo mis auras, los monstruos me olvidan y recibo `Died`.
2. **Dado** la pantalla "Has muerto" **cuando** pulso "Reaparecer" **entonces** aparezco en el punto seguro más cercano del mapa actual (capa `graveyards`) con `rules.combat.respawnHpPct` de vida y recurso.
3. **Dado** que estoy muerto **entonces** no puedo moverme, castear, usar items ni lootear (errores `is_dead`).
4. **Dado** un monstruo muerto **entonces** su cadáver permanece `rules.combat.corpseLifetimeSec` (o hasta ser saqueado) y luego desaparece.

---

### HU-038 · HUD de combate
**Como** jugador **quiero** ver mi vida, recurso, objetivo, casteos y daño **para** tomar decisiones en combate.
- Prioridad: Must · Estimación: L · Estado: Pendiente
- Dependencias: HU-033, HU-035
- Skills: `godot-client`, `pixel-art-assets`

**Criterios de aceptación**
1. **Dado** el HUD **entonces** veo: marco propio (retrato/clase, nombre, nivel, barra de vida roja, barra de recurso con color por tipo: maná azul, ira roja oscura, energía amarilla), marco de objetivo, barra de casteo, barra de 4 casillas de hechizo (teclas 1–4) y 4 de utilizables (teclas 5–8) según `rules.loadout`.
2. **Dado** un `CombatEvents` (lote del tick) **entonces** aparecen números flotantes sobre la entidad (colores de la skill `combat-system`) que suben y se desvanecen en 1 s.
3. **Dado** la hotbar **entonces** cada casilla muestra ícono, tecla, barrido de CD/GCD, y se oscurece si no hay recurso o el objetivo está fuera de rango (rango calculado en cliente, solo visual).
4. **Dado** un error del servidor **entonces** se muestra en rojo en el centro-arriba 2 s ("No tienes suficiente maná").
4b. **Dado** auras del mismo tipo **entonces** las que no mandan se muestran en gris, y un control bloqueado por inmunidad muestra el texto "Inmune" (ADR-022).
5. **Dado** la barra de casteo **entonces** sigue llenándose aunque me mueva y termina en "Interrumpido" (rojo), "Fuera de alcance" (gris) o sin mensaje si cancelo.
6. **Dado** efectos visuales **entonces** salen de reservas precreadas (32 marcas de área, 64 proyectiles, 48 textos, 32 impactos) y hay como máximo 24 marcas, 48 proyectiles y 40 textos visibles; nunca se oculta una marca enemiga que me alcanza; los ticks de una misma aura se agrupan y con más de 6 números por entidad y segundo se muestra uno sumado (ADR-018).

---

### HU-039 · Recursos: maná, ira, energía y regeneración
**Como** jugador **quiero** que mi recurso de clase funcione de forma distinta **para** que cada clase se sienta única.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-032
- Skills: `combat-system`

**Criterios de aceptación**
1. **Dado** un personaje con maná (Mago, Sacerdote) **entonces** regenera maná según `spi`/`int` cada 1 s, reducido al 30 % durante 5 s tras gastar maná, y además recupera maná con cada básico que impacta (HU-032 CA5).
2. **Dado** un guerrero **entonces** gana `ragePerHitDealt` (6) por golpe o habilidad que impacta y `ragePerHitTaken` (4) por golpe recibido, y pierde `rageDecayPerSecOutOfCombat` fuera de combate; empieza en 0 al entrar. Carga no cuesta ira y, al impactar su aturdimiento, cuenta como golpe (+6).
3. **Dado** un pícaro **entonces** gana 10 de energía/s hasta 100.
4. **Dado** fuera de combate 6 s **entonces** todos regeneran vida según `spi` y `sta`.
5. **Dado** tests con `FakeClock` **entonces** cubren cada fórmula con números exactos leídos de `rules.json` (si cambia un valor del archivo, el test sigue verde).

---

### HU-086 · Hechizos de área apuntados (combate híbrido)
**Como** jugador **quiero** lanzar los hechizos de área donde apunte con el ratón y ver las áreas enemigas antes de que golpeen **para** que el combate tenga esquiva y posicionamiento sin perder el tab-target.
- Prioridad: Must · Estimación: L · Estado: Pendiente
- Dependencias: HU-034, HU-038
- Skills: `combat-system`, `net-protocol`, `godot-client`, `game-content`

**Criterios de aceptación**
1. **Dado** un hechizo `ground_aoe_*` **cuando** pulso su tecla **entonces** el cliente muestra el círculo de `aoeRadius` bajo el cursor (en rojo si está fuera de alcance) y al hacer clic envía `CastSpell{spellId, targetPos}` sin necesidad de objetivo seleccionado.
2. **Dado** un `targetPos` a más de `range + castRangeToleranceTiles` del lanzador o sin línea de visión **entonces** `Error{out_of_range|no_los}`; un `targetPos` con NaN o fuera del mapa devuelve `invalid_payload`.
3. **Dado** un casteo de área aceptado **entonces** todos en la AOI reciben `CastStarted{targetPos}` (forma y tamaño salen del contenido del cliente; `radius` solo viaja si algo lo modifica, ADR-018) y ven la marca en el suelo durante el casteo; el punto no cambia aunque el objetivo se mueva.
4. **Dado** el fin del casteo **entonces** el área afecta solo a quien está dentro de `aoeRadius` en ese tick (hasta `maxTargets`, los más cercanos al centro); quien salió de la marca no recibe nada (test con posiciones concretas).
5. **Dado** un monstruo con hechizo de área (Golpe de pico del Capataz) **entonces** usa la misma marca: apunta a la posición de su objetivo al empezar el casteo y los jugadores pueden esquivarlo.
6. **Dado** el contenido **entonces** `target_aoe_enemies` ya no existe en el schema y Estallido de llamas y Golpe de pico usan `ground_aoe_enemies` (hecho en el contenido el 2026-09-30); el validador lo comprueba.
7. **Dado** un área enemiga **entonces** nunca afecta a aliados ni al lanzador (sin fuego amigo).
7b. **Dado** la búsqueda de objetivos **entonces** usa la rejilla AOI de la instancia y pruebas de forma sin raíces ni trigonometría; la línea de visión se comprueba desde el centro solo para los candidatos que pasan la forma; `maxTargets` nunca supera `rules.limits.aoeMaxTargetsCap` (10); con `rules.limits.maxAreasPerInstance` áreas activas, un jugador recibe `Error{area_limit}` y un monstruo elige otra acción (ADR-018).
8. **Dado** el schema de hechizos **entonces** `shape` admite `circle` (`aoeRadius`), `cone` (`aoeRadius`, `aoeAngleDeg`) y `line` (`aoeLength`, `aoeWidth`); en esta HU solo se implementa `circle`; los hechizos con `cone`/`line` quedan no disponibles hasta que se implementen (ADR-023).

**Notas técnicas**
- ADR-015 y ADR-018. `CastState` guarda `targetPos`; `TargetResolver` recibe el punto. `targetPos`, `dir` y `radius` son campos opcionales del protocolo (no sube `ProtocolVersion`).
- Los números y el reparto de áreas por clase están en `docs/design/class-kits.md` y `docs/design/balance-report.md` (Fase 1 medida; Fases 2 y 3 provisionales).

---

### HU-085 · Hechizo de área del Sacerdote (`ground_aoe_all`)
**Como** Sacerdote **quiero** un hechizo de área que cure a mis aliados y dañe un poco a los enemigos **para** tener algo propio tanto en solitario como en grupo.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-086
- Skills: `combat-system`, `game-content`

**Criterios de aceptación**
1. **Dado** `targeting: ground_aoe_all` **entonces**, dentro del área, los efectos positivos (cura, escudo, beneficios) se aplican a los aliados (incluido el lanzador) y los negativos (daño, controles, perjuicios) a los enemigos; ningún objetivo recibe ambos. Lo usan Pulso sagrado y Sendero de luz.
2. **Dado** Pulso sagrado (`docs/design/class-kits.md`) **entonces** es el hechizo de nivel 5 del Sacerdote (`priest_holy_pulse`; sustituyó a Rezo de sanación en el contenido el 2026-09-30, ADR-020), se desbloquea a nivel 5, ralentiza a los enemigos alcanzados y su daño a enemigos es mucho menor que su curación (test que compara ambos valores a igual `spellPower`).
3. **Dado** un rival de duelo dentro del área **entonces** recibe la parte de daño solo si `PvpService.CanAttack` lo permite.
4. **Dado** el validador **entonces** acepta `ground_aoe_all` y exige que el hechizo tenga al menos un efecto `heal` y uno `damage`.

**Notas técnicas**
- Nombre, radio y números en el rediseño de kits. Usar un `id` nuevo (los ids de contenido no se reutilizan).

---

### HU-087 · Saltos a un punto (`leap`)
**Como** jugador **quiero** saltar hacia donde apunto **para** acercarme, alejarme o caer sobre un grupo de enemigos.
- Prioridad: Must (Paso sombrío del Pícaro, nivel 3, lo usa en la Fase 1) · Estimación: M · Estado: Pendiente
- Dependencias: HU-022, HU-086
- Skills: `combat-system`, `net-protocol`, `godot-client`, `game-content`

**Criterios de aceptación**
1. **Dado** un hechizo con efecto `leap` **cuando** lo lanzo sobre un punto **entonces** el servidor mueve al lanzador hasta `targetPos` en `travelMs` (0 = teletransporte), recortando el destino a la última casilla libre con línea de visión dentro de `maxRange`.
2. **Dado** que estoy enraizado o aturdido **entonces** el salto se rechaza (`Error{rooted|stunned}`) y no gasta recurso ni cooldown.
3. **Dado** efectos después del `leap` en el mismo hechizo **entonces** se aplican en el punto de llegada (p. ej. un área al caer), con tests de posición.
4. **Dado** el cliente propio **entonces** no predice el salto: al llegar la posición del servidor la suaviza en ~100 ms y descarta la predicción previa sin tirones; los demás lo ven interpolado.
5. **Dado** un destino dentro de una pared o fuera del mapa **entonces** nunca atraviesa colisiones (test con `test_small.tmj`).
6. **Dado** un casteo propio en curso **cuando** salto **entonces** el casteo se cancela sin coste (ADR-019).

**Notas técnicas**
- ADR-016. El `leap` no pasa por `MovementStep`, así que no cambia `shared/test-vectors/movement.json`; `Snapshot.self` lleva la posición nueva y el cliente la trata como una corrección grande con suavizado.

---

### HU-088 · Rendimiento del combate
**Como** anfitrión **quiero** que el combate aguante muchas áreas y auras a la vez **para** que no haya lag ni el servidor se caiga en las peleas grandes.
- Prioridad: Must · Estimación: L · Estado: Pendiente
- Dependencias: HU-034, HU-035, HU-086
- Skills: `combat-system`, `dotnet-server`, `net-protocol`

**Criterios de aceptación**
1. **Dado** los sistemas de combate **entonces** áreas, impactos pendientes y auras salen de reservas de capacidad fija; los eventos del tick van a un buffer circular de estructuras y las consultas usan listas reutilizadas; no hay LINQ ni closures en los sistemas (revisión + test de asignaciones por tick).
2. **Dado** `rules.limits` **entonces** se respetan: 2 áreas duraderas por lanzador (la tercera reemplaza a la más antigua), 128 áreas y 256 impactos pendientes por instancia, 10 objetivos por área como máximo, y 16 auras beneficiosas + 16 perjudiciales por entidad sin contar controles (ADR-021).
3. **Dado** un área duradera **entonces** se evalúa cada `persistentAreaTickMs` (500 ms), repartida entre ticks; las áreas nunca interactúan entre sí.
4. **Dado** varios resultados en un tick **entonces** cada observador recibe un solo `CombatEvents{tick, e}` (máx. 64 entradas; si hay más, varios mensajes), solo con lo que ve.
5. **Dado** el escenario de HU-089 **entonces** el combate cuesta ≤ 4 ms p99 por instancia.

**Notas técnicas**
- ADR-018. Microbenchmarks con BenchmarkDotNet para las pruebas de forma y la consulta de área.
