# E2 · Mundo y movimiento

### HU-020 · Cargar mapa Tiled en servidor y cliente
**Como** jugador **quiero** ver el mundo con su terreno y obstáculos **para** explorarlo.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-005, HU-004
- Skills: `world-maps`, `godot-client`, `dotnet-server`

**Criterios de aceptación**
1. **Dado** `maps/meadow.tmj` (versión inicial 64×64 con pueblo, pradera y un muro de prueba) **cuando** arranca el servidor **entonces** construye la `CollisionGrid` y loguea tamaño, spawns y cementerios.
2. **Dado** un spawn sobre un tile sólido o un `monsterId` inexistente **entonces** el servidor no arranca y explica el error.
3. **Dado** el cliente en la escena `World` **entonces** se ve el mapa con capas `ground`, `detail`, `walls` y `above` por encima de las entidades.
4. **Dado** `maps/test_small.tmj` **entonces** `TiledMapLoaderTests` cubre: CSV, flags de flip, `solid`, `blocksSight`, objetos de cada tipo.

---

### HU-021 · Movimiento autoritativo con colisión
**Como** jugador **quiero** moverme con WASD sin atravesar paredes **para** recorrer el mundo.
- Prioridad: Must · Estimación: L · Estado: Pendiente
- Dependencias: HU-014, HU-020
- Skills: `net-protocol`, `dotnet-server`, `godot-client`

**Criterios de aceptación**
1. **Dado** que mantengo D **entonces** el cliente envía `MoveInput{seq, dx:1, dy:0}` al cambiar la dirección y cada 200 ms mientras se mantiene, y al soltar envía `dx:0, dy:0`.
2. **Dado** los inputs **entonces** el servidor mueve al jugador a 4 tiles/s (diagonal normalizada) y lo detiene en paredes (se desliza por ellas).
3. **Dado** `shared/test-vectors/movement.json` **entonces** `MovementStep` (C#) pasa todos los casos.
4. **Dado** un cliente que envía `dx: 5` o `seq` decreciente **entonces** se ignora el input (clamp / descarte) y se loguea `debug`.
5. **Dado** que no llega input en 500 ms **entonces** el servidor detiene al jugador (evita "correr solo" tras un corte).
6. **Dado** el `Snapshot` **entonces** incluye `self{x,y}` y `ackSeq` (implementar aquí `Snapshot` mínimo).

**Notas técnicas**
- Algoritmo exacto en `docs/architecture.md` §4 y en el propio archivo de vectores.
- Sin predicción todavía: el cliente dibuja la posición del snapshot (se verá con retraso; se resuelve en HU-022).

---

### HU-022 · Predicción y reconciliación del jugador propio
**Como** jugador **quiero** que mi personaje responda al instante **para** que el juego no se sienta lento con latencia.
- Prioridad: Must · Estimación: L · Estado: Pendiente
- Dependencias: HU-021
- Skills: `godot-client`, `net-protocol`

**Criterios de aceptación**
1. **Dado** `movement_step.gd` **entonces** pasa los mismos vectores que el servidor (test GUT `test_movement_vectors.gd`).
2. **Dado** 150 ms de latencia simulada (`Net.simulated_latency_ms`) **cuando** me muevo **entonces** el personaje responde en el mismo frame y no hay tirones visibles al caminar en línea recta.
3. **Dado** una corrección del servidor (p. ej. me bloqueó algo que el cliente no sabía) **entonces** si el error es < 2 px se corrige suave en 100 ms; si es mayor, salta.
4. **Dado** el overlay F3 **entonces** muestra inputs pendientes, último `ackSeq` y error de reconciliación en px.

---

### HU-023 · Ver a otros jugadores (AOI + interpolación)
**Como** jugador **quiero** ver a mis amigos moverse con fluidez **para** jugar juntos.
- Prioridad: Must · Estimación: L · Estado: Pendiente
- Dependencias: HU-021
- Skills: `net-protocol`, `dotnet-server`, `godot-client`

**Criterios de aceptación**
1. **Dado** dos jugadores a menos de ~16 tiles **entonces** cada uno recibe `EntitySpawn` del otro (nombre, clase, nivel) y lo ve moverse.
2. **Dado** que uno se aleja fuera de su AOI **entonces** el otro recibe `EntityDespawn{reason:"left"}` y deja de verlo; al volver, reaparece.
3. **Dado** 100 ms de interpolación **entonces** el movimiento remoto se ve fluido a 10 snapshots/s, incluso con 5 % de pérdida simulada de snapshots.
4. **Dado** 30 jugadores simulados (bots de prueba `tools/LoadBot`) **entonces** el tick p99 < 10 ms y cada cliente recibe < 30 KB/s.
5. **Dado** la animación **entonces** los remotos usan `walk_<dir>` / `idle_<dir>` según `dir` y `anim` del snapshot.

**Notas técnicas**
- `InterestSystem` con celdas de 16×16 tiles; recalcular pertenencia a celda solo cuando la entidad cambia de celda.
- `tools/LoadBot`: consola .NET que crea N cuentas/personajes y los mueve aleatoriamente (útil para todo el proyecto).

---

### HU-024 · Cámara, capas y nombres sobre personajes
**Como** jugador **quiero** una cámara que me siga y ver nombres **para** orientarme y reconocer a mis amigos.
- Prioridad: Must · Estimación: S · Estado: Pendiente
- Dependencias: HU-023
- Skills: `godot-client`, `pixel-art-assets`

**Criterios de aceptación**
1. **Dado** que me muevo **entonces** la cámara me sigue con suavizado leve, limitada a los bordes del mapa y sin *jitter* de subpíxel.
2. **Dado** entidades **entonces** se ordenan en Y (quien está más abajo se dibuja delante) y pasan por debajo de la capa `above`.
3. **Dado** cualquier jugador **entonces** su nombre aparece encima (blanco; el propio en amarillo; miembros de grupo en azul — HU-061).
4. **Dado** que entro en una zona de `zones` **entonces** aparece su nombre en el centro con fade de 2 s.

---

### HU-025 · Desconexión, linkdead y reconexión
**Como** jugador **quiero** que un corte breve de internet no me saque del juego **para** no perder el ritmo.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-023
- Skills: `dotnet-server`, `godot-client`, `net-protocol`

**Criterios de aceptación**
1. **Dado** que se corta la conexión **entonces** mi personaje queda 10 s en el mundo (quieto) y luego se guarda y desaparece.
2. **Dado** que el cliente reconecta en < 10 s (nuevo ticket automático con el JWT) **entonces** retoma el mismo personaje sin pasar por la pantalla de selección.
3. **Dado** el cliente sin conexión **entonces** muestra "Reconectando… (intento 2/5)" y tras 5 intentos vuelve al login.
4. **Dado** que cierro el juego con la X **entonces** el cliente envía close normal y el servidor guarda de inmediato (sin esperar 10 s) salvo si estoy en combate.

---

### HU-026 · Guardado de posición y estado
**Como** jugador **quiero** aparecer donde lo dejé **para** continuar mi partida.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-025
- Skills: `dotnet-server`

**Criterios de aceptación**
1. **Dado** que salgo y vuelvo a entrar **entonces** aparezco en la misma posición con la misma vida y recurso.
2. **Dado** que el servidor se apaga con Ctrl+C **entonces** guarda a todos los jugadores conectados antes de salir (log con el número guardado).
3. **Dado** un jugador conectado **entonces** se guarda cada 60 s si hubo cambios (`Dirty`).
4. **Dado** que el guardado falla **entonces** reintenta 3 veces con backoff y loguea `error` con el DTO; el tick nunca se bloquea.
5. **Dado** los tests de Persistence **entonces** cubren guardar y cargar un personaje completo (Testcontainers).
