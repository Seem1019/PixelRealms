# E7 · Operaciones y seguridad

### HU-070 · Comandos de administrador
**Como** administrador **quiero** comandos de juego **para** probar contenido y resolver problemas.
- Prioridad: Should · Estimación: M · Estado: Pendiente
- Dependencias: HU-060, HU-051
- Skills: `dotnet-server`, `net-protocol`

**Criterios de aceptación**
1. **Dado** una cuenta con `is_admin` **entonces** puede usar en el chat: `/tp x y`, `/tpto Nombre`, `/spawn monsterId [n]`, `/give itemId [qty] [Nombre]`, `/level n`, `/heal`, `/kill` (objetivo), `/gold n`, `/god` (inmortal), `/debug move on|off`, `/announce texto`.
2. **Dado** una cuenta normal **entonces** cualquier comando admin devuelve `Error{forbidden}` y se loguea `warn`.
3. **Dado** cada comando **entonces** se registra en el log con el autor; `/give` y `/gold` en `item_audit_log` como `admin_give`.
4. **Dado** `dotnet run --project server/src/PixelRealms.Server -- make-admin <usuario>` **entonces** se marca la cuenta como admin.

---

### HU-071 · Rate limiting y protección de mensajes
**Como** administrador **quiero** que el servidor se proteja de clientes abusivos **para** que un tramposo no arruine la partida.
- Prioridad: Must · Estimación: M · Estado: Pendiente
- Dependencias: HU-033, HU-051
- Skills: `net-protocol`, `dotnet-server`

**Criterios de aceptación**
1. **Dado** los límites de `docs/architecture.md` §4 **entonces** un token bucket por conexión y tipo de mensaje los aplica.
2. **Dado** 3 excesos en 10 s **entonces** se desconecta con `Error{rate_limited}` y se loguea IP y cuenta.
3. **Dado** 10 conexiones WebSocket simultáneas desde la misma IP **entonces** la 11.ª se rechaza.
4. **Dado** un test de fuzzing (1 000 mensajes aleatorios/malformados) **entonces** el servidor no lanza excepciones no controladas y el tick sigue en < 50 ms.

---

### HU-072 · Métricas y logs del servidor
**Como** administrador **quiero** ver el estado del servidor **para** detectar problemas de rendimiento.
- Prioridad: Should · Estimación: S · Estado: Pendiente
- Dependencias: HU-023
- Skills: `dotnet-server`

**Criterios de aceptación**
1. **Dado** `GET /health` **entonces** responde 200 con `{status, players, tickP99Ms, uptime}`.
2. **Dado** Serilog **entonces** en producción escribe JSON a consola con `CharacterName`, `AccountId`, `ConnId` como propiedades cuando aplique.
3. **Dado** `GET /admin/stats` (JWT admin) **entonces** muestra jugadores, monstruos, mensajes/s entrantes y salientes, bytes/s.

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
