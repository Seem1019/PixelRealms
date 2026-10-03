---
name: net-protocol
description: Procedimiento para agregar o cambiar mensajes de red cliente⇄servidor (DTO C#, registro, handler, dispatcher GDScript, docs y tests) y reglas de sincronización, predicción y versionado. Úsala siempre que un cambio cruce la red.
---

# Protocolo de red — cómo agregar o cambiar un mensaje

Fuente de verdad: `docs/protocol.md`. Sobre: `{ "t": "<Tipo>", "d": { ...camelCase } }`.

## Checklist para un mensaje NUEVO (hazlo completo, en este orden)
1. **Diseño**: añade la fila en `docs/protocol.md` (dirección, campos, validaciones, cuándo se envía) con un JSON de ejemplo.
2. **DTO C#** en `PixelRealms.Protocol/Messages/`:
   ```csharp
   public sealed record LootTake(int LootId, int Index) : IClientMessage;          // C→S
   public sealed record LootWindow(int LootId, long Gold, IReadOnlyList<LootEntryDto> Items) : IServerMessage; // S→C
   ```
   - Enteros para ids de entidad, `string` para ids de contenido, `long` para oro, `float` para posiciones.
   - Opcionales como `int?` con `[JsonIgnore(Condition = WhenWritingNull)]`.
3. **Registro**: `MessageRegistry.Register<LootTake>("LootTake")` y `[JsonSerializable(typeof(LootTake))]` en
   `ProtocolJsonContext`. El `MessageRouter` deserializa `d` según `t` (lookup en diccionario, no reflexión por mensaje).
4. **Handler** (C→S): clase `LootTakeHandler : IMessageHandler<LootTake>` en el archivo de su dominio
   (`PixelRealms.Server/Net/Handlers/InventoryHandlers.cs`), registrada con `router.Register(...)` en `Hosting/ServerApp.cs`;
   corre en el tick: valida **todo** (existencia, propiedad, distancia, estado vivo, rate limit) → llama al servicio de
   dominio → errores con `ctx.SendError(code, reqId)` (el `reqId` del mensaje si lo trae) usando `ErrorCodes` / códigos de `docs/protocol.md`.
5. **Emisión** (S→C): desde `EventDispatcher`/`SnapshotBuilder`, nunca desde sistemas de dominio directamente.
6. **Cliente**: `net.gd` solo maneja `Pong`, `Error`, `Snapshot` y `CombatEvents`; el resto se registra con
   `Net.register_handler("LootWindow", _on_loot_window)` desde `autoload/game_state.gd` (estado espejo) o
   `scenes/world/world.gd` (que también escucha `Net.message_received`) → actualiza `GameState` o emite señal. Para C→S,
   `Net.send("LootTake", {"lootId": id, "index": i})` desde la UI.
7. **Tests**:
   - `PixelRealms.Protocol.Tests`: serializa → JSON esperado exacto (snapshot de string) → deserializa igual.
   - Handler: test de integración con `TestServer` + `TestGameClient` (`PixelRealms.Server.Tests/Net/*Tests.cs`) que cubra
     éxito + cada código de error; la lógica de dominio, con `WorldBuilder` en `PixelRealms.Game.Tests`.
   - Cliente (GUT) si hay parseo no trivial.

## Cambiar un mensaje existente
- Campo opcional nuevo → compatible, no sube versión.
- Cualquier otro cambio → `ProtocolVersion++` en `PixelRealms.Protocol/ProtocolVersion.cs` **y** `client/scripts/net/protocol.gd`,
  cambiar ambos lados en el mismo commit, anotar en `docs/protocol.md` → sección "Historial".

## Reglas de autoridad (el servidor desconfía de todo)
| El cliente dice… | El servidor hace… |
|---|---|
| "me muevo hacia (dx,dy)" | simula con su propia velocidad/colisión; ignora posiciones del cliente |
| "lanzo X a Y" | valida conocimiento, nivel, CD, GCD, recurso, rango, LOS, estado; calcula resultado |
| "muevo item de A a B" | valida que A es suyo, B válido, reglas de equipo; aplica atómicamente |
| "tomo botín" | valida derecho, distancia, espacio; crea instancias nuevas (UUID v7) |
- Payload > 4 KB, `t` desconocido o JSON inválido → `Error{code:"invalid_payload"}`; 3 veces → desconectar.
- Todo número del cliente se *clampa* (p. ej. `qty` 1..maxStack, `index` dentro de rango).

## Snapshots e interés (AOI)
- Grid de celdas de 16×16 tiles; un jugador observa su celda y las 8 vecinas.
- `EntitySpawn` con datos estáticos al entrar en AOI; `Snapshot.ents` solo datos dinámicos; `EntityDespawn` al salir.
- El jugador propio viaja en `Snapshot.self` (posición autoritativa + `ackSeq`), no en `ents`.
- Eventos de combate se envían solo a observadores de `src` o `dst`.

## Depurar desincronización
1. Activa el overlay de depuración (F3, `scripts/ui/debug_overlay.gd`): muestra RTT, ackSeq, inputs pendientes, error de reconciliación.
2. Log servidor `Debug` de `MovementSystem` para el jugador (`/debug move on`).
3. Reproduce con un test vector nuevo en `shared/test-vectors/movement.json` y hazlo pasar en ambos lados.
