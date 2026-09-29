# E5 · Items e inventario

> Todas las HUs de esta épica deben cumplir los invariantes de la skill `inventory-items`.

### HU-050 · Botín de monstruos
**Como** jugador **quiero** recoger el botín de los monstruos que mato **para** conseguir equipo y oro.
- Prioridad: Must · Estimación: L · Estado: Pendiente
- Dependencias: HU-037, HU-051
- Skills: `inventory-items`, `net-protocol`, `godot-client`

**Criterios de aceptación**
1. **Dado** un monstruo que maté (o mi grupo) **cuando** muere **entonces** se tira su tabla de botín y, si hay algo, el cadáver brilla para mí (y no para otros).
2. **Dado** que hago clic en el cadáver a ≤ 2 tiles **entonces** se abre la ventana de botín con oro e items (color de rareza).
3. **Dado** "Tomar todo" o clic en un item **entonces** pasa a mi bolsa; si no cabe, `Error{bag_full}` y queda en el cadáver.
4. **Dado** que pasan 30 s **entonces** cualquiera puede lootear; a los 60 s el cadáver desaparece con lo que quede.
5. **Dado** dos jugadores que envían `LootTake` del mismo item en el mismo tick **entonces** solo uno lo recibe (test).
6. **Dado** tests con `FixedRng` **entonces** cubren probabilidades, cantidades `min..max`, `maxItems` y oro.

---

### HU-051 · Inventario (bolsa de 24)
**Como** jugador **quiero** una bolsa donde guardar y ordenar mis objetos **para** gestionar lo que llevo.
- Prioridad: Must · Estimación: L · Estado: Pendiente
- Dependencias: HU-014
- Skills: `inventory-items`, `godot-client`, `net-protocol`

**Criterios de aceptación**
1. **Dado** la tecla I **entonces** se abre la bolsa de 6×4 casillas con íconos, cantidades y borde de rareza, y el oro abajo (`12o 34p 56c`).
2. **Dado** que arrastro un item a otra casilla **entonces** se envía `InventoryMove` y tras `InventoryUpdate` queda movido (vacía), fusionado (mismo apilable) o intercambiado.
3. **Dado** un `InventoryMove` con índices fuera de rango, item ajeno o `qty` inválida **entonces** `Error{invalid_payload|not_found}` y nada cambia.
4. **Dado** tests de propiedad (1 000 operaciones aleatorias con `SeededRng`) **entonces** se mantienen los invariantes 1–6 de la skill.

---

### HU-052 · Equipar y desequipar
**Como** jugador **quiero** equiparme armas y armaduras **para** mejorar mis estadísticas.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-051
- Skills: `inventory-items`, `combat-system`

**Criterios de aceptación**
1. **Dado** un item equipable **cuando** hago clic derecho o lo arrastro a su slot **entonces** se equipa (intercambiando con lo que hubiera) y mis stats se recalculan (`StatsUpdate`).
2. **Dado** un item de nivel superior, de otra clase o de tipo no permitido (Mago + placas) **entonces** `Error{level_too_low|wrong_class|cannot_equip}`.
3. **Dado** que desequipo con la bolsa llena **entonces** `Error{bag_full}`.
4. **Dado** que cambio de arma **entonces** el auto-ataque usa el daño y velocidad nuevos desde el siguiente swing.
5. **Dado** que me quito un item con +aguante **entonces** mi vida actual no supera la nueva máxima.
6. **Dado** otros jugadores **entonces** ven el cambio de arma si el sprite lo soporta (post-MVP: solo arma principal).

---

### HU-053 · Tooltips y comparación
**Como** jugador **quiero** ver la información de un item y compararlo con lo que llevo **para** decidir si me conviene.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-052
- Skills: `inventory-items`, `godot-client`

**Criterios de aceptación**
1. **Dado** que paso el ratón sobre un item (bolsa, equipo, botín, vendedor) **entonces** veo el tooltip con todos los campos de la skill `inventory-items` §Cliente.
2. **Dado** un equipable **entonces** se muestran las diferencias con el item equipado en ese slot (▲ verde / ▼ rojo) incluido DPS del arma.
3. **Dado** un requisito no cumplido **entonces** aparece en rojo.
4. **Dado** `tooltip_builder.gd` **entonces** tiene tests GUT con 3 items de ejemplo (arma, armadura, consumible).

---

### HU-054 · Usar consumibles
**Como** jugador **quiero** usar pociones y comida **para** recuperarme durante y después del combate.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-051, HU-035
- Skills: `inventory-items`, `combat-system`

**Criterios de aceptación**
1. **Dado** una Poción menor de vida **cuando** la uso **entonces** recupero 60 de vida, se consume 1 y entra en CD de 60 s compartido por todas las pociones de ese tipo.
2. **Dado** el Pan **entonces** aplica una cura de 50 en 15 s.
3. **Dado** que uso una poción en CD **entonces** `Error{on_cooldown}` y no se consume.
4. **Dado** el último item del stack **entonces** la casilla queda vacía.

---

### HU-055 · Oro y vendedor NPC
**Como** jugador **quiero** vender basura y comprar pociones **para** aprovechar el oro que consigo.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-051, HU-050
- Skills: `inventory-items`, `world-maps`, `net-protocol`

**Criterios de aceptación**
1. **Dado** el NPC Marta en el pueblo **cuando** hago clic a ≤ 3 tiles **entonces** se abre su ventana con los items de `vendors.json` y precios.
2. **Dado** oro suficiente y espacio **cuando** compro 5 pociones **entonces** se descuenta el oro exacto y aparecen en la bolsa (fusionando stacks).
3. **Dado** la ventana abierta **cuando** hago clic derecho en un item de mi bolsa **entonces** lo vendo por `sellPrice × qty`; los items con `sellPrice: 0` no se pueden vender.
4. **Dado** un botón "Vender basura" **entonces** vende todos los items de rareza `junk` de una vez.
5. **Dado** que me alejo > 3 tiles **entonces** la ventana se cierra y el servidor rechaza operaciones.

---

### HU-056 · Dividir, fusionar y destruir stacks
**Como** jugador **quiero** manipular pilas de objetos **para** organizar mi bolsa.
- Prioridad: Should · Estimación: S · Estado: Pendiente
- Dependencias: HU-051
- Skills: `inventory-items`, `godot-client`

**Criterios de aceptación**
1. **Dado** Shift+clic en un stack **entonces** aparece un diálogo de cantidad; al soltar en casilla vacía se crea un stack nuevo con un id nuevo.
2. **Dado** arrastrar un item fuera de la ventana **entonces** pide confirmación "¿Destruir X?" y al aceptar se elimina (registro en auditoría).
3. **Dado** `qty` 0, negativa o mayor que el stack **entonces** el servidor la rechaza.

---

### HU-057 · Persistencia de inventario y auditoría
**Como** jugador **quiero** que mis objetos se guarden siempre **para** no perder nunca mi equipo.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-051, HU-026
- Skills: `inventory-items`, `dotnet-server`

**Criterios de aceptación**
1. **Dado** que salgo y entro **entonces** tengo exactamente los mismos items en las mismas casillas, equipo y oro.
2. **Dado** la migración **entonces** existen `character_items`, `character_hotbar` e `item_audit_log` con las restricciones de `docs/database.md`.
3. **Dado** loot, compra, venta, destrucción, split, merge o `admin_give` **entonces** se escribe una fila de auditoría (en lote con el guardado, no por operación en el tick).
4. **Dado** un `template_id` guardado que ya no existe **entonces** el personaje carga igual y se loguea `warn`.

---

### HU-058 · Equipo inicial por clase
**Como** jugador nuevo **quiero** empezar con equipo básico de mi clase **para** poder pelear desde el principio.
- Prioridad: Must · Estimación: S · Estado: Pendiente
- Dependencias: HU-052, HU-012
- Skills: `inventory-items`, `game-content`

**Criterios de aceptación**
1. **Dado** que creo un Guerrero **entonces** aparece con Espada gastada, Cota de recluta y Escudo de madera equipados y 5 Panes en la bolsa (según `classes.json`).
2. **Dado** cada clase **entonces** un test verifica que su equipo inicial es equipable por ella (también lo verifica el validador de contenido).
