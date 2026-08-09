#!/bin/bash
#
# Rotate the `plutus` MySQL password on the Mac mini (10.1.1.40).
#
# Run it ON THE MAC:
#     ssh admin@10.1.1.40
#     bash ~/PLUTUS/bin/rotate-mysql-password.sh      # (copy it there first)
#
# ⚠ THE PASSWORD IS GENERATED HERE AND NEVER PRINTED. That is the whole point: this exists
# because the old one was echoed into a session transcript twice (2026-07-26 and 2026-08-09).
# Nothing in this script writes the value to stdout, and every error path masks it. If you need
# to read it afterwards it is in ~/PLUTUS/secrets/mysql.env, which is the canonical store.
#
# ⚠ IT RESTARTS `plutus-backend`, so the test environment blips for a few seconds. It does NOT
# touch ETRIE: the restart targets ~/PLUTUS/plutus-ecosystem.config.js, which defines exactly one
# app (plutus-backend) — verified before writing this. ETRIE shares the pm2 instance, so never
# use a bare `pm2 restart all` here.
#
# What it updates:
#   1. MySQL itself (ALTER USER, authenticating with the current password)
#   2. ~/PLUTUS/secrets/mysql.env            — canonical store; the nightly backup sources it
#   3. ~/PLUTUS/plutus-ecosystem.config.js   — the ConnectionString the running backend uses
#   4. ~/.pm2/dump.pm2                       — via `pm2 save`, so a resurrect uses the new value
#
# NOT updated, and deliberately: appsettings*.json in the repo hold STALE placeholder strings
# (host.docker.internal, plutus-mysql-db:3307). They look authoritative and are not — the pm2 env
# override wins. Editing them changes nothing.
#
set -euo pipefail
export PATH="/opt/homebrew/bin:$PATH"
cd ~/PLUTUS

STAMP=$(date +%Y%m%d-%H%M%S)

# ── backups first, per the repo's .pre-* convention ──────────────────────────
cp secrets/mysql.env "secrets/mysql.env.pre-rotate-$STAMP"
cp plutus-ecosystem.config.js "plutus-ecosystem.config.js.pre-rotate-$STAMP"
echo "backups written: .pre-rotate-$STAMP"

OLD=$(sed -n 's/^MYSQL_PLUTUS_PASSWORD=//p' secrets/mysql.env)
NEW=$(LC_ALL=C tr -dc 'A-Za-z0-9' < /dev/urandom | head -c 40)

[ -n "$OLD" ]        || { echo "FAIL: could not read the current password"; exit 1; }
[ ${#NEW} -eq 40 ]   || { echo "FAIL: generated password is the wrong length"; exit 1; }

# ⚠ Alphanumeric ONLY. The value goes into a semicolon-delimited ADO.NET connection string and
# through sed; a `;`, `"` or `&` in it would either truncate the connection string or be eaten as
# a sed replacement metacharacter — and the failure would look like "database unreachable".

# ── 1. the canonical store FIRST, so the new value is recoverable before anything depends on it
sed -i '' "s|^MYSQL_PLUTUS_PASSWORD=.*|MYSQL_PLUTUS_PASSWORD=$NEW|" secrets/mysql.env
grep -q "^MYSQL_PLUTUS_PASSWORD=$NEW\$" secrets/mysql.env || { echo "FAIL: mysql.env not updated"; exit 1; }
echo "1/6  mysql.env written"

# ── 2. MySQL, authenticating with the old one. Roll the file back if it refuses.
if ! MYSQL_PWD="$OLD" mysql -uplutus -e "ALTER USER USER() IDENTIFIED BY '$NEW';" 2>/tmp/rot.err; then
  cp "secrets/mysql.env.pre-rotate-$STAMP" secrets/mysql.env
  echo "FAIL: ALTER USER was rejected — mysql.env restored, nothing has changed."
  sed -E "s/$OLD|$NEW/<redacted>/g" /tmp/rot.err | head -3
  rm -f /tmp/rot.err
  exit 1
fi
echo "2/6  MySQL password changed"

# ── 3. prove the new one actually authenticates before anything is restarted
ROWS=$(MYSQL_PWD="$NEW" mysql -uplutus -N -B -e "SELECT COUNT(*) FROM plutus.SalesV2;")
echo "3/6  new password verified (SalesV2 rows: $ROWS)"

# ── 4. the connection string the backend runs on
sed -i '' -E "s|(Password=)[^;]*(;)|\1$NEW\2|" plutus-ecosystem.config.js
grep -q "Password=$NEW;" plutus-ecosystem.config.js || { echo "FAIL: ConnectionString not updated"; exit 1; }
node -e "require(process.env.HOME + '/PLUTUS/plutus-ecosystem.config.js')" >/dev/null
echo "4/6  ecosystem ConnectionString updated and still parses as valid JS"

# ── 5. restart. ⚠ FROM THE FILE, not the process name: `pm2 restart plutus-backend --update-env`
# does NOT re-read new keys from the ecosystem file, which has cost this project a session before.
pm2 restart ~/PLUTUS/plutus-ecosystem.config.js --update-env
pm2 save
echo "5/6  plutus-backend restarted from the ecosystem file, pm2 state saved"

# ── 6. verify: the backend is up AND actually talking to the database, and ETRIE is untouched
sleep 5
PING=$(curl -s -o /dev/null -w "%{http_code}" http://127.0.0.1:5100/api/v1/ping || true)
ETRIE=$(curl -s -o /dev/null -w "%{http_code}" --resolve huggett.dscloud.me:443:127.0.0.1 https://huggett.dscloud.me/health || true)

echo "6/6  plutus ping: $PING   ETRIE health: $ETRIE"
rm -f /tmp/rot.err

if [ "$PING" != "200" ] || [ "$ETRIE" != "200" ]; then
  echo
  echo "⚠ SOMETHING IS NOT ANSWERING. Roll back with:"
  echo "    cp ~/PLUTUS/plutus-ecosystem.config.js.pre-rotate-$STAMP ~/PLUTUS/plutus-ecosystem.config.js"
  echo "    cp ~/PLUTUS/secrets/mysql.env.pre-rotate-$STAMP ~/PLUTUS/secrets/mysql.env"
  echo "  then set the password back with the OLD value from that restored mysql.env:"
  echo "    MYSQL_PWD=\"\$NEW\" mysql -uplutus -e \"ALTER USER USER() IDENTIFIED BY '<old>';\""
  echo "  (pm2 restart ~/PLUTUS/plutus-ecosystem.config.js --update-env afterwards)"
  exit 1
fi

echo
echo "ROTATION COMPLETE. /api/v1/ping is a no-database endpoint, so also click through the portal"
echo "once — a page that reads data proves the ConnectionString, which ping does not."
