#!/bin/sh
# HU-075 CA2: restaura un backup `pg_dump -Fc` en la base de producción (¡sobrescribe!). Ejecutar en el VPS, con el servidor parado:
#   docker compose -f docker-compose.prod.yml stop server
#   sh deploy/restore.sh backups/pixelrealms-YYYYMMDD-HHMMSS.dump
#   docker compose -f docker-compose.prod.yml start server
set -eu
FILE="${1:?ruta al .dump}"
[ -f "$FILE" ] || { echo "no existe $FILE" >&2; exit 1; }
set -a; [ -f .env ] && . ./.env; set +a
DB="${POSTGRES_DB:-pixelrealms}"; USER="${POSTGRES_USER:-pixelrealms}"
docker compose -f docker-compose.prod.yml cp "$FILE" postgres:/tmp/restore.dump
docker compose -f docker-compose.prod.yml exec -T postgres pg_restore -U "$USER" -d "$DB" --clean --if-exists --no-owner /tmp/restore.dump
docker compose -f docker-compose.prod.yml exec -T postgres rm -f /tmp/restore.dump
echo "restaurado $FILE en $DB"
