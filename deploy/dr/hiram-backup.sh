#!/usr/bin/env bash
# Daily logical backup of the Hiram database and the Data Protection key ring.
# Losing either part prevents a complete recovery, so the script fails when the key ring is absent.
# Run from cron and ship the output off the host.
set -euo pipefail

: "${PGHOST:?set PGHOST}"
: "${PGUSER:?set PGUSER (an admin or the hiram role with dump rights)}"
: "${BACKUP_DIR:?set BACKUP_DIR (an off-VM mount or synced directory)}"
: "${KEYRING_DIR:=/var/hiram/dataprotection-keys}"
: "${RETENTION_DAYS:=14}"

stamp="$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$BACKUP_DIR"

if [ ! -d "$KEYRING_DIR" ]; then
  echo "key ring directory $KEYRING_DIR not found; refusing an incomplete backup" >&2
  exit 1
fi

pg_dump --dbname=hiram --format=custom --file="$BACKUP_DIR/hiram-$stamp.dump"

tar -czf "$BACKUP_DIR/hiram-keyring-$stamp.tar.gz" -C "$KEYRING_DIR" .

find "$BACKUP_DIR" -name 'hiram-*' -type f -mtime "+$RETENTION_DAYS" -delete
