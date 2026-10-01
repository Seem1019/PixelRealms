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
EntityKind "player"|"monster"|"npc"|"loot"
ItemStack { id: string /*uuid*/, templateId: string, qty: number }
SlotRef   { c: "bag"|"equip", i: number }   // equip usa índice de EquipSlot
EquipSlot 0 head,1 neck,2 chest,3 hands,4 legs,5 feet,6 ring,7 main_hand,8 off_hand
```

## Cliente → Servidor

| t | d | Validaciones del servidor |
|---|---|---|
| `Hello` | `{ protocolVersion, ticket }` | ticket válido/no usado/no expirado; versión igual. Si falla → `Error` + close |
| `Ping` | `{ clientTime }` | — |
| `MoveInput` | `{ seq, dx, dy }` dx,dy ∈ {-1,0,1} | seq creciente; vivo; no aturdido/raíz |
| `SelectTarget` | `{ targetId? }` | entidad existe y está en AOI |
| `CastSpell` | `{ spellId, targetId?, targetPos?: Vec2, reqId }` | conoce el hechizo, nivel, CD, GCD, recurso, rango, objetivo válido según `targeting`, LOS, no aturdido (ni silenciado si el hechizo es `magic`), fuera del bloqueo tras interrupción (`locked_out`). Si ya está casteando, el casteo actual se cancela (ADR-019). Área al tope de la instancia → `area_limit`. Hechizos `ground_*`, de cono o línea y con `leap` (ADR-015, ADR-016): `targetPos` obligatorio, a ≤ `range + castRangeToleranceTiles` y con LOS al punto (el cono y la línea solo usan su dirección) |
| `CancelCast` | `{}` | — |
| `AutoAttack` | `{ on: bool }` | tiene arma; objetivo hostil |
| `InventoryMove` | `{ from: SlotRef, to: SlotRef, qty?, reqId }` | ver skill `inventory-items` |
| `UseItem` | `{ itemId, targetId?, reqId }` | item propio, usable, CD de consumibles |
| `DestroyItem` | `{ itemId, qty, reqId }` | item propio, no `questItem` |
| `LootOpen` | `{ lootId }` | distancia ≤ 2 tiles, tiene derecho |
| `LootTake` | `{ lootId, index }` / `LootTakeAll` `{ lootId }` | espacio en bolsa |
| `VendorOpen` / `VendorBuy` / `VendorSell` | `{ npcId }` / `{ npcId, templateId, qty }` / `{ npcId, itemId, qty }` | distancia ≤ 3 tiles, oro, espacio |
| `ChatSend` | `{ channel: "say"|"party"|"global"|"whisper", text, to? }` | 1–200 chars, rate limit, sanitizado |
| `PartyInvite` / `PartyRespond` / `PartyLeave` / `PartyKick` | `{ name }` / `{ accept }` / `{}` / `{ name }` | reglas de grupo (máx 5) |
| `SetHotbar` | `{ slot, kind: "spell"|"item"|null, ref? }` | slot 0–7: 0–3 solo `spell` (teclas 1–4), 4–7 solo `item` (teclas 5–8) (`rules.loadout`, ADR-014) |
| `Respawn` | `{}` | está muerto |
| `UsePortal` | `{ portalId }` | a ≤ 1 tile, vivo, fuera de combate, `minLevel` |
| `DuelRequest` / `DuelRespond` / `DuelForfeit` | `{ name }` / `{ accept }` / `{}` | ruleset `duel` habilitado, ambos vivos, sin duelo activo |
| `TradeRequest` / `TradeRespond` / `TradeOffer` / `TradeConfirm` / `TradeCancel` | `{ name }` / `{ accept }` / `{ items: {itemId, qty}[], gold }` / `{ version }` / `{}` | ≤ 3 tiles, items propios y no bloqueados, `version` vigente |
| `AdminCommand` | `{ text }` | `accounts.is_admin` (si no, `forbidden`); `text` = `/tp x y`, `/tpto Nombre`, `/spawn id [n]`, `/give id [qty] [Nombre]`, `/level n`, `/heal`, `/kill`, `/gold n`, `/god`, `/debug move on\|off`, `/announce texto`; la respuesta llega como `ChatMessage{channel:"system"}` (HU-070) |

## Servidor → Cliente

| t | d | Cuándo |
|---|---|---|
| `Welcome` | `{ selfId, tick, tickRate:20, snapshotRate:10, mapId: "meadow", self: SelfState, inventory, equipment, hotbar, knownSpells: string[], rulesHash }` | tras `Hello` válido (`rulesHash` permite al cliente detectar un `rules.json` distinto) |
| `Snapshot` | `{ tick, ackSeq, self: { x, y, speed, hp, maxHp, res, maxRes }, ents: EntState[] }` (`speed` en tiles/s, incluye auras) | cada 2 ticks |
| `EntitySpawn` | `{ id, kind, templateId, name, x, y, dir, level, classId?, hpPct, flags }` | entra a tu AOI |
| `EntityDespawn` | `{ id, reason: "left"|"died"|"despawn" }` | sale de tu AOI |
| `CastStarted` | `{ casterId, spellId, targetId?, targetPos?, dir?, radius?, durationMs }` | inicio de casteo (a la AOI); en áreas y saltos `targetPos` (y `dir` en cono/línea) fijan la marca en el suelo; la forma y el tamaño salen del contenido (`radius` solo si algo los modifica) |
| `CastEnded` | `{ casterId, spellId, result: "done"|"interrupted"|"cancelled"|"failed", reason? }` | `failed` con `reason: out_of_range\|no_los` si al terminar el objetivo quedó fuera (sin coste) |
| `CombatEvents` | `{ tick, e: { src, dst, spellId?, kind: "dmg"\|"heal"\|"miss"\|"dodge"\|"absorb"\|"immune", amount, crit, school: "physical"\|"magic" }[] }` | una vez por tick y observador con todos los resultados que ve (máx. 64 entradas; si hay más, se parte). Reemplaza al antiguo `CombatEvent` por golpe (ADR-018) |
| `AuraApplied` / `AuraRemoved` | `{ targetId, auraId, casterId?, stacks, durationMs }` / `{ targetId, auraId, casterId? }` | `casterId` distingue instancias del mismo aura de lanzadores distintos (ADR-022) |
| `Cooldown` | `{ spellId, remainingMs }` / `{ gcdMs }` | al castear |
| `StatsUpdate` | `{ level, xp, xpNext, stats, derived, gold }` | al cambiar |
| `XpGain` / `LevelUp` | `{ amount, sourceId? }` / `{ level, newSpells: string[], rankUps?: { spellId, rank }[] }` | `rankUps`: hechizos que subieron de rango (ADR-014) |
| `InventoryUpdate` | `{ bag: (ItemStack|null)[24], equipment: (ItemStack|null)[9], gold, reqId? }` | tras cualquier op (estado completo v1) |
| `LootWindow` | `{ lootId, gold, items: { index, templateId, qty, ownerId, freeInMs }[] }` | tras `LootOpen` |
| `ChangeMap` | `{ mapId, x, y }` | tras `UsePortal`; sigue una AOI nueva completa |
| `DuelUpdate` | `{ state: "requested"\|"countdown"\|"active"\|"ended"\|"declined", opponentId, winnerId?, startsInMs? }` | ciclo de vida del duelo |
| `TradeUpdate` | `{ state: "requested"\|"open"\|"completed"\|"cancelled", partnerId, version, mine: Offer, theirs: Offer, confirmedMine, confirmedTheirs, reason? }` | ciclo de vida del intercambio |
| `VendorWindow` | `{ npcId, items: { templateId, price }[] }` | |
| `ChatMessage` | `{ channel, from, text, ts }` | |
| `PartyUpdate` | `{ leader, members: { name, entityId?, classId, level, hpPct, online }[] }` | |
| `Died` | `{ killerId? , respawnInMs }` | |
| `Error` | `{ code, message?, reqId? }` | códigos abajo |
| `Pong` | `{ clientTime, serverTick }` | |

`EntState` (en `Snapshot`) = `{ id, x, y, dir, hpPct, anim: "idle"|"walk"|"cast"|"attack"|"dead", tgt? }`
— solo campos que cambian con frecuencia. Los estáticos van en `EntitySpawn`.

## Códigos de error
`bad_version`, `bad_ticket`, `rate_limited`, `not_found`, `out_of_range`, `no_los`, `on_cooldown`, `on_gcd`,
`not_enough_resource`, `invalid_target`, `is_dead`, `stunned`, `rooted`, `silenced`, `locked_out`, `area_limit`, `bag_full`,
`not_enough_gold`, `level_too_low`, `not_owner`, `in_combat`, `pvp_not_allowed`, `duel_busy`, `trade_busy`, `trade_version`, `forbidden`, `invalid_payload`.
(`cannot_equip` y `wrong_class` **no existen**: cualquier clase equipa cualquier item, ADR-009. `is_casting` tampoco: un `CastSpell` durante un casteo lo cancela, ADR-019.)

## Reglas de evolución
1. Añadir un campo opcional **no** rompe → no se sube versión.
2. Renombrar/quitar campo, cambiar semántica o tipo → `ProtocolVersion++` y actualizar cliente a la vez.
3. Todo mensaje nuevo: DTO en `PixelRealms.Protocol/Messages/`, registro en `MessageRegistry`,
   handler en `client/autoload/net.gd` (`_handlers`), fila en esta tabla, test de ida y vuelta.
