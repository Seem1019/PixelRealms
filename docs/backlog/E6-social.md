# E6 · Social

### HU-060 · Chat
**Como** jugador **quiero** chatear con otros **para** coordinarme con mis amigos.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-023
- Skills: `net-protocol`, `godot-client`, `dotnet-server`

**Criterios de aceptación**
1. **Dado** Enter **entonces** se enfoca la caja de chat; Enter envía, Esc cancela; mientras escribo, WASD no mueve al personaje.
2. **Dado** un mensaje sin prefijo **entonces** va a `say` (jugadores a ≤ 20 tiles) y aparece como burbuja sobre mi cabeza 4 s.
3. **Dado** `/g texto` **entonces** va a todos los conectados; `/w Nombre texto` solo al destinatario (error si no está conectado); `/p texto` a mi grupo.
4. **Dado** más de 5 mensajes en 5 s **entonces** `Error{rate_limited}` y el mensaje no se envía.
5. **Dado** un texto > 200 caracteres, vacío o con caracteres de control **entonces** se recorta/rechaza; el cliente escapa BBCode (`[`) para evitar inyección en `RichTextLabel`.
6. **Dado** colores por canal **entonces** say blanco, global naranja, grupo azul, susurro rosa, sistema amarillo.

---

### HU-061 · Grupos
**Como** jugador **quiero** formar un grupo con mis amigos **para** combatir juntos.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-060
- Skills: `net-protocol`, `dotnet-server`, `godot-client`

**Criterios de aceptación**
1. **Dado** `/invite Nombre` o clic derecho en un jugador → "Invitar" **entonces** el otro ve un diálogo Aceptar/Rechazar (expira en 60 s).
2. **Dado** que acepta **entonces** ambos reciben `PartyUpdate`; el que invitó es líder. Máximo 5 miembros.
3. **Dado** `/leave` **entonces** salgo; si era líder, lo hereda el siguiente; si queda 1, el grupo se disuelve.
4. **Dado** que el líder usa `/kick Nombre` **entonces** el miembro sale.
5. **Dado** un miembro que se desconecta **entonces** sigue en el grupo como "desconectado" 5 min; luego sale automáticamente.

---

### HU-062 · Marcos de grupo y XP/oro compartidos
**Como** miembro de un grupo **quiero** ver la vida de mis compañeros y compartir recompensas **para** jugar en equipo.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-061, HU-040, HU-050
- Skills: `godot-client`, `combat-system`, `inventory-items`

**Criterios de aceptación**
1. **Dado** un grupo **entonces** a la izquierda veo un marco por compañero con nombre, clase, nivel, vida y recurso (actualizado ≥ 2 veces/s aunque esté fuera de mi AOI) y puedo seleccionarlo con clic (para curarlo).
2. **Dado** que el grupo mata un monstruo **entonces** la XP ×1.2 se reparte a partes iguales entre miembros vivos a ≤ 40 tiles.
3. **Dado** el oro del botín **entonces** se reparte igual; el resto de la división va a quien lootea.
4. **Dado** el botín **entonces** cualquiera del grupo puede lootear el cadáver; los items uncommon+ se anuncian en el chat de grupo.
5. **Dado** F1–F5 **entonces** selecciono a mí mismo y a los miembros 1–4.

---

### HU-063 · Lista de jugadores en línea
**Como** jugador **quiero** ver quién está conectado **para** saber si mis amigos están jugando.
- Prioridad: Could · Estimación: S · Estado: Pendiente
- Dependencias: HU-060
- Skills: `net-protocol`, `godot-client`

**Criterios de aceptación**
1. **Dado** `/who` o la tecla O **entonces** veo la lista con nombre, clase, nivel y zona de cada jugador conectado.
2. **Dado** un nombre de la lista **cuando** hago clic derecho **entonces** puedo susurrar o invitar al grupo.
