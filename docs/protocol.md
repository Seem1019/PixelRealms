# Protocolo cliente ⇄ servidor (v1)

`ProtocolVersion = 1`. Todo mensaje es JSON UTF-8 en un frame de texto WebSocket:

```json
{ "t": "CastSpell", "d": { "spellId": "mage_fireball", "targetId": 42, "reqId": 7 } }
```

- `t`: nombre del tipo (PascalCase, igual al nombre del `record` C# y a la clave del dispatcher GDScript).
- `d`: payload en `camelCase`. Campos opcionales se omiten (no `null`).
- `reqId` (int, opcional): lo pone el cliente en intenciones que esperan respuesta; el servidor lo devuelve en
  `Error` o en la respuesta, para que la UI asocie el resultado.
- Posiciones en **píxeles** del mundo (`float` con 2 decimales). 1 tile = 16 px.
- IDs de entidad: `int` asignado por el servidor, válido solo durante la sesión. IDs de contenido: `string`
  (`"mage_fireball"`). IDs de item instancia: `string` UUID.

## Tipos compartidos
```ts
Vec2      { x: number, y: number }
Dir       "n"|"ne"|"e"|"se"|"s"|"sw"|"w"|"nw"
EntityKind "player"|"monster"|"npc"        // un cadáver es el `monster` muerto (ver flags); no hay entidad "loot"
ItemStack { id: string /*uuid*/, templateId: string, qty: number }
SlotRef   { c: "bag"|"equip", i: number }   // equip usa índice de EquipSlot
EquipSlot 0 head,1 neck,2 chest,3 hands,4 legs,5 feet,6 ring,7 main_hand,8 off_hand
```

## Cliente → Servidor

| t | d | Validaciones del servidor |
|---|---|---|
| `Hello` | `{ protocolVersion, ticket }` | ticket válido/no usado/no expirado; versión igual. Si falla → `Error` + close. Si la cuenta tiene **otro** personaje en el mundo y está en combate → `Error{in_combat}` + close (sacarlo sería escapar de la pelea; el cliente reintenta con un ticket nuevo); fuera de combate, la sesión anterior se guarda y se cierra con `replaced` (HU-014 CA5) |
| `Ping` | `{ clientTime }` | — |
| `MoveInput` | `{ seq, dx, dy }` dx,dy ∈ {-1,0,1} | seq creciente por conexión (vuelve a 1 tras cada `Hello`; un `Welcome` posterior en la misma conexión no lo reinicia); uno por tick de 50 ms con movimiento y uno con 0,0 al parar; vivo; no aturdido/raíz |
| `SelectTarget` | `{ targetId? }` | la entidad existe en el mapa del jugador; si no, el objetivo queda vacío (no se guarda ni se reenvía en `EntState.tgt`) |
| `CastSpell` | `{ spellId, targetId?, targetPos?: Vec2, reqId }` | solo hechizos de clase que conoce (los de objeto se lanzan únicamente con `UseItem`, que paga la recarga y gasta la unidad; los de monstruo, nunca → `not_found`) y que tiene equipados en una casilla de hechizo de la barra (ADR-014; si no, `not_equipped`), nivel, CD, GCD, recurso, rango, objetivo válido según `targeting`, LOS, no aturdido ni silenciado (el silencio bloquea cualquier habilidad, física o mágica), fuera del bloqueo tras interrupción (`locked_out`). Si ya está casteando, el casteo actual se cancela (ADR-019). Un área con casteo cuando la instancia ya tiene `rules.limits.maxAreasPerInstance` marcas en el suelo → `area_limit`. Otro jugador fuera de un duelo activo → `pvp_not_allowed`. Hechizos `ground_*`, de cono o línea y con `leap` (ADR-015, ADR-016): `targetPos` obligatorio, a ≤ `range + castRangeToleranceTiles` y con LOS al punto (el cono y la línea solo usan su dirección) |
| `CancelCast` | `{}` | — |
| `AutoAttack` | `{ on: bool }` | tiene arma; objetivo hostil (otro jugador fuera de duelo → `pvp_not_allowed`) |
| `InventoryMove` | `{ from: SlotRef, to: SlotRef, qty?, reqId }` | ver skill `inventory-items` |
| `UseItem` | `{ itemId, targetId?, reqId }` | item propio, usable, CD de consumibles, no aturdido. Silenciado o bloqueado por una interrupción sí puede usarlo (HU-035 CA3) |
| `DestroyItem` | `{ itemId, qty, reqId }` | item propio, no `questItem` |
| `LootOpen` | `{ lootId }` | vivo (`is_dead`), distancia ≤ `rules.loot.lootRangeTiles` (2), elegible (`not_owner`). Abrir cobra la parte de oro (y el resto de la división, al primero que abre) |
| `LootTake` | `{ lootId, index }` / `LootTakeAll` `{ lootId }` | vivo, distancia, elegible, dueño o ya libre (`not_owner`), espacio en bolsa (`bag_full`). Solo quien puede saquear recibe la `LootWindow` de vuelta |
| `VendorOpen` / `VendorBuy` / `VendorSell` | `{ npcId }` / `{ npcId, templateId, qty }` / `{ npcId, itemId, qty }` | distancia ≤ 3 tiles, oro, espacio |
| `ChatSend` | `{ channel: "say"|"party"|"global"|"whisper"|"who", text, to? }` | 1–200 chars, rate limit, sanitizado. `who` (`/who`) pide la lista de conectados, que llega como `ChatMessage{channel:"system"}` (HU-063) |
| `PartyInvite` / `PartyRespond` / `PartyLeave` / `PartyKick` | `{ name }` / `{ accept }` / `{}` / `{ name }` | reglas de grupo (máx 5) |
| `SetHotbar` | `{ slot, kind: "spell"|"item"|null, ref? }` | slot 0–7: 0–3 solo `spell` (teclas 1–4), 4–7 solo `item` (teclas 5–8) (`rules.loadout`, ADR-014). En combate, una casilla de hechizo ocupada no se cambia ni se vacía (`in_combat`); una vacía sí se puede llenar |
| `Respawn` | `{}` | está muerto |
| `UsePortal` | `{ portalId }` | a ≤ 1 tile, vivo, fuera de combate, `minLevel` |
| `Interact` | `{ objectId, reqId? }` | HU-083: usar una palanca del mapa (id de la capa `levers`): vivo, a ≤ `rules.world.interactRangeTiles` (`out_of_range`), que exista (`not_found`). Con la puerta abierta, tirar de cualquiera de sus palancas renueva el plazo; una palanca `opensAlone` (la de dentro de la sala) abre su puerta ella sola. El cambio llega como `MapObjects` a todos los del mapa |
| `DuelRequest` / `DuelRespond` / `DuelForfeit` | `{ name }` / `{ accept }` / `{}` | ruleset `duel` habilitado, ambos vivos, sin duelo ni intercambio, **ambos fuera de combate** (`in_combat`: si no, el duelo serviría para que los monstruos los soltaran), a ≤ `maxDistanceTiles` y fuera de zona segura si el ruleset lo exige. Al aceptar se revalida todo; si alguien entra en combate durante la cuenta atrás, el duelo se retira. `DuelForfeit` antes de que empiece retira el reto (`declined`: nadie gana). Al terminar (`ended`) cada uno se queda con la vida y el recurso con que acabó y pierde las auras que le puso el rival; quien pierde por vida (al `endAtHpPct`; rendirse o alejarse no cuenta) regenera vida × `loserRegenMult` y sin esperar `hpRegenDelaySec` hasta la vida con que empezó el duelo o hasta volver a entrar en combate (HU-064 CA3). En duelo activo, un duelista no es aliado de nadie más (ni cura ni lo curan) y se aleja del punto de inicio más de `maxDistanceTiles` → pierde |
| `TradeRequest` / `TradeRespond` / `TradeOffer` / `TradeConfirm` / `TradeCancel` | `{ name }` / `{ accept }` / `{ items: {itemId, qty}[], gold }` / `{ version }` / `{}` | ≤ 3 tiles, items propios y no bloqueados, `version` vigente |
| `ChangeClass` | `{ npcId, classId, reqId? }` | NPC `class_change` a ≤ `vendorRangeTiles`, vivo, fuera de combate, sin duelo ni intercambio, clase distinta (HU-044); responde con `Welcome` + `StatsUpdate` + `InventoryUpdate` por la misma conexión y un `EntitySpawn` renovado a quien lo ve |
| `OnlineListRequest` | `{}` | lista de conectados (tecla O, HU-063); responde `OnlineList` |
| `Logout` | `{ reqId? }` | volver a la selección de personaje o salir del juego (HU-015). En combate (`Actor.IsInCombat`) → `Error{in_combat}` y el jugador sigue dentro. Si no: cancela el casteo, cancela duelo e intercambio como al desconectarse, guarda, saca al jugador del mundo (los demás reciben `EntityDespawn{reason:"left"}`), responde `LoggedOut` y cierra la conexión con motivo `logout` cuando la respuesta ya salió (si el cliente deja de leer, se cierra igual a los 2 s). Sin personaje en el mundo (antes del `Welcome`) también responde `LoggedOut` y cierra. Ejemplo: `{"t":"Logout","d":{"reqId":12}}` |
| `AdminCommand` | `{ text }` | `accounts.is_admin` (si no, `forbidden`); `text` = `/tp x y`, `/tpto Nombre`, `/spawn id [n]`, `/give id [qty] [Nombre]`, `/level n`, `/heal`, `/kill`, `/gold n`, `/god`, `/debug move on\|off`, `/announce texto`, `/reload rules` (relee `content/rules.json`; si es inválido conserva el anterior y responde con el error; si vale, recalcula las stats y cada jugador recibe `StatsUpdate`, HU-003 CA4c); la respuesta llega como `ChatMessage{channel:"system"}` (HU-070) |

## Servidor → Cliente

| t | d | Cuándo |
|---|---|---|
| `Welcome` | `{ selfId, tick, tickRate:20, snapshotRate:10, mapId: "meadow", self: SelfState, inventory, equipment, hotbar, knownSpells: string[], rulesHash }` | tras `Hello` válido (`rulesHash` permite al cliente detectar un `rules.json` distinto), seguido siempre de un `StatsUpdate`. Tras `ChangeClass` se reenvía por la misma conexión: el cliente solo actualiza clase, hechizos, barra y vitales (no es una entrada nueva: sin AOI reenviada ni `seq` reiniciado) |
| `Snapshot` | `{ tick, ackSeq, self: { x, y, speed, hp, maxHp, res, maxRes }, ents: EntState[] }` (`speed` en tiles/s, incluye auras) | cada 2 ticks |
| `EntitySpawn` | `{ id, kind, templateId, name, x, y, dir, level, classId?, hpPct, flags }` | entra a tu AOI. `flags`: 2 = muerto, 4 = evadiendo, 8 = cadáver con botín para ti (se reenvía al soltar botín, HU-050 CA1) |
| `EntityDespawn` | `{ id, reason: "left"|"died"|"despawn" }` | sale de tu AOI |
| `CastStarted` | `{ casterId, spellId, targetId?, targetPos?, dir?, radius?, durationMs }` | inicio de casteo (a la AOI); en áreas y saltos `targetPos` fija la marca en el suelo; la forma y el tamaño salen del contenido. Los instantáneos también lo emiten, con `durationMs: 0` y seguido de `CastEnded{done}`. `dir` (cono/línea) y `radius` hoy no se envían nunca: el cono y la línea no están implementados (ADR-023) y nada modifica el radio |
| `CastEnded` | `{ casterId, spellId, result: "done"|"interrupted"|"cancelled"|"failed", reason? }` | `failed` (sin coste) con `reason`: `out_of_range`/`no_los` si al terminar el objetivo quedó fuera, `invalid_target` si murió o desapareció, `not_enough_resource` si ya no hay recurso |
| `CombatEvents` | `{ tick, e: { src, dst, spellId?, kind: "dmg"\|"heal"\|"miss"\|"dodge"\|"absorb"\|"immune", amount, crit, school: "physical"\|"magic" }[] }` | una vez por tick y observador con todos los resultados que ve (máx. 64 entradas; si hay más, se parte). Reemplaza al antiguo `CombatEvent` por golpe (ADR-018) |
| `AuraApplied` / `AuraRemoved` | `{ targetId, auraId, casterId?, stacks, durationMs }` / `{ targetId, auraId, casterId? }` | `casterId` distingue instancias del mismo aura de lanzadores distintos (ADR-022) |
| `Cooldown` | `{ spellId, remainingMs }` / `{ templateId, remainingMs }` / `{ gcdMs }` | al castear; al usar un consumible con `useCooldownMs` (`templateId`: la recarga es compartida por plantilla); y tras cada `Welcome` uno por hechizo o consumible aún en recarga (guardados al salir, HU-015). `templateId` es opcional y aditivo: un cliente que no lo conozca lo ignora |
| `StatsUpdate` | `{ level, xp, xpNext, stats, derived, gold }` | tras cada `Welcome` (por la misma conexión, después de él) y al cambiar |
| `XpGain` / `LevelUp` | `{ amount, sourceId? }` / `{ level, newSpells: string[], rankUps?: { spellId, rank }[] }` | `rankUps`: hechizos que subieron de rango (ADR-014) |
| `InventoryUpdate` | `{ bag: (ItemStack|null)[24], equipment: (ItemStack|null)[9], gold, reqId? }` | tras cualquier op (estado completo v1) |
| `LootWindow` | `{ lootId, gold, items: { index, templateId, qty, ownerId, freeInMs }[] }` | tras `LootOpen`, `LootTake` y `LootTakeAll`. `gold` = oro de ese cadáver para quien mira (ya abonado al abrir) |
| `ChangeMap` | `{ mapId, x, y }` | al cruzar un portal (pisándolo; el cliente actual no envía `UsePortal`) o con `/tpto`; sigue una AOI nueva completa. Un casteo en curso termina antes con `CastEnded{cancelled}`; en pleno salto (`leap` con `travelMs`) no se cruza ningún portal hasta aterrizar |
| `DuelUpdate` | `{ state: "requested"\|"countdown"\|"active"\|"ended"\|"declined", opponentId, winnerId?, startsInMs? }` | ciclo de vida del duelo |
| `TradeUpdate` | `{ state: "requested"\|"open"\|"completed"\|"cancelled", partnerId, version, mine: Offer, theirs: Offer, confirmedMine, confirmedTheirs, reason? }` con `Offer = { items: {itemId, templateId, qty}[], gold }` | ciclo de vida del intercambio; `templateId` (aditivo) dice qué objeto ofrece cada uno. No se puede intercambiar en un duelo ni retar a duelo en un intercambio (`duel_busy` / `trade_busy`) |
| `VendorWindow` | `{ npcId, items: { templateId, price }[] }` | |
| `ChatMessage` | `{ channel, from, text, ts }` | |
| `PartyUpdate` | `{ leader, members: { name, entityId?, classId, level, hpPct, resPct?, online, mapId }[] }` (`resPct`, aditivo: recurso en %, HU-062 CA1) | al cambiar el grupo y cada 10 ticks. La invitación no tiene mensaje propio: `{ leader: <quien invita>, members: [] }` es una invitación (Aceptar/Rechazar) y `{ leader: "", members: [] }` avisa de que tu grupo se disolvió |
| `Died` | `{ killerId? , respawnInMs }` | al morir el jugador propio. `respawnInMs` siempre 0: se reaparece a mano con `Respawn` |
| `Error` | `{ code, message?, reqId? }` | códigos abajo |
| `Pong` | `{ clientTime, serverTick }` | |
| `OnlineList` | `{ players: { name, classId, level, zone }[] }` | respuesta a `OnlineListRequest`, ordenada por nombre; `zone` = zona de Tiled donde está (o el nombre del mapa) |
| `MapObjects` | `{ objects: { id, state }[] }` | HU-083: palancas (`on`/`off`) y puertas (`open`/`closed`) del mapa. Tras `Welcome`, una reconexión o `ChangeMap` llegan todas las del mapa (si tiene); después, solo las que cambian, a todos los del mapa. Una puerta cerrada es sólida y tapa la vista: el cliente la aplica a su rejilla de colisión para predecir igual que el servidor |
| `LoggedOut` | `{}` | `Logout` aceptado: el personaje ya está guardado (encolado) y fuera del mundo; el servidor cierra después la conexión. El cliente cierra con `disconnect_from_server()` (sin reconexión) y vuelve a la selección de personaje con el mismo token. Un `Hello` posterior del mismo personaje espera (máx. 3 s) a que ese guardado esté escrito antes de leerlo de la BD; si aún no lo está, entra con el estado con el que salió (el servidor lo guarda en memoria hasta que el personaje vuelve a entrar). Un `Hello` del mismo personaje mientras sigue dentro toma ese personaje vivo (la conexión anterior se cierra con `replaced`) en vez de recargarlo |

`EntState` (en `Snapshot`) = `{ id, x, y, dir, hpPct, anim: "idle"|"walk"|"cast"|"attack"|"dead", tgt? }`
— solo campos que cambian con frecuencia. Los estáticos van en `EntitySpawn`.

## Códigos de error
`bad_version`, `bad_ticket`, `rate_limited`, `not_found`, `out_of_range`, `no_los`, `on_cooldown`, `on_gcd`,
`not_enough_resource`, `invalid_target`, `is_dead`, `stunned`, `rooted`, `silenced`, `locked_out`, `area_limit`, `not_equipped`, `bag_full`, `in_combat`,
`not_enough_gold`, `level_too_low`, `not_owner`, `pvp_not_allowed`, `duel_busy`, `trade_busy`, `trade_version`, `forbidden`, `invalid_payload`.
(`cannot_equip` y `wrong_class` **no existen**: cualquier clase equipa cualquier item, ADR-009. `is_casting` tampoco: un `CastSpell` durante un casteo lo cancela, ADR-019.)

## Reglas de evolución
1. Añadir un campo opcional **no** rompe → no se sube versión.
2. Renombrar/quitar campo, cambiar semántica o tipo → `ProtocolVersion++` y actualizar cliente a la vez.
3. Todo mensaje nuevo: DTO en `PixelRealms.Protocol/Messages/`, registro en `MessageRegistry`, handler en el cliente
   (`net.gd` solo atiende `Pong`, `Error`, `Snapshot` y `CombatEvents`; el resto se registra con `Net.register_handler` desde
   `game_state.gd` o `world.gd`), fila en esta tabla, test de ida y vuelta.
4. Un mensaje nuevo que solo se usa cuando el jugador lo pide (p. ej. `Logout`/`LoggedOut`, HU-015) no rompe a los clientes
   anteriores: el servidor nunca lo envía sin que el cliente lo haya pedido. Cliente y servidor se publican juntos, así que
   **no** se sube `protocolVersion` (sigue en 1).

## Historial
- v1 · HU-015: `Logout` (C→S) y `LoggedOut` (S→C), sin subir versión (regla 4).
- v1 · auditoría de la Fase 1 (2026-10-02), sin subir versión (validaciones nuevas y campos que ya existían, ahora documentados):
  `CastSpell` rechaza hechizos de objeto y de monstruo; los duelos exigen estar fuera de combate; `Hello` responde `in_combat`
  si el otro personaje de la cuenta pelea; `LootWindow.gold` muestra el oro cobrado; `SelectTarget` solo guarda entidades del mapa.
- v1 · HU-063: `OnlineListRequest` (C→S) y `OnlineList` (S→C), sin subir versión (regla 4: solo se envía si el cliente lo pide).
- v1 · HU-083: `Interact` (C→S) y `MapObjects` (S→C), sin subir versión: un cliente antiguo no tiene palancas que usar y el
  servidor solo envía `MapObjects` en mapas con objetos. `CastSpell` responde `not_equipped` si el hechizo no está en la barra.
  HU-062: `PartyUpdate.members[].resPct` (aditivo).

## REST (HTTP)
| Método y ruta | Cuerpo / auth | Respuesta |
|---|---|---|
| `POST /api/auth/register` | `{ username, password }` | `201 { id, username }` · `400 { errors }` · `409 username_taken` · 5 por hora e IP |
| `POST /api/auth/login` | `{ username, password }` | `200 { token, expiresAt }` (JWT de 15 min) · `401 invalid_credentials` · 5 por minuto e IP |
| `POST /api/auth/refresh` | `Authorization: Bearer <jwt>` | `200 { token, expiresAt }` con la cuenta releída de la BD (el flag de admin sale de la BD) · `401`. El cliente lo llama cada 10 min mientras hay sesión (HU-015 CA1) |
| `GET` / `POST /api/characters`, `DELETE /api/characters/{id}` | Bearer | lista, crea (`201`) y borra (`204`) personajes propios (HU-012, HU-013) |
| `POST /api/game/ticket` | Bearer, `{ characterId }` | `200 { ticket }` (un solo uso, 30 s) para `Hello` |
| `GET /health` | — | `{ status, players, tickP99Ms, uptime, tick }` |
| `GET /admin/stats` | Bearer de una cuenta admin | métricas del servidor (HU-072) |

En Producción, detrás de Caddy, los límites por IP usan la IP de `X-Forwarded-For`: `UseForwardedHeaders` va antes que
`UseRateLimiter` (si no, todos los jugadores compartirían el cupo de la IP del proxy). La cabecera solo se cree si la conexión
viene de una red de confianza (`Net:TrustedProxyNetworks`; por defecto loopback y rangos privados, donde vive el contenedor de
Caddy): desde cualquier otra IP no se puede fingir el origen.
