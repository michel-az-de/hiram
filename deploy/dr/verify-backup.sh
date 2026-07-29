#!/usr/bin/env bash
set -euo pipefail

container="hiram-dr-${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-0}-$$"
work_dir="$(mktemp -d)"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

cleanup() {
  docker rm --force "$container" >/dev/null 2>&1 || true
  rm -rf "$work_dir"
}
trap cleanup EXIT

mkdir -p "$work_dir/backups" "$work_dir/keyring" "$work_dir/restored-keyring"
printf '%s\n' "key-ring-recovery-probe" > "$work_dir/keyring/key.xml"

docker run --detach \
  --name "$container" \
  --env POSTGRES_PASSWORD=postgres \
  --env POSTGRES_DB=hiram \
  postgres:17 >/dev/null

ready=false
for _ in {1..60}; do
  if docker exec "$container" pg_isready --username=postgres --dbname=hiram >/dev/null 2>&1; then
    ready=true
    break
  fi
  sleep 1
done
if [ "$ready" != true ]; then
  echo "PostgreSQL did not become ready within 60 seconds" >&2
  exit 1
fi

docker exec "$container" psql --username=postgres --dbname=hiram --set ON_ERROR_STOP=1 \
  --command "CREATE TABLE recovery_probe (value text NOT NULL); INSERT INTO recovery_probe VALUES ('database-recovery-probe');"

if docker run --rm \
  --network "container:$container" \
  --env PGHOST=127.0.0.1 \
  --env PGUSER=postgres \
  --env PGPASSWORD=postgres \
  --env BACKUP_DIR=/backup \
  --env KEYRING_DIR=/missing \
  --volume "$script_dir:/scripts:ro" \
  --volume "$work_dir/backups:/backup" \
  postgres:17 \
  bash /scripts/hiram-backup.sh; then
  echo "backup accepted a missing key ring" >&2
  exit 1
fi

docker run --rm \
  --network "container:$container" \
  --env PGHOST=127.0.0.1 \
  --env PGUSER=postgres \
  --env PGPASSWORD=postgres \
  --env BACKUP_DIR=/backup \
  --env KEYRING_DIR=/keys \
  --volume "$script_dir:/scripts:ro" \
  --volume "$work_dir/backups:/backup" \
  --volume "$work_dir/keyring:/keys:ro" \
  postgres:17 \
  bash /scripts/hiram-backup.sh

dump_file="$(find "$work_dir/backups" -name 'hiram-*.dump' -type f -print -quit)"
keyring_archive="$(find "$work_dir/backups" -name 'hiram-keyring-*.tar.gz' -type f -print -quit)"
test -n "$dump_file"
test -n "$keyring_archive"

docker exec "$container" createdb --username=postgres hiram_restored

docker run --rm \
  --network "container:$container" \
  --env PGHOST=127.0.0.1 \
  --env PGUSER=postgres \
  --env PGPASSWORD=postgres \
  --volume "$dump_file:/backup/hiram.dump:ro" \
  postgres:17 \
  pg_restore --exit-on-error --dbname=hiram_restored /backup/hiram.dump

database_probe="$(docker exec "$container" psql --username=postgres --dbname=hiram_restored --tuples-only --no-align --command "SELECT value FROM recovery_probe;")"
test "$database_probe" = "database-recovery-probe"

tar -xzf "$keyring_archive" -C "$work_dir/restored-keyring"
cmp "$work_dir/keyring/key.xml" "$work_dir/restored-keyring/key.xml"

echo "Database and Data Protection key ring restored successfully."
