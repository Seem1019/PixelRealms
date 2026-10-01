# E7 · Operaciones y seguridad

### HU-070 · Comandos de administrador
**Como** administrador **quiero** comandos de juego **para** probar contenido y resolver problemas.
- Prioridad: Should · Estimación: M · Estado: Hecha
- Dependencias: HU-060, HU-051
- Skills: `dotnet-server`, `net-protocol`

**Criterios de aceptación**
1. **Dado** una cuenta con `is_admin` **entonces** puede usar en el chat: `/tp x y`, `/tpto Nombre`, `/spawn monsterId [n]`, `/give itemId [qty] [Nombre]`, `/level n`, `/heal`, `/kill` (objetivo), `/gold n`, `/god` (inmortal), `/debug move on|off`, `/announce texto`.
2. **Dado** una cuenta normal **entonces** cualquier comando admin devuelve `Error{forbidden}` y se loguea `warn`.
3. **Dado** cada comando **entonces** se registra en el log con el autor; `/give` y `/gold` en `item_audit_log` como `admin_give`.
4. **Dado** `dotnet run --project server/src/PixelRealms.Server -- make-admin <usuario>` **entonces** se marca la cuenta como admin.

**Notas de implementación**
- `AdminCommandHandler` (server/Net/Handlers): el cliente manda `AdminCommand{text}` cuando el chat recibe `/tp`, `/tpto`, `/spawn`, `/give`, `/level`, `/heal`, `/kill`, `/gold`, `/god`, `/debug move`, `/announce`; la respuesta vuelve como `ChatMessage{system}`. Coordenadas de `/tp` en casillas.
- El flag admin viaja en el ticket (`GameTicket.Admin` desde el claim del JWT) y queda en `WebSocketSession.IsAdmin`; con `dev:<id>` se consulta la cuenta. Cuentas normales → `Error{forbidden}` + `warn` con nombre y cuenta; cada comando se loguea en `info` con el autor.
- `/give` y `/gold` auditan `admin_give` (el oro con `item_id = 00000000-…` y `template_id = gold`, decisión provisional: la tabla no tiene columna de oro). `/god` y `/debug move` son flags de sesión en `Player` (no se persisten). `/spawn` crea monstruos sin punto de spawn (no reaparecen). `/level` baja también (quita hechizos y casillas de barra que ya no cumplen el nivel).
- `make-admin <usuario>` en `Program.cs` (construye el host, `IAccountRepository.SetAdminAsync`, sale). Tests: `AdminCommandTests` (3, WebSocket) y `AdminToolsTests` (3, dominio).

---
### HU-071 · Rate limiting y protección de mensajes
**Como** administrador **quiero** que el servidor se proteja de clientes abusivos **para** que un tramposo no arruine la partida.
- Prioridad: Must · Estimación: M · Estado: Hecha
- Dependencias: HU-033, HU-051
- Skills: `net-protocol`, `dotnet-server`

**Criterios de aceptación**
1. **Dado** los límites de `docs/architecture.md` §4 **entonces** un token bucket por conexión y tipo de mensaje los aplica.
2. **Dado** 3 excesos en 10 s **entonces** se desconecta con `Error{rate_limited}` y se loguea IP y cuenta.
3. **Dado** 10 conexiones WebSocket simultáneas desde la misma IP **entonces** la 11.ª se rechaza.
4. **Dado** un test de fuzzing (1 000 mensajes aleatorios/malformados) **entonces** el servidor no lanza excepciones no controladas y el tick sigue en < 50 ms.

**Notas de implementación**
- Token bucket por conexión y tipo en `Net/MessageRateLimiter.cs` (MoveInput 30/s, CastSpell 10/s, Chat 5 por 5 s, resto 20/s); límites configurables en `appsettings.json` → `Net:RateLimits` (son técnicos, no de balance, por eso no van en rules.json).
- Exceso → `Error{rate_limited}` y se descarta el mensaje; 3 excesos en 10 s (contados por conexión, no por tipo) → cierre `rate_limited` con `warn` que incluye IP y cuenta.
- Tope de 10 conexiones por IP en `ConnectionManager.TryReserveIp`; la 11.ª recibe HTTP 429 en `/ws`. Ojo detrás de Caddy: la IP vista es la del proxy salvo que se configure `ForwardedHeaders` (pendiente para HU-073).
- Tests: `RateLimitTests` (buckets, caducidad de excesos, cierre por flood, 11.ª conexión, fuzz de 1 000 mensajes malformados con reconexión; el servidor responde `/health` con p99 < 50 ms y sigue atendiendo Ping).

---
### HU-072 · Métricas y logs del servidor
**Como** administrador **quiero** ver el estado del servidor **para** detectar problemas de rendimiento.
- Prioridad: Should · Estimación: S · Estado: Pendiente
- Dependencias: HU-023
- Skills: `dotnet-server`

**Criterios de aceptación**
1. **Dado** `GET /health` **entonces** responde 200 con `{status, players, tickP99Ms, uptime}`.
2. **Dado** Serilog **entonces** en producción escribe JSON a consola con `CharacterName`, `AccountId`, `ConnId` como propiedades cuando aplique.
3. **Dado** `GET /admin/stats` (JWT admin) **entonces** muestra jugadores, monstruos, mensajes/s entrantes y salientes, bytes/s y, por instancia, tiempo de combate p99, áreas y auras activas y memoria asignada por segundo.

---

### HU-073 · Despliegue en VPS con TLS (wss)
**Como** anfitrión **quiero** subir el servidor a un VPS **para** que mis amigos jueguen desde sus casas.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-026, HU-072
- Skills: `dotnet-server`

**Criterios de aceptación**
1. **Dado** `Dockerfile` multi-stage (`sdk:10.0` → `aspnet:10.0`, usuario no root) **entonces** la imagen pesa < 150 MB.
2. **Dado** `docker-compose.prod.yml` con `server`, `postgres` y `caddy` **cuando** ejecuto `docker compose -f docker-compose.prod.yml up -d` en el VPS **entonces** el juego responde en `https://<dominio>` y `wss://<dominio>/ws` con certificado automático.
3. **Dado** `docs/deploy.md` **entonces** explica paso a paso: comprar VPS/dominio, DNS, firewall (solo 22, 80, 443), variables de entorno, primer despliegue, actualizar, ver logs.
4. **Dado** un GitHub Action manual (`workflow_dispatch`) **entonces** construye la imagen, la sube a GHCR y despliega por SSH.

---

### HU-074 · Build web y de escritorio del cliente
**Como** jugador **quiero** jugar desde el navegador o descargar el juego **para** entrar fácilmente.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-073
- Skills: `godot-client`

**Criterios de aceptación**
1. **Dado** el export Web **entonces** se sirve en `https://<dominio>/play` (Caddy) y funciona en Chrome y Firefox recientes conectando por `wss`.
2. **Dado** el export Windows **entonces** se publica un `.zip` en itch.io (página privada con contraseña) vía `butler` desde CI.
3. **Dado** una versión de cliente desactualizada **entonces** el servidor responde `bad_version` y el cliente muestra un enlace para actualizar.

---

### HU-075 · Backups automáticos
**Como** anfitrión **quiero** copias de seguridad diarias **para** no perder el progreso de mis amigos.
- Prioridad: Must · Estimación: S · Estado: Pendiente
- Dependencias: HU-073
- Skills: `dotnet-server`

**Criterios de aceptación**
1. **Dado** un contenedor/cron diario **entonces** ejecuta `pg_dump -Fc` y conserva los últimos 7.
2. **Dado** `docs/deploy.md` **entonces** documenta cómo restaurar un backup y se ha probado una restauración.

---

### HU-089 · Prueba de carga del combate ("Mina llena")
**Como** anfitrión **quiero** una prueba de carga del combate repetible **para** detectar lag, fugas de memoria y exceso de tráfico antes de que lo noten mis amigos.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-023, HU-088
- Skills: `dotnet-server`, `combat-system`

**Criterios de aceptación**
1. **Dado** `tools/LoadBot` en modo combate **entonces** reproduce el escenario: una instancia, 30 bots y 300 monstruos en 60×60 casillas, 40 áreas activas superpuestas, ~200 auras, un hechizo por GCD (la mitad de área), durante 5 min, y una prueba de resistencia de 30 min.
2. **Dado** el escenario **entonces** falla si: tick p99 > 15 ms o algún tick > 50 ms; combate p99 > 6 ms por instancia; memoria nueva > 2 MB/s o alguna recolección Gen2; memoria +10 % en 30 min; salida p95 > 40 KB/s por cliente.
3. **Dado** el cliente web con 24 marcas de área y 40 textos flotantes **entonces** falla si los FPS p5 bajan de 45 en el equipo de referencia.
4. **Dado** los microbenchmarks **entonces** 1 millón de pruebas de forma < 5 ms y una consulta de área con 100 candidatos < 20 µs.
5. **Dado** cada HU que toque áreas o auras y el cierre de M2 y M5 **entonces** se ejecuta y el resultado queda en el informe de la HU.

**Notas técnicas**
- ADR-018. Métricas desde `/admin/stats` (HU-072) y `dotnet-counters`.
