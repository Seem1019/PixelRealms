#!/bin/sh
# HU-075: copia diaria `pg_dump -Fc` a /backups y conserva las últimas $BACKUP_KEEP (7). Uso dentro del contenedor `backup`:
#   sh /backup.sh once   → una copia ahora        sh /backup.sh loop → una copia diaria a las $BACKUP_HOUR_UTC
set -eu
KEEP="${BACKUP_KEEP:-7}"
DIR="${BACKUP_DIR:-/backups}"
mkdir -p "$DIR"

dump() {
  stamp="$(date -u +%Y%m%d-%H%M%S)"
  out="$DIR/pixelrealms-$stamp.dump"
  pg_dump -Fc -f "$out.tmp" "$PGDATABASE" && mv "$out.tmp" "$out"
  echo "backup ok: $out ($(du -h "$out" | cut -f1))"
  # Conserva los $KEEP más recientes.
  ls -1t "$DIR"/pixelrealms-*.dump 2>/dev/null | tail -n +"$((KEEP + 1))" | while read -r old; do rm -f "$old"; echo "borrado: $old"; done
}

case "${1:-once}" in
  once) dump ;;
  loop)
    hour="${BACKUP_HOUR_UTC:-04}"
    echo "backup diario a las ${hour}:00 UTC, conservando $KEEP"
    while true; do
      if [ "$(date -u +%H)" = "$hour" ]; then dump; sleep 3600; fi
      sleep 300
    done ;;
  *) echo "uso: backup.sh once|loop" >&2; exit 2 ;;
esac
