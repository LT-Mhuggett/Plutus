#!/bin/zsh
# Plutus nightly MySQL backup (WP12.3).
#
# ⚠⚠ THIS SCRIPT SILENTLY PRODUCED EMPTY BACKUPS FOR TWO DAYS (2026-08-10 and -11) AND LOGGED
# "backup ok" BOTH TIMES. Two independent faults, and it needed both to stay hidden:
#
#   1. IT CONNECTED OVER PLAIN TCP (`-h 127.0.0.1`). The `plutus` password was rotated late on
#      2026-08-09, and rotating a `caching_sha2_password` account breaks plain-TCP clients — which
#      is exactly why the backend was moved onto the unix socket at the time. This script was not.
#      Last good dump 08-09 (58.8 MB), 0 bytes from 08-10.
#
#   2. IT COULD NOT TELL. `mysqldump | gzip && mv` takes its exit status from GZIP, which succeeds
#      happily on empty input — so `mv` ran, a 20-byte file was written, and the log recorded
#      success. A backup that cannot fail is not a backup; it is a reassuring noise.
#
# ⚠ The old retention line would have deleted the last GOOD dump on 2026-08-16, leaving nothing.
#
# ⚠ NEVER RUN THIS UNDER `bash -x` / `zsh -x`. On 2026-08-09 that printed the connection string —
# password included — into a session transcript, and the credential had to be treated as burned.
# The script is written so the secret never reaches stdout; tracing defeats that.

set -o pipefail          # ⚠ THE FIX FOR FAULT 2 — without it, gzip's success masks mysqldump's failure
setopt ERR_EXIT          # zsh's `set -e`

source ~/PLUTUS/secrets/mysql.env

STAMP=$(date +%Y%m%d)
DEST=~/PLUTUS/backups/nightly
LOG="$DEST/backup.log"
mkdir -p "$DEST"

# ⚠ A FLOOR, NOT A ZERO-CHECK. An empty gzip is 20 bytes and a dump of an EMPTY SCHEMA is a few
# hundred — both are "successful" to any test that only asks whether the file exists or is non-empty.
# The real database compresses to ~15 MB, so anything under 1 MB means something is wrong even if
# every command returned 0. Deliberately far below the true size: this is a tripwire, not a quota.
MIN_BYTES=1000000

fail() {
  echo "$(date -u +%FT%TZ) ⚠ BACKUP FAILED: $1" >> "$LOG"
  echo "⚠ BACKUP FAILED: $1" >&2
  exit 1
}

for DB in plutus plutus_t1; do
  TMP="$DEST/$DB-$STAMP.sql.gz.tmp"

  # ⚠ --socket, NOT -h 127.0.0.1 — see fault 1. The socket is what the backend uses and what
  # survives a caching_sha2 password rotation.
  # ⚠ plutus lacks RELOAD, hence --no-tablespaces --skip-lock-tables --set-gtid-purged=OFF.
  if ! /opt/homebrew/bin/mysqldump -u plutus -p"$MYSQL_PLUTUS_PASSWORD" \
        --socket=/tmp/mysql.sock \
        --no-tablespaces --skip-lock-tables --set-gtid-purged=OFF "$DB" \
        2>>"$DEST/mysqldump.err" | gzip > "$TMP"; then
    rm -f "$TMP"
    fail "$DB — mysqldump or gzip returned non-zero (see mysqldump.err)"
  fi

  SIZE=$(stat -f%z "$TMP" 2>/dev/null || echo 0)

  # ⚠ plutus_t1 is a small tenant schema and legitimately dumps far smaller than plutus, so the
  # floor only guards the main database. It is the one whose loss would be unrecoverable.
  if [[ "$DB" == "plutus" && "$SIZE" -lt "$MIN_BYTES" ]]; then
    rm -f "$TMP"
    fail "$DB — dump was only ${SIZE} bytes (floor ${MIN_BYTES}). NOT kept; the previous good backup is untouched."
  fi

  # ⚠ Only now does it replace anything. The .tmp dance was already right — the bug was never
  # checking whether the .tmp was worth promoting.
  mv "$TMP" "$DEST/$DB-$STAMP.sql.gz"
  echo "$(date -u +%FT%TZ) backup ok: $DB ${SIZE} bytes" >> "$LOG"
done

# Retention: keep 7 days.
# ⚠ RUNS ONLY AFTER A VERIFIED-GOOD DUMP, because `fail` exits before reaching here. The old script
# pruned unconditionally, so two failed nights would have aged out the last usable backup while
# writing empty files over the top — the deletion and the corruption arriving from the same clock.
find "$DEST" -name "*.sql.gz" -mtime +7 -delete
