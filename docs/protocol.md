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
| `CastSpell` | `{ spellId, targetId?, reqId }` | conoce el hechizo, nivel, CD, GCD, recurso, rango, objetivo válido según `targeting`, LOS, no casteando, no aturdido/silenciado |
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
| `SetHotbar` | `{ slot, kind: "spell"|"item"|null, ref? }` | slot 0–9 |
| `Respawn` | `{}` | está muerto |
| `AdminCommand` | `{ text }` | `accounts.is_admin` |

## Servidor → Cliente

| t | d | Cuándo |
|---|---|---|
| `Welcome` | `{ selfId, tick, tickRate:20, snapshotRate:10, map: "meadow", self: SelfState, inventory, equipment, hotbar, knownSpells: string[] }` | tras `Hello` válido |
| `Snapshot` | `{ tick, ackSeq, self: { x, y, speed, hp, maxHp, res, maxRes }, ents: EntState[] }` (`speed` en tiles/s, incluye auras) | cada 2 ticks |
| `EntitySpawn` | `{ id, kind, templateId, name, x, y, dir, level, classId?, hpPct, flags }` | entra a tu AOI |
| `EntityDespawn` | `{ id, reason: "left"|"died"|"despawn" }` | sale de tu AOI |
| `CastStarted` | `{ casterId, spellId, targetId?, durationMs }` | inicio de casteo (a la AOI) |
| `CastEnded` | `{ casterId, spellId, result: "done"|"interrupted"|"cancelled" }` | |
| `CombatEvent` | `{ src, dst, spellId?, kind: "dmg"|"heal"|"miss"|"dodge"|"absorb", amount, crit, school }` | resolución |
| `AuraApplied` / `AuraRemoved` | `{ targetId, auraId, stacks, durationMs }` / `{ targetId, auraId }` | |
| `Cooldown` | `{ spellId, remainingMs }` / `{ gcdMs }` | al castear |
| `StatsUpdate` | `{ level, xp, xpNext, stats, derived, gold }` | al cambiar |
| `XpGain` / `LevelUp` | `{ amount, sourceId? }` / `{ level, newSpells: string[] }` | |
| `InventoryUpdate` | `{ bag: (ItemStack|null)[24], equipment: (ItemStack|null)[9], gold, reqId? }` | tras cualquier op (estado completo v1) |
| `LootWindow` | `{ lootId, gold, items: { templateId, qty }[] }` | tras `LootOpen` |
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
`not_enough_resource`, `invalid_target`, `is_dead`, `is_casting`, `stunned`, `silenced`, `bag_full`,
`not_enough_gold`, `cannot_equip`, `level_too_low`, `wrong_class`, `forbidden`, `invalid_payload`.

## Reglas de evolución
1. Añadir un campo opcional **no** rompe → no se sube versión.
2. Renombrar/quitar campo, cambiar semántica o tipo → `ProtocolVersion++` y actualizar cliente a la vez.
3. Todo mensaje nuevo: DTO en `PixelRealms.Protocol/Messages/`, registro en `MessageRegistry`,
   handler en `client/autoload/net.gd` (`_handlers`), fila en esta tabla, test de ida y vuelta.
