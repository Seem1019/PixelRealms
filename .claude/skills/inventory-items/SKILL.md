---
name: inventory-items
description: Modelo e invariantes de items, inventario (bolsa de 24), equipo (9 slots), apilado, botín, consumibles, vendedor y oro, más persistencia y prevención de duplicación. Úsala al tocar inventario, equipo, loot, vendedores o la UI de items.
---

# Items, inventario y economía

## Modelo (PixelRealms.Game/Items)
```csharp
public sealed class ItemInstance { Guid Id; string TemplateId; int Qty; }   // Id = Guid.CreateVersion7()
public sealed class Inventory   { ItemInstance?[] Bag = new ItemInstance?[24]; long Gold; }
public sealed class Equipment   { ItemInstance?[] Slots = new ItemInstance?[9]; } // índice = EquipSlot
```
`EquipSlot`: 0 head, 1 neck, 2 chest, 3 hands, 4 legs, 5 feet, 6 ring, 7 main_hand, 8 off_hand.

## Invariantes (tests de propiedad obligatorios)
1. **Conservación**: ninguna operación crea ni destruye cantidad salvo las que lo declaran
   (`Loot`, `VendorBuy`, `Destroy`, `Use`, `AdminGive`). Test: suma de `(templateId → qty)` antes/después.
2. Un `ItemInstance.Id` aparece **una sola vez** en todo el mundo (bolsa + equipo de todos los jugadores + loot bags).
3. `1 ≤ Qty ≤ template.maxStack`. Los no apilables tienen `Qty == 1`.
4. En equipo solo hay items con `slot` coincidente y `levelReq ≤ nivel`. **No hay restricción por clase ni tipo**
   (ADR-009): la afinidad (`rules.affinity.byClass[clase][tipo]`) multiplica la contribución del item en `StatCalculator`,
   nunca impide equiparlo. `classes[].recommended*` es solo informativo.
5. `Gold ≥ 0` siempre.
6. Toda operación es **atómica**: o se aplica completa o no cambia nada (valida todo primero, muta después).

## Operaciones (`InventoryOps`, todas devuelven `OpResult { Ok, ErrorCode }`)
| Op | Semántica |
|---|---|
| `Move(from, to, qty?)` | bag→bag: vacío = mover; mismo template apilable = fusionar (sobrante queda en origen); distinto = intercambiar. `qty` < total en destino vacío = dividir (nuevo Id) |
| `Equip(bagIdx)` | = `Move(bag→equip[slot del item])`; si hay algo equipado, intercambia. `ring` y `main_hand` son únicos en MVP. Único error posible: `level_too_low` |
| `Trade*` | `TradeSession` con ofertas versionadas y doble confirmación; commit atómico con `locked` en los items ofrecidos (HU-059) |
| `Unequip(slot, bagIdx?)` | al primer hueco libre si no se indica; `bag_full` si no hay |
| `Use(itemId)` | consumible: valida `useCooldownMs` (CD compartido por template), castea `useSpellId` vía `CastSystem` con el jugador como lanzador; si el cast se acepta, `Qty -= 1` |
| `Destroy(itemId, qty)` | registra en `item_audit_log` |
| `AddItem(template, qty)` | llena stacks existentes, luego huecos vacíos; si no cabe TODO → falla sin cambios (`bag_full`) |
Cambiar equipo ⇒ `actor.MarkStatsDirty()` ⇒ `StatsUpdate`. Si baja `maxHp`, `hp = min(hp, maxHp)`.

## Botín (ADR-012, constantes en `rules.loot`)
- Al morir un monstruo: `LootSystem.Roll(table, rng)` → cada `entries[i]` tira independiente `rng < chance`, cantidad
  `rng.Next(min, max+1)`; de cada `groups[j]` caen exactamente `rolls` items por peso sin repetir; se ordenan por rareza
  y los de `entries` se cortan a `maxItems`. Oro `rng.Next(min, max+1)`.
- **Asignación por item:** `elegibles` = quien hizo el primer daño o los miembros de su grupo vivos a ≤ `eligibleRangeTiles`.
  Cada item se asigna `elegibles[rng.Next(count)]` (uniforme, independiente por item). `LootBag { Id, MapInstanceId, Position,
  ExpiresAtMs = now + corpseLifetimeSec, Gold, Entries: { templateId, qty, ownerCharacterId, freeAtMs = now + exclusiveSec } }`.
- El cadáver brilla para quien tiene ≥ 1 entrada propia; `LootOpen` lo puede hacer cualquier elegible y ve todas las entradas con su dueño.
- `LootTake`: distancia ≤ `lootRangeTiles`, `owner == yo` o `now ≥ freeAtMs`, `AddItem` ok → quita entrada; si no, `Error{not_owner|bag_full}`.
  Oro se reparte a partes iguales entre elegibles al abrir (el resto al que lootea). Todo `LootTake` genera instancias nuevas con Id nuevo.
- Items de rareza ≥ `announceRarityFrom` muestran aviso en chat de grupo al caer: "[Espada de hierro] → Ana".

## Vendedor
- Precio de compra: `vendorPrice ?? sellPrice × rules.economy.vendorBuyMultiplier`. Venta: `sellPrice × qty`. `sellPrice == 0` → no se puede vender.
- Validar distancia ≤ 3 tiles al NPC en cada operación. Operación atómica: oro y items en el mismo paso.
- Recompra (buyback) fuera del MVP.

## Persistencia
- El inventario completo se guarda con el personaje (ver skill `dotnet-server` §Persistencia).
- `container`: 0 bag, 1 equip; `slot` = índice. Restricción `UNIQUE(character_id, container, slot)`.
- Al cargar: si un `template_id` ya no existe en `content/`, el item se mueve a un "correo perdido" (log `warn`) y no
  se crashea. Si hay dos items en el mismo slot (no debería), se mueve el segundo al primer hueco y se loguea `error`.

## Cliente (UI)
- `InventoryUpdate` trae el estado completo → `GameState.inventory` → la UI se redibuja entera (24 celdas, barato).
- Tooltip (`scripts/ui/tooltip_builder.gd`): nombre en color de rareza, slot + tipo, **Afinidad: alta/media/baja (×mult)**
  para mi clase (verde/amarillo/rojo), daño `min–max` y velocidad (s) **ya multiplicados por la afinidad y el haste de clase**
  (base entre paréntesis si difiere), DPS, escuela del básico (físico/mágico), armadura, stats `+2 Fuerza`, nivel requerido
  (rojo si no alcanza), precio de venta, y **comparación** con lo equipado (`▲ +3 Int` verde / `▼ −1 Agi` rojo).
- Drag & drop envía `InventoryMove`; clic derecho = `Equip`/`Use`; Shift+clic en stack = dividir (diálogo de cantidad);
  arrastrar fuera de la ventana = confirmar destruir.
- Oro: `money_format.gd` → `12o 34p 56c` con íconos.
