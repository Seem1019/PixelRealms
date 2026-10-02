# Despliegue en un VPS (HU-073, HU-074, HU-075)

Objetivo: el juego responde en `https://<dominio>` (cliente web en `/play`) y `wss://<dominio>/ws` con certificado automático,
copias de seguridad diarias y despliegue desde GitHub con un clic. Todo lo de esta guía está en el repo:
`server/Dockerfile`, `docker-compose.prod.yml`, `deploy/Caddyfile`, `deploy/backup.sh`, `deploy/restore.sh`,
`.github/workflows/deploy.yml` y `.github/workflows/release-client.yml`.

> Estado: escrito sin poder probarlo en un VPS real (no había Docker ni red en la sesión que lo generó). La primera vez,
> sigue los pasos en orden y anota aquí lo que haya que corregir.
>
> Primer despliegue real (2026-10-02, Vultr Miami, Ubuntu 24.04, opción B): funcionó tras dos arreglos en
> `server/Dockerfile`: copiar el `.editorconfig` de la raíz (sin él, CA1716/CA1720 rompen el `dotnet publish`) y usar
> `Urls` en vez de `ASPNETCORE_URLS` (el `"Urls"` de `appsettings.json` pisa la variable con prefijo y el servidor solo
> escuchaba en `localhost` dentro del contenedor). Con opción B pon `SERVER_IMAGE=pixelrealms-server:local` en `.env`.
> El cliente se exportó en local (plantillas 4.7.2) y se subió a `deploy/play` con `scp`; los workflows siguen sin probar.

## 1. Comprar VPS y dominio
- VPS Linux (Ubuntu 24.04 LTS o Debian 12), 2 vCPU y 2–4 GB de RAM bastan para ~20 jugadores (el servidor usa < 1 GB; Postgres
  y Caddy son ligeros). Proveedores habituales: Hetzner, DigitalOcean, OVH, Contabo.
- Un dominio (o subdominio) para el juego, p. ej. `juego.midominio.com`.

## 2. DNS
- Registro `A` → IP pública del VPS (y `AAAA` si tiene IPv6). TTL corto (300 s) mientras pruebas.
- Comprueba con `dig +short juego.midominio.com` antes de arrancar Caddy: si el DNS no apunta aún, Let's Encrypt fallará y
  Caddy reintentará con esperas crecientes.

## 3. Preparar el VPS (una sola vez)
```bash
# como root
apt update && apt upgrade -y
apt install -y ufw curl git
ufw default deny incoming && ufw default allow outgoing
ufw allow 22/tcp && ufw allow 80/tcp && ufw allow 443/tcp && ufw allow 443/udp   # solo 22, 80 y 443 (HU-073 CA3)
ufw enable
# Docker + compose
curl -fsSL https://get.docker.com | sh
adduser --disabled-password pixelrealms && usermod -aG docker pixelrealms
# clave SSH para el usuario (la misma que pondrás en el secret VPS_SSH_KEY del repo)
mkdir -p /home/pixelrealms/.ssh && cat tu_clave_publica.pub >> /home/pixelrealms/.ssh/authorized_keys
chown -R pixelrealms:pixelrealms /home/pixelrealms/.ssh && chmod 700 /home/pixelrealms/.ssh && chmod 600 /home/pixelrealms/.ssh/authorized_keys
```
Recomendado: desactiva la entrada por contraseña (`PasswordAuthentication no` en `/etc/ssh/sshd_config`) y, si quieres,
`apt install fail2ban`.

## 4. Variables de entorno
En el VPS, como `pixelrealms`:
```bash
mkdir -p ~/pixelrealms/deploy/play ~/pixelrealms/backups && cd ~/pixelrealms
# copia docker-compose.prod.yml, deploy/Caddyfile, deploy/backup.sh y deploy/restore.sh del repo (el Action lo hace solo)
cp .env.example .env   # o créalo a mano con estas claves:
```
| Variable | Valor |
|---|---|
| `DOMAIN` | `juego.midominio.com` |
| `POSTGRES_USER` / `POSTGRES_DB` | `pixelrealms` |
| `POSTGRES_PASSWORD` | contraseña larga aleatoria (`openssl rand -base64 24`) |
| `JWT_SIGNING_KEY` | ≥ 32 bytes aleatorios (`openssl rand -base64 48`) |
| `SERVER_IMAGE` | `ghcr.io/<usuario>/pixelrealms-server:latest` (lo actualiza el Action en cada despliegue) |

`.env` nunca va al repo (regla 8 de `CLAUDE.md`). Si cambias `JWT_SIGNING_KEY`, todas las sesiones abiertas caducan.

## 5. Primer despliegue
Opción A, desde GitHub (recomendada): en el repo → *Settings → Secrets and variables → Actions* crea `VPS_HOST`, `VPS_USER`
(`pixelrealms`), `VPS_SSH_KEY` (clave privada) y, si no usas `~/pixelrealms`, `VPS_PATH`. Crea el *environment* `production`.
Luego *Actions → Deploy servidor → Run workflow*. El job construye la imagen (falla si pesa > 150 MB), la sube a GHCR y en el
VPS hace `docker compose pull` + `up -d` y comprueba `/health`. La primera vez la imagen de GHCR es privada: en el VPS
el Action ya hace `docker login ghcr.io` con el token del workflow; si lo lanzas a mano necesitas un PAT con `read:packages`.

Opción B, construyendo en el VPS:
```bash
git clone https://github.com/<usuario>/pixelrealms.git src && cd src
docker compose -f docker-compose.prod.yml build server
docker compose -f docker-compose.prod.yml up -d
```
Comprobar:
```bash
docker compose -f docker-compose.prod.yml ps
curl -s https://juego.midominio.com/health      # {"status":"ok","players":0,"tickP99Ms":...,"uptime":...}
```
Las migraciones de EF Core se aplican solas al arrancar el servidor (`MigrateAsync`), así que la base queda lista en el primer
arranque.

**El despliegue parte de una base de datos vacía.** La primera migración (`InitialCreate`) crea todo el esquema, incluida
la collation `case_insensitive`. Si el volumen `pgdata` ya tiene tablas creadas sin migraciones (sin
`__EFMigrationsHistory`), el servidor no arranca: falla con `relation "accounts" already exists`. Hay dos salidas:

- **Recrear la base** (si sus datos no importan; borra todo lo que hay en ella):
  ```bash
  docker compose -f docker-compose.prod.yml stop server
  docker compose -f docker-compose.prod.yml exec postgres sh -c 'dropdb -U "$POSTGRES_USER" "$POSTGRES_DB" && createdb -U "$POSTGRES_USER" "$POSTGRES_DB"'
  docker compose -f docker-compose.prod.yml start server
  ```
- **Baseline** (conservar los datos): solo si el esquema existente ya es **idéntico** al de `InitialCreate`, collation e
  índices `ix_*_ci` incluidos (compáralo con `dotnet ef migrations script 0 InitialCreate`). Haz una copia antes (§9).
  Comprueba que no hay nombres repetidos sin distinguir mayúsculas, porque el índice único los rechazaría:
  ```sql
  SELECT lower(username), count(*) FROM accounts GROUP BY 1 HAVING count(*) > 1;
  SELECT lower(name), count(*) FROM characters GROUP BY 1 HAVING count(*) > 1;
  ```
  y marca la migración como aplicada para que el servidor no intente crearla otra vez:
  ```sql
  CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
      migration_id varchar(150) PRIMARY KEY,
      product_version varchar(32) NOT NULL);
  INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
  VALUES ('20261001173627_InitialCreate', '10.0.1');
  ```
  Si el esquema no coincide, el baseline dejaría la base a medias: exporta los datos, recrea la base y vuelve a importarlos. Para el primer administrador: `docker compose -f docker-compose.prod.yml exec server dotnet PixelRealms.Server.dll make-admin <usuario>`
(la cuenta debe existir: regístrala desde el cliente antes).

## 6. Cliente web y de escritorio (HU-074)
*Actions → Publicar cliente → Run workflow* con la URL pública (`https://juego.midominio.com`) y la versión. El workflow:
1. copia `content/` y `maps/` al cliente (`tools/sync_content.gd`), fija `DEFAULT_SERVER_URL` y exporta **Web** y **Windows**
   con los presets de `client/export_presets.cfg`;
2. sube el export Web a `~/pixelrealms/deploy/play` del VPS: Caddy lo sirve en `https://<dominio>/play/` con las cabeceras
   COOP/COEP que exige Godot Web (hilos) — Chrome y Firefox recientes;
3. sube el `.zip` de Windows a itch.io con `butler` (secrets `BUTLER_API_KEY` e `ITCH_TARGET` = `usuario/juego:windows`; en
   itch.io marca la página como *Restricted* con contraseña).
Si el cliente es de otra versión de protocolo, el servidor responde `bad_version` y el cliente muestra el aviso con un enlace
a `/play/` (configurable en `user://settings.cfg` → `[net] update_url`).

## 7. Actualizar
- Servidor: *Deploy servidor → Run workflow* (o `docker compose -f docker-compose.prod.yml pull server && docker compose -f docker-compose.prod.yml up -d server`).
  El servidor guarda a todos los jugadores al pararse (`OnStopping`), pero avisa antes con `/announce`.
- Cliente: *Publicar cliente → Run workflow*. La web se actualiza al recargar; el `.zip` de itch.io lo bajan los jugadores.
- Contenido (`content/*.json`, `maps/*.tmj`) va dentro de la imagen: cambiar números = nuevo despliegue del servidor **y** del
  cliente (el cliente lleva su copia).

## 8. Ver logs y estado
```bash
docker compose -f docker-compose.prod.yml logs -f server      # JSON por línea (ConnId, CharacterName, AccountId cuando aplica)
docker compose -f docker-compose.prod.yml logs -f caddy
docker compose -f docker-compose.prod.yml logs backup
curl -s https://juego.midominio.com/health
curl -s -H "Authorization: Bearer <jwt de una cuenta admin>" https://juego.midominio.com/admin/stats | jq
```
El JWT se obtiene con `POST /api/auth/login` (15 min de vida). `/admin/stats` da tick p50/p99, jugadores, monstruos,
mensajes/s, bytes/s y, por instancia, p99 del combate, áreas y auras activas (HU-072).

## 9. Copias de seguridad (HU-075)
El servicio `backup` de `docker-compose.prod.yml` ejecuta `pg_dump -Fc` cada día a las 04:00 UTC en `~/pixelrealms/backups/`
y conserva las 7 más recientes. Copia manual: `docker compose -f docker-compose.prod.yml exec backup sh /backup.sh once`.
Guarda también una copia fuera del VPS (p. ej. `rclone`/`scp` nocturno de `backups/`): un solo disco no es un backup.

### Restaurar
```bash
cd ~/pixelrealms
docker compose -f docker-compose.prod.yml stop server
sh deploy/restore.sh backups/pixelrealms-20261001-040000.dump
docker compose -f docker-compose.prod.yml start server
```
`restore.sh` hace `pg_restore --clean --if-exists` sobre la base de producción (sobrescribe). **Pendiente**: ensayar una
restauración real al menos una vez (HU-075 CA2) y anotar aquí la fecha y el resultado.

## 10. Problemas frecuentes
- *Caddy no obtiene certificado*: el DNS no apunta aún o el puerto 80/443 está cerrado en el firewall del proveedor (además de ufw).
- *`/ws` devuelve 429*: tope de 10 conexiones por IP (HU-071). Detrás de Caddy el servidor usa `X-Forwarded-For` (solo en
  `ASPNETCORE_ENVIRONMENT=Production`); si todos los amigos salen por la misma IP (misma casa), sube `Net:RateLimits:MaxConnectionsPerIp`
  con la variable `Net__RateLimits__MaxConnectionsPerIp=20` en el servicio `server`.
- *El cliente web no carga*: faltan las cabeceras COOP/COEP (revisa el `Caddyfile`) o el navegador bloquea `SharedArrayBuffer`.
