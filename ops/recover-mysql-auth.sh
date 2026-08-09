#!/bin/bash
#
# RECOVERY: plutus-backend cannot authenticate to MySQL after a password rotation.
#
# Run it ON THE MAC:
#     bash ~/PLUTUS/bin/recover-mysql-auth.sh
#
# ── WHAT HAPPENED ────────────────────────────────────────────────────────────
# The `plutus`@`localhost` account uses `caching_sha2_password` (confirmed via
# SHOW CREATE USER). Under that plugin MySQL keeps a server-side cache of the
# password digest; a connection can only use the cheap "fast auth" path once that
# cache is warm. Changing the password EMPTIES it.
#
# With a cold cache the client must do FULL authentication, which MySQL permits
# only over a channel it considers secure: a unix socket, or TLS, or an RSA
# key exchange. The backend connects over plain TCP to 127.0.0.1:3306 and does
# none of those, so it cannot complete the first authentication and never will —
# every connection attempt fails identically.
#
# That is why it worked for months and broke the instant the password changed:
# the cache had been warm since whenever the account was last set up. Nothing
# about the new password is wrong. Verified: the exact value in
# ~/PLUTUS/secrets/mysql.env authenticates over the socket and is rejected over
# TCP, same server (one mysqld, PID confirmed), same account row.
#
# ── WHAT THIS DOES ───────────────────────────────────────────────────────────
# Tries the smallest fix first and stops as soon as the service answers:
#   A. Add AllowPublicKeyRetrieval=true — lets the client fetch the server's RSA
#      public key and complete full auth over TCP. Keeps everything else as-is.
#   B. Switch to the unix socket — the channel already proven to authenticate,
#      and strictly safer for a same-host backend (no TCP auth exposure at all).
#   C. If neither works, restore the original file and restart, so you are back
#      where you started rather than somewhere new.
#
# It never changes the password and never touches ETRIE: every restart targets
# ~/PLUTUS/plutus-ecosystem.config.js, which defines exactly one app.
#
set -uo pipefail
export PATH="/opt/homebrew/bin:$PATH"
cd ~/PLUTUS

STAMP=$(date +%Y%m%d-%H%M%S)
BACKUP="plutus-ecosystem.config.js.pre-recover-$STAMP"
cp plutus-ecosystem.config.js "$BACKUP"
echo "backup: $BACKUP"

PW=$(sed -n 's/^MYSQL_PLUTUS_PASSWORD=//p' secrets/mysql.env)
[ -n "$PW" ] || { echo "FAIL: no password in secrets/mysql.env"; exit 1; }

# Prove the value itself is good before blaming the connection string.
if MYSQL_PWD="$PW" mysql --no-defaults -uplutus -N -B -e "SELECT 1;" >/dev/null 2>&1; then
  echo "precheck: the password in mysql.env authenticates over the socket — the value is correct"
else
  echo "FAIL: the password in mysql.env does NOT authenticate at all. This script cannot help;"
  echo "      the rotation left MySQL and the file disagreeing. Stop and investigate."
  exit 1
fi

set_connstr() {   # $1 = the full new ConnectionString
  sed -E "s|(ConnectionString: \")[^\"]*(\")|\1$1\2|" "$BACKUP" > plutus-ecosystem.config.js
  grep -q "ConnectionString" plutus-ecosystem.config.js || { echo "  FAIL: rewrite lost the key"; return 1; }
  node -e "require(process.env.HOME + '/PLUTUS/plutus-ecosystem.config.js')" >/dev/null 2>&1 || { echo "  FAIL: not valid JS"; return 1; }
  pm2 restart ~/PLUTUS/plutus-ecosystem.config.js --update-env >/dev/null 2>&1
  sleep 12
  [ "$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:5100/api/v1/ping || true)" = "200" ]
}

BASE="User=plutus;Password=$PW;Database=plutus;Persist Security Info=false;Connect Timeout=300"

echo
echo "A. trying AllowPublicKeyRetrieval over TCP…"
if set_connstr "Server=127.0.0.1;Port=3306;$BASE;AllowPublicKeyRetrieval=true"; then
  pm2 save >/dev/null 2>&1
  echo "   ✓ SERVICE RESTORED (TCP + public-key retrieval)"
  RESULT=A
else
  echo "   ✗ still not answering"
  echo
  echo "B. trying the unix socket…"
  if set_connstr "Server=/tmp/mysql.sock;ConnectionProtocol=unix;$BASE"; then
    pm2 save >/dev/null 2>&1
    echo "   ✓ SERVICE RESTORED (unix socket)"
    RESULT=B
  else
    echo "   ✗ still not answering"
    echo
    echo "C. restoring the original connection string…"
    cp "$BACKUP" plutus-ecosystem.config.js
    pm2 restart ~/PLUTUS/plutus-ecosystem.config.js --update-env >/dev/null 2>&1
    sleep 10
    RESULT=C
  fi
fi

echo
echo "── final state ──"
echo "  plutus ping : $(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:5100/api/v1/ping || true)  (200 = up)"
echo "  ETRIE health: $(curl -s -o /dev/null -w '%{http_code}' --resolve huggett.dscloud.me:443:127.0.0.1 https://huggett.dscloud.me/health || true)  (must be 200)"
echo "  pm2 restarts: $(pm2 jlist 2>/dev/null | node -e "let d='';process.stdin.on('data',c=>d+=c).on('end',()=>{const p=JSON.parse(d).find(x=>x.name=='plutus-backend');console.log(p?p.pm2_env.restart_time:'?')})")"
echo "  outcome     : $RESULT"

if [ "$RESULT" = "C" ]; then
  echo
  echo "⚠ NEITHER FIX WORKED and the original connection string is back — but that one cannot"
  echo "  authenticate either, because the problem is the cold caching_sha2 cache, not the string."
  echo "  The remaining options both need root:"
  echo "    • ALTER USER 'plutus'@'localhost' IDENTIFIED WITH caching_sha2_password BY '<pw>'"
  echo "      as root over the socket, then immediately connect once over the socket as plutus"
  echo "      to warm the cache."
  echo "    • or grant a second account for TCP, or enable TLS properly on 3306."
  echo "  Until then the backend is down; ETRIE is unaffected."
fi
