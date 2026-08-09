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

# ⚠ FAILURE MUST BE LOUD. The first version of this script died at the password-generation line
# and printed nothing beyond "backups written" — which reads exactly like success. The rotation
# silently did not happen, the exposed password stayed live, and it took an independent check of
# the files' modification times to notice. A script that changes credentials must never be
# ambiguous about whether it did.
CHANGED=no
trap 'rc=$?; if [ "$CHANGED" = no ]; then
        rm -f "secrets/mysql.env.pre-rotate-$STAMP" "plutus-ecosystem.config.js.pre-rotate-$STAMP"
        echo; echo "✗ ABORTED at line $LINENO (exit $rc). NOTHING WAS CHANGED — the old password is still live.";
        echo "  Backups from this run were removed (they were identical copies of the live secret).";
      else
        echo; echo "✗ FAILED PART-WAY at line $LINENO (exit $rc). See the rollback note above; backups kept as .pre-rotate-'"$STAMP"'.";
      fi' ERR

# ── backups first, per the repo's .pre-* convention ──────────────────────────
cp secrets/mysql.env "secrets/mysql.env.pre-rotate-$STAMP"
cp plutus-ecosystem.config.js "plutus-ecosystem.config.js.pre-rotate-$STAMP"
echo "backups written: .pre-rotate-$STAMP"

OLD=$(sed -n 's/^MYSQL_PLUTUS_PASSWORD=//p' secrets/mysql.env)

# ⚠ NO PIPELINE HERE, and that is not a style choice — it is the bug that made the first version
# of this script do nothing. `tr -dc … < /dev/urandom | head -c 40` looks obviously fine, but
# `head` exits the moment it has its 40 bytes and closes the pipe, `tr` dies of SIGPIPE (141),
# `pipefail` promotes that to the pipeline's status and `set -e` kills the script — right after
# the backups and BEFORE anything was changed. It reported nothing useful and left the live
# password in place while looking like it had run. `openssl rand` produces finite output and
# needs no pipe.
#
# ⚠ Hex is alphanumeric by construction, which matters twice over: the value goes into a
# SEMICOLON-DELIMITED ADO.NET connection string and through `sed`, so a `;`, `"` or `&` would
# either truncate the connection string or be eaten as a replacement metacharacter — and the
# failure would present as "database unreachable" long after the cause.
NEW=$(openssl rand -hex 24)   # 48 chars, 192 bits

[ -n "$OLD" ]        || { echo "FAIL: could not read the current password"; exit 1; }
[ ${#NEW} -eq 48 ]   || { echo "FAIL: generated password is the wrong length"; exit 1; }

# ── PRE-FLIGHT: can the backend still authenticate AFTER the password changes? ────────────────
#
# ⚠ THIS CHECK EXISTS BECAUSE ROTATING TOOK THE BACKEND DOWN ON 2026-08-09, and no amount of
# care AFTER the ALTER could have saved it. The `plutus` account uses `caching_sha2_password`,
# which keeps a server-side cache of the password digest; only the cheap "fast auth" path works
# from that cache, and CHANGING THE PASSWORD EMPTIES IT. With a cold cache the client must do
# FULL authentication, which MySQL permits only over a channel it considers secure — a unix
# socket, TLS, or an RSA key exchange.
#
# A connection string of plain `Server=127.0.0.1;Port=3306` has none of those. It works
# indefinitely while the cache is warm and fails permanently the moment it is not, so the damage
# is done by the ALTER itself. Rolling the password back does NOT undo it — the cache stays cold
# for the old value too. The only safe moment to catch this is now, BEFORE anything changes.
CONNSTR=$(grep -oE 'ConnectionString: "[^"]*"' plutus-ecosystem.config.js | sed 's/ConnectionString: "//; s/"$//')

case "$CONNSTR" in
  *ConnectionProtocol=unix*|*Protocol=unix*)
      echo "pre-flight: backend connects over the unix socket — safe to rotate" ;;
  *AllowPublicKeyRetrieval=true*|*SslMode=Required*|*SslMode=VerifyCA*|*SslMode=VerifyFull*)
      echo "pre-flight: backend can complete full auth over TCP — safe to rotate" ;;
  *)
      rm -f "secrets/mysql.env.pre-rotate-$STAMP" "plutus-ecosystem.config.js.pre-rotate-$STAMP"
      echo
      echo "✗ REFUSING TO ROTATE — nothing has been changed."
      echo
      echo "  The backend connects over plain TCP with no TLS and no AllowPublicKeyRetrieval:"
      echo "      $(printf '%s' "$CONNSTR" | sed -E 's/(Password=)[^;]*/\1<redacted>/')"
      echo
      echo "  The account is caching_sha2_password. Changing the password empties the server's"
      echo "  auth cache, and this connection string cannot complete the full authentication that"
      echo "  then becomes necessary — so the backend would crash-loop with 'Access denied' and"
      echo "  rolling the password back would NOT fix it."
      echo
      echo "  Fix the connection string FIRST (either is fine, the socket is safer for a same-host"
      echo "  backend), restart, confirm the service is healthy, then re-run this script:"
      echo "      Server=/tmp/mysql.sock;ConnectionProtocol=unix;User=plutus;Password=…;Database=plutus"
      echo "      …or append  ;AllowPublicKeyRetrieval=true  to the existing TCP string"
      exit 1 ;;
esac

# ── 1. the canonical store FIRST, so the new value is recoverable before anything depends on it
CHANGED=yes
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

# ⚠ PROVE IT ACTUALLY CHANGED, rather than trusting that the steps above ran. This is the check
# that caught the first version doing nothing: the files' modification times, compared against
# this run. "The script ran" and "the password rotated" are different claims.
for f in secrets/mysql.env plutus-ecosystem.config.js; do
  MOD=$(stat -f "%Sm" -t "%Y%m%d-%H%M%S" "$f")
  case "$MOD" in
    "${STAMP%%-*}"*) echo "verified: $f was modified today at ${MOD#*-}" ;;
    *) echo "✗ $f was NOT modified (last change $MOD) — the rotation did not take"; exit 1 ;;
  esac
done

echo
echo "ROTATION COMPLETE. /api/v1/ping is a no-database endpoint, so also click through the portal"
echo "once — a page that reads data proves the ConnectionString, which ping does not."
