---
name: hu-implementation
description: Flujo completo para implementar una historia de usuario (HU-XXX) de docs/backlog de principio a fin — plan, tests desde criterios de aceptación, servidor, protocolo, cliente, verificación y cierre. Úsala siempre que el usuario pida implementar, continuar o cerrar una HU.
---

# Implementar una historia de usuario

## 0. Localiza la HU
- Busca el ID en `docs/backlog/*.md` (`grep -rn "HU-XXX" docs/backlog`). Lee la HU completa, sus **dependencias**
  y la sección "Notas técnicas". Si una dependencia no está `Hecha`, detente y avisa.
- Lee las skills listadas en la HU (campo **Skills**) antes de tocar código.

## 1. Plan (modo plan, sin editar)
Entrega un plan con:
1. Archivos a crear/modificar (rutas exactas), agrupados por proyecto (`Protocol`, `Game`, `Server`, `client/`…).
2. Mensajes de protocolo nuevos/cambiados (con JSON de ejemplo) → si hay, seguir skill `net-protocol`.
3. Cambios de BD → migración EF (nombre).
4. Lista de tests: uno o más por cada criterio Dado/Cuando/Entonces, más casos de abuso (cliente tramposo).
5. Riesgos / preguntas abiertas. **Pregunta** solo si una decisión cambia el diseño; si no, elige lo más simple y dilo.
Espera aprobación del usuario.

## 2. Tests primero
- Dominio (`PixelRealms.Game.Tests`): tests puros con `FakeClock`, `SeededRng`, `WorldBuilder` (helpers de test).
  Nombra: `MetodoOSistema_Escenario_ResultadoEsperado`.
- Protocolo: test de serialización ida y vuelta por mensaje nuevo.
- Servidor integración (`PixelRealms.Server.Tests`): `TestServer` (servidor real en `127.0.0.1:0`) + cliente WebSocket de
  prueba (`TestGameClient`) cuando la HU cruza red.
- Cliente: tests GUT en `client/tests/` para lógica pura (parsers, predicción, formateo, inventario UI-model).
- Ejecuta y comprueba que **fallan** por la razón correcta.

## 3. Implementa en este orden
`Content` (si hay campos nuevos + schema) → `Protocol` → `Game` → `Server` (handlers/router/snapshots) →
`Persistence` → `client/` (net.gd → game_state.gd → escenas/UI). Commits pequeños por capa si es grande.

## 4. Verificación (obligatoria)
```bash
dotnet build server/PixelRealms.sln -warnaserror
dotnet test  server/PixelRealms.sln
dotnet run --project server/tools/ContentValidator -- content/
godot --path client --headless -s ../tools/sync_content.gd
godot --path client --headless -s addons/gut/gut_cmdln.gd -gdir=res://tests -gexit
```
- Si tocaste `server/`: lanza el subagente **server-authority-reviewer** sobre el diff y corrige sus hallazgos.
- Prueba manual si la HU es visible: arranca servidor + 2 clientes (`godot --path client` ×2) y recorre los
  criterios. Describe al usuario qué verificaste y qué no pudiste verificar.

## 5. Cierre (Definition of Done)
- [ ] Todos los criterios de aceptación cubiertos por tests o verificación manual documentada.
- [ ] Sin warnings nuevos; sin `TODO` sin HU asociada.
- [ ] `docs/protocol.md` / `docs/database.md` / `docs/design/*` actualizados si cambió algo.
- [ ] En el archivo de la HU: `Estado: Hecha` + sección "Notas de implementación" (3–6 líneas: decisiones, archivos clave).
- [ ] Fila actualizada en `docs/backlog/README.md`.
- [ ] Commit(s) Conventional Commits que referencian la HU: `feat(inventory): equip items (HU-052)`.

## Anti-patrones que debes evitar
- Calcular daño, botín o posición final en el cliente.
- Tocar `World` desde un handler async o desde `SaveService`.
- Meter valores de balance en código (van en `content/`).
- Mezclar 2 HUs en un commit. Refactors grandes no pedidos.
