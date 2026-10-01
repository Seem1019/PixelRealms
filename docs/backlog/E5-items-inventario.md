# E5 · Items e inventario

> Todas las HUs de esta épica deben cumplir los invariantes de la skill `inventory-items`.

### HU-050 · Botín de monstruos
**Como** jugador **quiero** recoger el botín de los monstruos que mato **para** conseguir equipo y oro.
- Prioridad: Must · Estimación: L · Estado: Hecha
- Dependencias: HU-037, HU-051
- Skills: `inventory-items`, `net-protocol`, `godot-client`

**Criterios de aceptación**
1. **Dado** un monstruo que maté (o mi grupo) **cuando** muere **entonces** se tira su tabla de botín (`entries` independientes + `groups` garantizados) y **cada item se asigna al azar (uniforme) a un miembro elegible** (vivo, a ≤ `rules.loot.eligibleRangeTiles`); el cadáver brilla solo para quienes ganaron algo.
2. **Dado** que hago clic en el cadáver a ≤ 2 tiles **entonces** se abre la ventana de botín con oro e items (color de rareza) y el **nombre del dueño** de cada item; los que no son míos aparecen atenuados y no se pueden tomar (`Error{not_owner}`).
3. **Dado** "Tomar todo" o clic en un item **entonces** pasa a mi bolsa; si no cabe, `Error{bag_full}` y queda en el cadáver.
4. **Dado** que pasan `rules.loot.exclusiveSec` (30 s) **entonces** cualquiera del grupo puede tomar lo no reclamado; a los `corpseLifetimeSec` (60 s) el cadáver desaparece con lo que quede.
4b. **Dado** 4 miembros elegibles y 1 000 kills simulados con `SeededRng` **entonces** cada uno recibe ≈ 25 % de los items (±3 %) y la probabilidad de que un mismo jugador se lleve los 3 items de un cadáver es ≈ 1/16.
4c. **Dado** el Capataz **entonces** siempre cae exactamente un item de su `groups` (uno de los tres raros).
5. **Dado** dos jugadores que envían `LootTake` del mismo item en el mismo tick **entonces** solo uno lo recibe (test).
6. **Dado** tests con `FixedRng` **entonces** cubren probabilidades, cantidades `min..max`, `maxItems`, `groups` por peso, asignación por item y oro repartido a partes iguales.

**Notas de implementación**
- `Items/LootSystem`: tirada de la tabla (`entries` independientes, `groups` por peso sin repetir, orden por rareza, corte a `maxItems`), elegibles = quien taggeó (vivos a ≤ `eligibleRangeTiles`; el grupo llega con HU-062 por `EligibleFor`), cada item asignado al azar uniforme, oro a partes iguales (resto al primero que abre), `LootOpen`/`LootTake`/`LootTakeAll` con `lootRangeTiles`, `not_owner` hasta `exclusiveSec`, `bag_full` deja el item; el cadáver brilla para los ganadores (`EntitySpawn.flags` bit 8) y desaparece al quedar saqueado o a los `corpseLifetimeSec`. Cliente: clic en el cadáver → ventana con oro, items con color de rareza y dueño (ajenos atenuados), "Tomar todo".
- Tests: `LootAndVendorTests` (FixedRng, Capataz siempre 1 del grupo, 1 000 kills ≈ 25 % ±3 % y ≈ 1/16, open/take/exclusive/bag_full, flujo en tick). CA5 (dos `LootTake` en el mismo tick): la cola única del tick serializa los mensajes, el segundo recibe `not_found`.

---
### HU-051 · Inventario (bolsa de 24)
**Como** jugador **quiero** una bolsa donde guardar y ordenar mis objetos **para** gestionar lo que llevo.
- Prioridad: Must · Estimación: L · Estado: Hecha
- Dependencias: HU-014
- Skills: `inventory-items`, `godot-client`, `net-protocol`

**Criterios de aceptación**
1. **Dado** la tecla I **entonces** se abre la bolsa de 6×4 casillas con íconos, cantidades y borde de rareza, y el oro abajo (`12o 34p 56c`).
2. **Dado** que arrastro un item a otra casilla **entonces** se envía `InventoryMove` y tras `InventoryUpdate` queda movido (vacía), fusionado (mismo apilable) o intercambiado.
3. **Dado** un `InventoryMove` con índices fuera de rango, item ajeno o `qty` inválida **entonces** `Error{invalid_payload|not_found}` y nada cambia.
4. **Dado** tests de propiedad (1 000 operaciones aleatorias con `SeededRng`) **entonces** se mantienen los invariantes 1–6 de la skill.

**Notas de implementación**
- `Items/InventoryOps.Move`: vacío = mover, mismo apilable = fusionar (sobrante en origen), distinto = intercambiar, `qty` menor sobre vacío = dividir con id nuevo; índices/qty inválidos → `invalid_payload`/`not_found` sin cambios. `InventoryUpdate` completo tras cada operación (`InventoryChangedEvent`). Cliente: bolsa 6×4 con tecla I, cantidades, borde de rareza por color, oro `12o 34p 56c`, arrastrar → `InventoryMove`.
- Tests: `InventoryOpsTests` incl. propiedad de 1 000 operaciones con `SeededRng` (conservación, ids únicos, `1 ≤ qty ≤ maxStack`, slot y nivel en equipo, oro ≥ 0) e `InventoryFlowTests` por WebSocket.

---
### HU-052 · Equipar y desequipar
**Como** jugador **quiero** equiparme cualquier arma o armadura **para** mejorar mis estadísticas o jugar a mi manera, sabiendo cuánto rinde para mi clase.
- Prioridad: Must · Estimación: M · Estado: Hecha
- Dependencias: HU-051
- Skills: `inventory-items`, `combat-system`

**Criterios de aceptación**
1. **Dado** un item equipable **cuando** hago clic derecho o lo arrastro a su slot **entonces** se equipa (intercambiando con lo que hubiera) y mis stats se recalculan (`StatsUpdate`).
2. **Dado** un item de nivel superior **entonces** `Error{level_too_low}`. **No existe** restricción por clase ni por tipo: un Mago equipa placas y espada (afinidad baja) y sus stats se recalculan con `rules.affinity` y `rules.classScaling`.
2b. **Dado** un Mago que equipa `iron_sword` (+2 str, +1 sta, afinidad baja ×0.7) **entonces** recibe +1.4 str y +0.7 sta, es decir `attackPower +0.84` (1.4 · ap.str 0.6) y `maxHp +7` (0.7 · hpPerSta 10); el daño del arma cuenta como 4.2–7.7. Test con números exactos para las 4 clases.
3. **Dado** que desequipo con la bolsa llena **entonces** `Error{bag_full}`.
4. **Dado** que cambio de arma **entonces** el ataque básico usa el daño, la escuela (`scaling`), el rango y la velocidad nuevos desde el siguiente swing.
5. **Dado** que me quito un item con +aguante **entonces** mi vida actual no supera la nueva máxima.
6. **Dado** otros jugadores **entonces** ven el cambio de arma si el sprite lo soporta (post-MVP: solo arma principal).

**Notas de implementación**
- `InventoryOps.Equip/Unequip/Move(bag→equip)`: intercambio con lo equipado, único error de item `level_too_low`, sin restricción por clase/tipo (ADR-009); `MarkStatsDirty` → `CombatServices.Recalculate` → `StatsUpdate`; la vida se recorta al nuevo máximo. El básico lee el arma equipada en cada swing (`WeaponOf`). Cliente: clic derecho equipa.
- Tests: Mago + `iron_sword` exactos (+1.4 str, +0.7 sta, +7 HP, afinidad 0.7) y las 4 clases en `DerivedStats_AllFormulas_AllClasses`; CA6 (sprite del arma) post-MVP.

---
### HU-053 · Tooltips y comparación
**Como** jugador **quiero** ver la información de un item y compararlo con lo que llevo **para** decidir si me conviene.
- Prioridad: Must · Estimación: M · Estado: Hecha
- Dependencias: HU-052
- Skills: `inventory-items`, `godot-client`

**Criterios de aceptación**
1. **Dado** que paso el ratón sobre un item (bolsa, equipo, botín, vendedor) **entonces** veo el tooltip con todos los campos de la skill `inventory-items` §Cliente.
2. **Dado** un equipable **entonces** se muestran las diferencias con el item equipado en ese slot (▲ verde / ▼ rojo) incluido DPS del arma.
3. **Dado** un requisito no cumplido **entonces** aparece en rojo.
3b. **Dado** cualquier equipable **entonces** el tooltip muestra "Afinidad: alta/media/baja (×1.0/×0.85/×0.7)" para mi clase (verde/amarillo/rojo) y los valores del item **ya multiplicados** por la afinidad, con el valor base entre paréntesis si difiere.
4. **Dado** `tooltip_builder.gd` **entonces** tiene tests GUT con 3 items de ejemplo (arma, armadura, consumible).

**Notas de implementación**
- `client/scripts/ui/tooltip_builder.gd` (+ `money_format.gd`): nombre en color de rareza, slot/tipo, afinidad de mi clase con ×mult (verde/amarillo/rojo), daño/velocidad/armadura/poder/stats ya multiplicados con el base entre paréntesis si difiere, DPS con haste, escuela del básico, nivel requerido en rojo si no alcanza, venta y comparación ▲/▼ con lo equipado. Tests GUT `test_tooltip_builder.gd` (arma, armadura, consumible, DPS).

---
### HU-054 · Usar consumibles
**Como** jugador **quiero** usar pociones y comida **para** recuperarme durante y después del combate.
- Prioridad: Must · Estimación: M · Estado: Hecha
- Dependencias: HU-051, HU-035
- Skills: `inventory-items`, `combat-system`

**Criterios de aceptación**
1. **Dado** una Poción menor de vida **cuando** la uso **entonces** recupero 60 de vida, se consume 1 y entra en CD de 60 s compartido por todas las pociones de ese tipo.
2. **Dado** el Pan **entonces** aplica una cura de 50 en 15 s.
3. **Dado** que uso una poción en CD **entonces** `Error{on_cooldown}` y no se consume.
4. **Dado** el último item del stack **entonces** la casilla queda vacía.

**Notas de implementación**
- `Items/ItemUseService`: `useCooldownMs` compartido por plantilla (`Player.ItemCooldownEndsAtMs`), lanza `useSpellId` con el jugador como lanzador sin cancelar su casteo ni activar GCD (ADR-019), `Qty -= 1` solo si el hechizo se acepta, el último deja la casilla vacía; `on_cooldown` no consume. Poción 60 de vida, Pan HoT de 50 en 15 s (contenido).
- Tests: `UseItem_Potion_Heals_Consumes_SharedCooldown_Bread_Hot`.

---
### HU-055 · Oro y vendedor NPC
**Como** jugador **quiero** vender basura y comprar pociones **para** aprovechar el oro que consigo.
- Prioridad: Must · Estimación: M · Estado: Hecha
- Dependencias: HU-051, HU-050
- Skills: `inventory-items`, `world-maps`, `net-protocol`

**Criterios de aceptación**
1. **Dado** el NPC Marta en el pueblo **cuando** hago clic a ≤ 3 tiles **entonces** se abre su ventana con los items de `vendors.json` y precios.
2. **Dado** oro suficiente y espacio **cuando** compro 5 pociones **entonces** se descuenta el oro exacto y aparecen en la bolsa (fusionando stacks).
3. **Dado** la ventana abierta **cuando** hago clic derecho en un item de mi bolsa **entonces** lo vendo por `sellPrice × qty`; los items con `sellPrice: 0` no se pueden vender.
4. **Dado** un botón "Vender basura" **entonces** vende todos los items de rareza `junk` de una vez.
5. **Dado** que me alejo > 3 tiles **entonces** la ventana se cierra y el servidor rechaza operaciones.

**Notas de implementación**
- NPC de la capa `npcs` como `Npc` (actor, `EntitySpawn kind: npc, templateId: vendor`); `Items/VendorService`: `VendorOpen` → `VendorWindow` con precios (`vendorPrice ?? sellPrice × vendorBuyMultiplier`), compra atómica (oro exacto, fusiona stacks, `not_enough_gold`/`bag_full`), venta `sellPrice × qty` (0 → no se vende), distancia ≤ `vendorRangeTiles` en cada operación. Cliente: clic en Marta abre la ventana, clic derecho en la bolsa vende, "Vender basura" vende los `junk`, se cierra al alejarse.
- Tests: `Vendor_Buy_Sell_Range_Gold_Junk`, `InventoryFlowTests.Vendor_Open_Buy_Sell_ThenPersistWithAudit`.

---
### HU-056 · Dividir, fusionar y destruir stacks
**Como** jugador **quiero** manipular pilas de objetos **para** organizar mi bolsa.
- Prioridad: Should · Estimación: S · Estado: Hecha
- Dependencias: HU-051
- Skills: `inventory-items`, `godot-client`

**Criterios de aceptación**
1. **Dado** Shift+clic en un stack **entonces** aparece un diálogo de cantidad; al soltar en casilla vacía se crea un stack nuevo con un id nuevo.
2. **Dado** arrastrar un item fuera de la ventana **entonces** pide confirmación "¿Destruir X?" y al aceptar se elimina (registro en auditoría).
3. **Dado** `qty` 0, negativa o mayor que el stack **entonces** el servidor la rechaza.

**Notas de implementación**
- Dividir (Shift+clic → diálogo → el siguiente arrastre lleva `qty`; id nuevo en servidor), destruir (soltar fuera → "¿Destruir X?" → `DestroyItem`, auditoría "destroy"), `qty` 0/negativa/mayor → `invalid_payload`.

---
### HU-057 · Persistencia de inventario y auditoría
**Como** jugador **quiero** que mis objetos se guarden siempre **para** no perder nunca mi equipo.
- Prioridad: Must · Estimación: M · Estado: Hecha
- Dependencias: HU-051, HU-026
- Skills: `inventory-items`, `dotnet-server`

**Criterios de aceptación**
1. **Dado** que salgo y entro **entonces** tengo exactamente los mismos items en las mismas casillas, equipo y oro.
2. **Dado** la migración **entonces** existen `character_items`, `character_hotbar` e `item_audit_log` con las restricciones de `docs/database.md`.
3. **Dado** loot, compra, venta, destrucción, split, merge o `admin_give` **entonces** se escribe una fila de auditoría (en lote con el guardado, no por operación en el tick).
4. **Dado** un `template_id` guardado que ya no existe **entonces** el personaje carga igual y se loguea `warn`.

**Notas de implementación**
- La auditoría pendiente (`Player.PendingAudit`: loot, buy, sell, destroy, split, merge, use, admin_give) viaja en lote en `CharacterSaveDto.Audit` con cada guardado y el repositorio la escribe (InMemory: `store.Audit`; EF: `item_audit_log`, sin compilar). Un `template_id` desconocido se ignora con aviso y el personaje carga igual. Round-trip comprobado en `InventoryFlowTests`; CA2 (migración) pendiente del PC con NuGet.

---
### HU-058 · Equipo inicial por clase
**Como** jugador nuevo **quiero** empezar con equipo básico de mi clase **para** poder pelear desde el principio.
- Prioridad: Must · Estimación: S · Estado: Hecha
- Dependencias: HU-052, HU-012
- Skills: `inventory-items`, `game-content`

**Criterios de aceptación**
1. **Dado** que creo un Guerrero **entonces** aparece con Espada gastada, Cota de recluta y Escudo de madera equipados y 5 Panes en la bolsa; un Sacerdote lleva Maza de iniciado equipada y una Varita de novicio en la bolsa (según `classes.json`).
2. **Dado** cada clase **entonces** un test verifica que su equipo inicial existe, cabe en la bolsa y tiene afinidad **alta** para ella (también lo verifica el validador de contenido).

**Notas de implementación**
- `CharacterFactory` ya equipaba los `startingItems` con `equip: true` y metía el resto en la bolsa; tests `StartingGear_*` (existe, cabe, afinidad alta por clase; Guerrero y Sacerdote según classes.json) y el validador comprueba la afinidad alta.

---
### HU-059 · Intercambio entre jugadores
**Como** jugador **quiero** intercambiar items y oro con un amigo **para** darle el botín que me cayó y a él le sirve.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-051, HU-057, HU-060
- Skills: `inventory-items`, `net-protocol`, `godot-client`

**Criterios de aceptación**
1. **Dado** otro jugador a ≤ 3 tiles **cuando** hago clic derecho → "Intercambiar" (o `/trade Nombre`) **entonces** recibe una solicitud (expira en 30 s); al aceptar se abre la ventana en ambos (`TradeUpdate`).
2. **Dado** la ventana **cuando** arrastro items de mi bolsa (hasta 6) o escribo oro **entonces** el otro ve mi oferta en vivo; cualquier cambio de oferta **desmarca** la confirmación de ambos.
3. **Dado** que ambos pulsan "Confirmar" con la misma oferta vista **entonces** el servidor valida (propiedad, cantidades, espacio en ambas bolsas, oro ≥ 0) y mueve todo en **una operación atómica**; si algo falla, nadie pierde nada y se muestra el motivo.
4. **Dado** que uno se aleja > 3 tiles, se desconecta, muere o cancela **entonces** el intercambio se cancela (`TradeUpdate{state:"cancelled"}`) y los items vuelven a estar disponibles.
5. **Dado** los tests de propiedad de HU-051 **entonces** incluyen intercambios aleatorios entre 2 personajes: la suma de `(templateId → qty)` y de oro de ambos se conserva y ningún `ItemInstance.Id` se duplica.
6. **Dado** cada intercambio completado **entonces** se escriben filas `trade_out`/`trade_in` en `item_audit_log` con el id de la contraparte.

**Notas técnicas**
- `TradeSession { A, B, offerA, offerB, confirmedA, confirmedB, version }`; toda `TradeConfirm` lleva `version` y se ignora si no coincide (evita confirmar una oferta cambiada en el mismo tick).
- Los items ofrecidos se marcan `locked` en la bolsa: no se pueden mover, usar, vender ni destruir mientras dura el intercambio.
