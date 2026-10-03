# E6 · Social

### HU-060 · Chat
**Como** jugador **quiero** chatear con otros **para** coordinarme con mis amigos.
- Prioridad: Must · Estimación: M · Estado: Hecha
- Dependencias: HU-023
- Skills: `net-protocol`, `godot-client`, `dotnet-server`

**Criterios de aceptación**
1. **Dado** Enter **entonces** se enfoca la caja de chat; Enter envía, Esc cancela; mientras escribo, WASD no mueve al personaje.
2. **Dado** un mensaje sin prefijo **entonces** va a `say` (jugadores a ≤ 20 tiles) y aparece como burbuja sobre mi cabeza 4 s.
3. **Dado** `/g texto` **entonces** va a todos los conectados; `/w Nombre texto` solo al destinatario (error si no está conectado); `/p texto` a mi grupo.
4. **Dado** más de 5 mensajes en 5 s **entonces** `Error{rate_limited}` y el mensaje no se envía.
5. **Dado** un texto > 200 caracteres, vacío o con caracteres de control **entonces** se recorta/rechaza; el cliente escapa BBCode (`[`) para evitar inyección en `RichTextLabel`.
6. **Dado** colores por canal **entonces** say blanco, global naranja, grupo azul, susurro rosa, sistema amarillo.

**Notas de implementación**
- `Social/ChatService`: say (≤ `rules.movement.sayRangeTiles`), global, party, whisper (error si no está conectado), 1–200 caracteres sin control, 5 mensajes / 5 s (solo cuentan los aceptados). Cliente `chat_panel.gd`: Enter enfoca/envía, Esc cancela, WASD bloqueado al escribir, `/g`, `/w Nombre`, `/p`, colores por canal (say blanco, global naranja, grupo azul, susurro rosa, sistema amarillo), BBCode escapado, burbuja 4 s.
- Tests: `SocialTests.Chat_*`, `SocialFlowTests.Chat_*`.

---
### HU-061 · Grupos
**Como** jugador **quiero** formar un grupo con mis amigos **para** combatir juntos.
- Prioridad: Must · Estimación: M · Estado: Hecha
- Dependencias: HU-060
- Skills: `net-protocol`, `dotnet-server`, `godot-client`

**Criterios de aceptación**
1. **Dado** `/invite Nombre` o clic derecho en un jugador → "Invitar" **entonces** el otro ve un diálogo Aceptar/Rechazar (expira en 60 s).
2. **Dado** que acepta **entonces** ambos reciben `PartyUpdate`; el que invitó es líder. Máximo 5 miembros.
3. **Dado** `/leave` **entonces** salgo; si era líder, lo hereda el siguiente; si queda 1, el grupo se disuelve.
4. **Dado** que el líder usa `/kick Nombre` **entonces** el miembro sale.
5. **Dado** un miembro que se desconecta **entonces** sigue en el grupo como "desconectado" 5 min; luego sale automáticamente.

**Notas de implementación**
- `Social/PartyService`: `/invite` o clic derecho → Invitar (la invitación viaja como `PartyUpdate{leader, members: []}`, caduca `inviteExpireSec`), aceptar → grupo con el que invitó como líder (máx. `maxMembers`), `/leave` (hereda el siguiente; con 1 se disuelve), `/kick` solo el líder, desconectados "(desc.)" durante `offlineGraceSec` y luego fuera.
- Tests: `SocialTests.Party_*`, `SocialFlowTests.Party_*`. Decisión provisional: la invitación reutiliza `PartyUpdate` con lista vacía (no hay mensaje de invitación en el protocolo).

---
### HU-062 · Marcos de grupo y XP/oro compartidos
**Como** miembro de un grupo **quiero** ver la vida de mis compañeros y compartir recompensas **para** jugar en equipo.
- Prioridad: Must · Estimación: M · Estado: Hecha
- Dependencias: HU-061, HU-040, HU-050
- Skills: `godot-client`, `combat-system`, `inventory-items`

**Criterios de aceptación**
1. **Dado** un grupo **entonces** a la izquierda veo un marco por compañero con nombre, clase, nivel, vida y recurso (actualizado ≥ 2 veces/s aunque esté fuera de mi AOI) y puedo seleccionarlo con clic (para curarlo).
2. **Dado** que el grupo mata un monstruo **entonces** la XP se reparte con la fórmula de grupo del GDD (`rules.group`): miembros *activos* (vivos, a ≤ 40 tiles, con acción en los últimos 90 s), referencia = nivel más alto, pesos por brecha `0.75^max(0, brecha−2)` con mínimo 0.10, pool × `bonusBySize[N]`.
2b. **Dado** niveles 10/8/5 y un monstruo normal nv 9 **entonces** reciben 26.5 / 26.5 / 11.2 XP (redondeo al entero más cercano en el mensaje `XpGain`); un miembro muerto o a 41 tiles no cuenta ni cobra (tests exactos).
3. **Dado** el oro del botín **entonces** se reparte igual entre los elegibles; el resto de la división va a quien lootea.
4. **Dado** el botín **entonces** cualquiera del grupo puede abrir el cadáver y ver qué cayó y para quién; solo el dueño toma cada item (HU-050). Los items uncommon+ se anuncian en el chat de grupo con el nombre del ganador.
5. **Dado** F1–F5 **entonces** selecciono a mí mismo y a los miembros 1–4.

**Notas de implementación**
- Marcos de grupo (`social_panels.gd`) con nombre, clase, nivel, vida y estado, actualizados con `PartyUpdate` cada 500 ms (`EventDispatcher.PartyFrameEveryTicks`) aunque estén fuera de la AOI; clic o F1–F5 seleccionan. XP de grupo con la fórmula del GDD (`GroupXp.Split`: activos vivos a ≤ `xpRangeTiles` con acción en `activeWindowSec`, referencia nivel máximo, pesos `0.75^max(0, brecha−2)` mín. 0.10, `bonusBySize`); oro y botín para los miembros elegibles del grupo (`LootSystem.EligibleFor`).
- Tests: `GroupXp_Example_10_8_5_vs_Normal9` (26.5 / 26.5 / 11.2), `GroupXp_InWorld_DeadOrFarMembersExcluded`. CA4: `LootAnnouncedEvent` al crear la bolsa → `ChatMessage{party}` a los miembros conectados del grupo del ganador (`EventDispatcher`); si el monstruo es jefe va al `global` (HU-083 CA4). Test `UncommonPlus_EmitsLootAnnounced_GlobalForBoss_PartyOtherwise`.
- 2026-10-02 (rama `fix/phase1-audit-blockers`): CA1: `PartyUpdate.members[].resPct` y el marco muestra el recurso (`test_party_frames.gd`). CA3: el resto de la división (`GoldRemainder`) es para el primer elegible que lootea, no para `eligible[0]`.

---
### HU-063 · Lista de jugadores en línea
**Como** jugador **quiero** ver quién está conectado **para** saber si mis amigos están jugando.
- Prioridad: Should · Estimación: S · Estado: Hecha
- Dependencias: HU-060
- Skills: `net-protocol`, `godot-client`

**Criterios de aceptación**
1. **Dado** `/who` o la tecla O **entonces** veo la lista con nombre, clase, nivel y zona de cada jugador conectado.
2. **Dado** un nombre de la lista **cuando** hago clic derecho **entonces** puedo susurrar o invitar al grupo.

**Notas de implementación**
- `/who` (`ChatSend{channel: "who"}`) → mensaje de sistema con nombre, clase, nivel y zona de cada conectado. CA2 (clic derecho en la lista) y la tecla O quedaron pendientes (la lista llegaba como texto en el chat). *(cerrado el 2026-10-02: ver la última nota)*
- 2026-10-02 (rama `fix/phase1-audit-blockers`): Tecla O (`toggle_online_list`) → `OnlineListRequest` → ventana con nombre, clase, nivel y zona; clic o clic derecho sobre otro jugador → Susurrar / Invitar al grupo (`test_online_list.gd`). `/who` sigue funcionando en el chat.

---
### HU-064 · Duelos (PvP amistoso)
**Como** jugador **quiero** retar a un amigo a un duelo **para** medirnos sin perder nada.
- Prioridad: Must · Estimación: L · Estado: Parcial
- Dependencias: HU-033, HU-035, HU-037, HU-060
- Skills: `combat-system`, `net-protocol`, `godot-client`

**Criterios de aceptación**
1. **Dado** otro jugador visible **cuando** hago clic derecho → "Retar a duelo" o `/duel Nombre` **entonces** recibe `DuelUpdate{state:"requested"}` con Aceptar/Rechazar (expira en `rules.pvp.rulesets.duel.requestExpireSec`).
2. **Dado** que acepta **entonces** ambos ven una cuenta atrás de `countdownSec` y luego pueden atacarse: los hechizos con `targeting: enemy` aceptan al rival, los `ally` no; el daño aplica `rules.classAdvantage[atacante][defensor]`.
3. **Dado** que un duelista baja a `endAtHpPct` (1 %) de vida **entonces** no muere: el duelo termina (`DuelUpdate{state:"ended", winner}`), ambos recuperan vida y recurso al 100 %, se limpian sus auras y se anuncia en `say`.
4. **Dado** que un duelista se aleja > `maxDistanceTiles`, se desconecta, usa un portal o escribe `/rendirse` **entonces** pierde el duelo.
5. **Dado** el duelo **entonces** no se pierde XP, oro, items ni durabilidad; los monstruos ignoran a los duelistas y estos no pueden atacar monstruos ni a terceros mientras dure.
6. **Dado** la aldea (`safe=true`) **entonces** los duelos están permitidos (`allowedInSafeZones`); cualquier otro daño entre jugadores sigue prohibido (`Error{pvp_not_allowed}`).
7. **Dado** `PvpService.CanAttack(a, b)` **entonces** devuelve el ruleset aplicable o `null`; tests: sin duelo → null; en duelo → `duel`; con `enabledRulesets: []` → siempre null.
8. **Dado** un duelo **entonces** se aplican las mismas reglas de acumulación e inmunidad tras control que en PvE (ADR-022).

**Notas técnicas**
- ADR-011. `PvpService` es la única puerta: `EffectResolver` le pregunta antes de aplicar daño/auras a un jugador. El pipeline de combate no cambia.
- `DuelSession { A, B, state, startedAt }` vive en la `MapInstance`; termina también si alguno cambia de mapa.
- Cliente: marco del rival en naranja durante el duelo; resultado en pantalla 3 s.

**Notas de implementación**
- `Social/PvpService` (única puerta del PvP): `/duel` o clic derecho → `DuelUpdate{requested}` (caduca `requestExpireSec`), aceptar → `countdown` (`countdownSec`) → `active`; `CanAttack(a, b)` devuelve el ruleset o null (sin duelo, otro rival, `enabledRulesets` vacío); daño con `rules.classAdvantage`; al llegar a `endAtHpPct` el daño se recorta, nadie muere, ambos se restauran, se limpian auras y se anuncia en `say`; pierde quien se rinde, se aleja > `maxDistanceTiles`, se desconecta o cambia de mapa; los monstruos ignoran a los duelistas y estos no atacan a monstruos ni terceros; los `ally` no aceptan al rival; mismas reglas de auras (ADR-022). Cliente: diálogo, cuenta atrás, rival en naranja, resultado 3 s.
- Tests: `SocialTests.Duel_*`, `SocialFlowTests.Duel_*`.
- 2026-10-02 (rama `fix/phase1-audit-blockers`): Exploit cerrado: no se puede retar ni aceptar en combate, rendirse antes de empezar solo cancela y al terminar cada uno vuelve a la vida y el recurso del inicio del duelo (no al máximo) y pierde las auras del rival. CA2: `classAdvantage` también en el básico y los DoT. CA6: `pvp_not_allowed`. **Falta una decisión:** CA3 dice "recuperan vida y recurso completos"; si se acepta la restauración al estado del inicio, cambiar ese texto y marcarla Hecha.

### HU-097 · Historial del chat
**Como** jugador **quiero** subir en el chat **para** leer mensajes que ya pasaron.
- Prioridad: Should · Estimación: S · Estado: Hecha
- Dependencias: HU-060
- Skills: `godot-client`

**Criterios de aceptación**
1. **Dado** el registro del chat **cuando** giro la rueda sobre él o pulso Re Pág / Av Pág **entonces** se desplaza por los mensajes anteriores (se guardan 200 líneas).
2. **Dado** que estoy leyendo arriba **cuando** llega un mensaje **entonces** no salta al final y aparece el aviso "↓ nuevos"; al volver al final sigue al último mensaje.
3. **Dado** el ratón sobre el chat o que estoy desplazado arriba **entonces** el chat no se desvanece.

**Notas de implementación** (escritas en la auditoría del 2026-10-02, a partir de `7c279c1`)
- `scripts/ui/chat_panel.gd`: guarda 200 líneas; rueda y Re Pág / Av Pág desplazan; leyendo arriba no salta al final y muestra
  "↓ nuevos". El desvanecido por inactividad (8 s, `f88c107`, sin HU propia) se suspende con el ratón encima o leyendo arriba.
- Tests: `test_chat_history.gd` (200 líneas, desplazar sin saltar, seguir al último, no desvanecer leyendo); la rueda y el
  hover no tienen test.
