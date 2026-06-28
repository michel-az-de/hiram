#!/usr/bin/env bash
# ADR-016 / plan step 0.7: daily logical backup of the hiram database plus the Data Protection key ring
# volume. The key ring backup matters as much as the data: losing it makes provider and tenant secrets
# undecryptable, which is an outage, exactly the scenario the shared key ring (step 0.3) introduced.
# Run from cron and ship the output off the VM.
set -euo pipefail

: "${PGHOST:?set PGHOST}"
: "${PGUSER:?set PGUSER (an admin or the hiram role with dump rights)}"
: "${BACKUP_DIR:?set BACKUP_DIR (an off-VM mount or synced directory)}"
: "${KEYRING_DIR:=/var/hiram/dataprotection-keys}"
: "${RETENTION_DAYS:=14}"

stamp="$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$BACKUP_DIR"

# Logical dump of the hiram database (custom format restores with pg_restore).
pg_dump --dbname=hiram --format=custom --file="$BACKUP_DIR/hiram-$stamp.dump"

# Key ring volume: a tarball so the secrets stay decryptable after a disk loss.
if [ -d "$KEYRING_DIR" ]; then
  tar -czf "$BACKUP_DIR/hiram-keyring-$stamp.tar.gz" -C "$KEYRING_DIR" .
else
  echo "warning: key ring directory $KEYRING_DIR not found; skipping key ring backup" >&2
fi

find "$BACKUP_DIR" -name 'hiram-*' -type f -mtime "+$RETENTION_DAYS" -delete
