# E1 · Cuentas y personajes

### HU-010 · Registro de cuenta
**Como** jugador nuevo **quiero** crear una cuenta con usuario y contraseña **para** guardar mis personajes.
- Prioridad: Must · Estimación: S · Estado: Hecha
- Dependencias: HU-002
- Skills: `dotnet-server`, `godot-client`

**Criterios de aceptación**
1. **Dado** un usuario libre **cuando** envío `POST /api/auth/register {"username":"ana","password":"segura123"}` **entonces** recibo 201 y la contraseña se guarda con hash (nunca en texto plano).
2. **Dado** un usuario existente (sin importar mayúsculas: `Ana` vs `ana`) **cuando** me registro **entonces** recibo 409 `username_taken`.
3. **Dado** un usuario < 3 o > 20 caracteres, con símbolos, o contraseña < 8 **entonces** recibo 400 con el campo inválido.
4. **Dado** la pantalla Login del cliente **cuando** pulso "Crear cuenta" y relleno el formulario **entonces** veo errores en español junto al campo y, si va bien, se inicia sesión automáticamente.

**Notas técnicas**
- `PasswordHasher<Account>` de ASP.NET Core Identity (sin el resto de Identity). Índice único `ix_accounts_username_ci` sobre `username`, que usa la collation ICU no determinista `case_insensitive` (ver `docs/database.md` → *Nombres únicos*).
- Rate limit: 5 registros/hora por IP.
- Cliente: `HTTPRequest` en `scripts/net/api_client.gd` (async con `await`), URL base desde `Settings`.

**Notas de implementación**
- Servidor: `POST /api/auth/register` (`Api/AuthEndpoints`, validación en `Api/Validation`: 3–20 `^[A-Za-z0-9_]+$`, contraseña ≥ 8; 409 `username_taken` sin distinguir mayúsculas; hash con `PasswordHasher` de Identity; 5 registros/hora por IP con `Microsoft.AspNetCore.RateLimiting`).
- Cliente: `scenes/login` con errores por campo en español (`scripts/net/api_messages.gd`) y login automático tras crear la cuenta; `scripts/net/api_client.gd` (HTTPRequest + await, autoload `Api`).
- Tests: 4 de API (201 + hash, 409, 400 por campo).

---
### HU-011 · Inicio de sesión
**Como** jugador **quiero** iniciar sesión **para** acceder a mis personajes.
- Prioridad: Must · Estimación: S · Estado: Hecha
- Dependencias: HU-010
- Skills: `dotnet-server`, `godot-client`

**Criterios de aceptación**
1. **Dado** credenciales válidas **cuando** envío `POST /api/auth/login` **entonces** recibo `{ token, expiresAt }` (JWT 15 min) y se actualiza `last_login_at`.
2. **Dado** credenciales inválidas **entonces** recibo 401 con el mismo mensaje genérico para usuario inexistente y contraseña errónea.
3. **Dado** 5 intentos fallidos en 1 min desde la misma IP **entonces** el 6.º recibe 429.
4. **Dado** el cliente **cuando** marco "Recordar usuario" **entonces** el usuario (no la contraseña) se guarda en `user://settings.cfg`.
5. **Dado** un token expirado **cuando** el cliente llama a la API **entonces** vuelve a la pantalla de login con el aviso "Tu sesión expiró".

**Notas de implementación**
- `POST /api/auth/login` → `{token, expiresAt}` (JWT HS256 de 15 min hecho con la BCL en `Auth/JwtService`, decisión provisional frente al paquete JwtBearer), 401 con el mismo mensaje para usuario inexistente y contraseña errónea, `last_login_at`, 5 intentos/min por IP → 429.
- Cliente: "Recordar usuario" guarda solo el usuario en `user://settings.cfg`; un 401 con token devuelve al login con "Tu sesión expiró".
- Tests: JWT emitido/validado/expirado/manipulado, mensaje genérico, 6.º intento 429.

---
### HU-012 · Crear personaje
**Como** jugador **quiero** crear un personaje eligiendo nombre y clase **para** empezar a jugar.
- Prioridad: Must · Estimación: M · Estado: Hecha
- Dependencias: HU-011, HU-003
- Skills: `dotnet-server`, `godot-client`, `game-content`

**Criterios de aceptación**
1. **Dado** la pantalla de selección **cuando** pulso "Nuevo" **entonces** veo las 4 clases (desde `Content`) con sprite, rol, recurso y descripción.
2. **Dado** un nombre válido (3–16, empieza por letra, solo letras/números) y único **cuando** confirmo **entonces** `POST /api/characters {name, classId}` devuelve 201 con nivel 1, en el cementerio por defecto del mapa `meadow`, vida/recurso llenos.
3. **Dado** un nombre repetido (case-insensitive) o de la lista de nombres reservados (`admin`, `gm`, `system`…) **entonces** 409/400 con mensaje claro.
4. **Dado** que ya tengo 4 personajes **entonces** el botón "Nuevo" está deshabilitado y la API responde 400 `max_characters`.
5. **Dado** un personaje recién creado **entonces** tiene el equipo inicial de su clase según `classes.json` (se completa en HU-058; aquí basta con guardar la clase).

**Notas de implementación**
- `POST /api/characters` con `CharacterFactory`: nivel 1 en el cementerio por defecto del mapa inicial, equipo inicial de `classes.json` equipado/en bolsa, vida y maná llenos (ira/energía a 0) vía `StatCalculator`, barra con el hechizo de nivel 1. Nombre 3–16 `^[A-Za-z][A-Za-z0-9]+$`, lista de reservados, 409 `name_taken`, 400 `max_characters` al 5.º.
- Cliente: panel "Nuevo" con las 4 clases desde `Content` (rol, recurso, descripción); botón deshabilitado con 4 personajes. Sin sprite todavía (placeholder).

---
### HU-013 · Listar y borrar personajes
**Como** jugador **quiero** ver mis personajes y borrar los que no quiero **para** gestionar mis ranuras.
- Prioridad: Must · Estimación: S · Estado: Hecha
- Dependencias: HU-012
- Skills: `dotnet-server`, `godot-client`

**Criterios de aceptación**
1. **Dado** `GET /api/characters` **entonces** recibo solo mis personajes no borrados con `id, name, classId, level, mapId`.
2. **Dado** un personaje **cuando** pulso "Borrar" **entonces** debo escribir su nombre para confirmar y se hace soft delete (`deleted_at`).
3. **Dado** un id de personaje de otra cuenta **cuando** intento borrarlo **entonces** recibo 404 (no 403, para no filtrar existencia).
4. **Dado** un personaje borrado **entonces** su nombre queda libre para otros tras 7 días (MVP: se libera inmediatamente añadiendo sufijo `#deleted-<id>`).

**Notas de implementación**
- `GET /api/characters` (solo no borrados, `id/name/classId/level/mapId`), `DELETE /api/characters/{id}` con soft delete y 404 para personajes ajenos; el nombre queda libre con sufijo `#deleted-<id>` (MVP).
- Cliente: borrar exige escribir el nombre exacto.

---
### HU-014 · Entrar al mundo (ticket + Hello/Welcome)
**Como** jugador **quiero** entrar al mundo con mi personaje **para** empezar a jugar.
- Prioridad: Must · Estimación: M · Estado: Hecha
- Dependencias: HU-006, HU-013
- Skills: `net-protocol`, `dotnet-server`, `godot-client`

**Criterios de aceptación**
1. **Dado** un personaje propio **cuando** pulso "Jugar" **entonces** el cliente pide `POST /api/game/ticket`, conecta a `/ws?ticket=...` y envía `Hello{protocolVersion, ticket}`.
2. **Dado** un `Hello` válido **entonces** recibo `Welcome` con `selfId`, estado del personaje, `tickRate`, mapa y hechizos conocidos, y el cliente carga la escena `World`.
3. **Dado** un ticket usado, expirado (> 30 s) o de otra cuenta **entonces** recibo `Error{bad_ticket}` y la conexión se cierra.
4. **Dado** un `protocolVersion` distinto **entonces** `Error{bad_version}` y el cliente muestra "Actualiza el juego".
5. **Dado** que mi cuenta ya tiene un personaje en el mundo **cuando** entro con otro (o el mismo) **entonces** la sesión anterior se desconecta y guarda primero.

**Notas técnicas**
- El ticket mapea a `(accountId, characterId)`; el handler de `Hello` encola `PlayerJoin` al tick; el tick carga el personaje
  **ya leído de BD** (la lectura de BD se hace fuera del tick antes de encolar).

**Notas de implementación**
- `POST /api/game/ticket` (`Auth/TicketService`: 32 bytes aleatorios, 30 s, un solo uso, solo personajes propios). `Hello` lo procesa `Players/HelloGate` en la tarea de la conexión (versión → `bad_version`, ticket → `bad_ticket`, lectura de BD fuera del tick) y encola `PlayerJoin` con el personaje cargado; `Players/WorldSession` lo mete en la instancia de su mapa en el tick y envía `Welcome` (`Players/PlayerMapper`); si la cuenta ya tenía un personaje dentro, se guarda y se desconecta (`replaced`). `Hosting/SaveService` guarda fuera del tick con 3 reintentos; al apagar se guardan todos.
- Flag de desarrollo: con `Net:RequireTicket=false` se acepta `ticket = "dev:<characterId>"`.
- Cliente: `scenes/world` conecta con el ticket, envía Hello, carga el mapa (render provisional por colores desde el .tmj) y coloca al jugador; errores `bad_version`/`bad_ticket` vuelven al login.
- Tests: 5 de integración (Welcome completo, ticket usado, versión, reemplazo con guardado, guardado al desconectar).
