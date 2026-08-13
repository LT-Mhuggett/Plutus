# Migrate the back end to Linux

**Date:** 2026-08-13 · **Status:** plan; nothing done. **Matt asked:** *"You mentioned something
about the back end being built on ARM and I would need to redevelop it if I moved to a linux server.
What is needed here?"*

> ## The answer: nothing needs redeveloping. Not one line of the backend.
>
> `Plutus.DBService` targets plain **`net10.0`**, references **no native libraries**, and contains
> **no OS branching at all**. `osx-arm64` is a **publish flag in the runbook**, not a property of the
> code — swap it for `linux-x64` (or `linux-arm64`) and the same source produces a Linux binary.
>
> ⚠ **The cost is entirely operational, and it is dominated by one thing that is not obvious:
> [the MySQL authentication channel](#1--the-real-problem-the-mysql-auth-channel).** The unix-socket
> workaround that keeps the backend talking to MySQL today **cannot cross a machine boundary.**
>
> **Estimate: 1–2 days including verification and a rehearsal.** No feature work, no schema change,
> no till change — **provided the public hostname does not change** ([§7](#7-what-must-not-change)).

**Read with:** [`../repo-runbook.md`](../repo-runbook.md) (the deploy procedure this replaces half of)
and [`../../HANDOVER.md`](../../HANDOVER.md) (what is live today).

---

## Contents

[0 Why it is portable](#0-why-it-is-portable--the-evidence) · [1 ⚠ The real problem](#1--the-real-problem-the-mysql-auth-channel) ·
[2 Decide the topology first](#2-decide-the-topology-first) · [3 What moves](#3-what-moves--the-inventory) ·
[4 The plan](#4-the-plan) · [5 Verification](#5-verification--nothing-is-done-until-these-pass) ·
[6 Traps](#6-traps--each-one-already-cost-somebody-a-day-here) · [7 What must not change](#7-what-must-not-change) ·
[8 Open decisions](#8-open-decisions--matts-call)

---

## 0. Why it is portable — the evidence

Checked against the tree on 2026-08-13, not assumed:

| Check | Finding | Where |
|---|---|---|
| Target framework | **`net10.0`**, plain. **No `RuntimeIdentifier` in the project at all** | [`Plutus.DBService.csproj`](../../Plutus/Endpoints/Plutus.DBService/Plutus.DBService.csproj) |
| Where `osx-arm64` lives | Only as a **command-line argument** in the runbook and the deploy notes | [`../repo-runbook.md`](../repo-runbook.md) §Backend deploy |
| Native dependencies | **None.** The only data package is `Pomelo.EntityFrameworkCore.MySql` 9.0.0 — fully managed. **No SkiaSharp, System.Drawing, PDF or spreadsheet libraries anywhere in the backend or `src/`** — those are the usual Linux breakers, and Syncfusion/`XlsIO` is till-side only | `Plutus.Entities.csproj` |
| OS branching | **Zero hits** for `IsOSPlatform`, `OperatingSystem.Is*` or `OSPlatform.` across `src/`, `Plutus/Endpoints`, `Plutus/Commons` and `tools/` | — |
| File logging | **None** — no Serilog, no file sinks. Logs go to stdout, which is why pm2 captures them | — |
| Static content | **None served by the backend.** Caddy serves the till and portal bundles; the backend is API-only | HANDOVER §1 |
| Stated intent | The csproj already declares `<DockerDefaultTargetOS>Linux</DockerDefaultTargetOS>` | csproj |

⚠ **But there is no Dockerfile.** The Docker properties and the
`Microsoft.VisualStudio.Azure.Containers.Tools.Targets` package reference are **vestigial Visual
Studio tooling** — `find` returns no `Dockerfile` anywhere in the repo. Containerising is a real (if
small) piece of new work, not something already sitting there. See [§8](#8-open-decisions--matts-call).

**So the build-side change is one word:**

```bash
# today
dotnet publish -c Release -r osx-arm64  --self-contained true
# on Linux (x86-64 host)
dotnet publish -c Release -r linux-x64  --self-contained true
# on an ARM Linux host (Pi 5, Ampere/Graviton VPS) — the architecture genuinely does not matter
dotnet publish -c Release -r linux-arm64 --self-contained true
# Alpine or anything else on musl instead of glibc
dotnet publish -c Release -r linux-musl-x64 --self-contained true
```

`--self-contained` means **the runtime ships inside the tarball**, so the new box needs no .NET
installed. The publish is a cross-compile, so whichever machine publishes today keeps publishing.

---

## 1. ⚠⚠ The real problem: the MySQL auth channel

**This is the whole migration. Everything else is a checklist.**

[`ops/recover-mysql-auth.sh`](../../ops/recover-mysql-auth.sh) lines 9–33 records what was learned
the hard way on 2026-08-09:

> The `plutus`@`localhost` account uses `caching_sha2_password`. Under that plugin MySQL keeps a
> server-side cache of the password digest; a connection can use the cheap "fast auth" path only once
> that cache is warm. **Changing the password EMPTIES it.** With a cold cache the client must do FULL
> authentication, which MySQL permits **only over a channel it considers secure: a unix socket, or
> TLS, or an RSA key exchange.**

The backend was on plain TCP to `127.0.0.1:3306` with none of those, so after the rotation it could
not authenticate at all — and the fix was **to put it on `/tmp/mysql.sock`**, the channel already
proven to work, described in that script as *"strictly safer for a same-host backend (no TCP auth
exposure at all)."*

⚠ **A unix socket exists only between processes on the same machine.** So the moment the backend
lives on a different box from MySQL, that fix evaporates and you are back on the path that failed —
this time across a LAN, with credentials on the wire. **The reasoning that made the socket the right
answer inverts when the hosts separate**, so this is a decision to take deliberately:

| Option | What it means | Verdict |
|---|---|---|
| **Move MySQL to the Linux box too** | Keep app and database same-host; keep the unix socket (path changes to `/var/run/mysqld/mysqld.sock` or similar). No auth redesign at all | ⭐ **Recommended if you are moving anyway.** It preserves the property that made today's setup safe. Cost: a real database migration — dump, restore, re-point, re-prove the backups |
| **TLS on the MySQL connection** | Backend stays on TCP but the channel becomes secure, so `caching_sha2_password` can do full auth. Server certificate, `SslMode=Required` (**not** `Preferred`, which silently falls back) in the connection string | The correct answer if app and database must be on different hosts. Costs a cert and a verification step |
| **`AllowPublicKeyRetrieval=true`** | The client fetches the server's RSA public key and completes full auth over plain TCP. This is fix "A" in the recovery script | ⚠ **Works, and weakest.** It means trusting a key handed over by whatever answered on port 3306, and the password still crosses the LAN inside an RSA envelope rather than a TLS tunnel. Acceptable on an isolated LAN, not a habit to keep |
| Change the account to `mysql_native_password` | Sidesteps the plugin entirely | ❌ **No.** Downgrading password hashing to dodge a config problem, on the box holding the shop's sales history |

⚠ **Whichever you choose, prove it against a COLD cache** — restart mysqld (or `FLUSH PRIVILEGES`
plus a fresh connection) before believing it. The whole reason this bit once is that a warm cache had
been hiding the problem for months: *"it worked for months and broke the instant the password
changed."*

---

## 2. Decide the topology first

The Mac mini currently runs **five things**, and only one of them is being moved:

| On the Mac today | Moving? |
|---|---|
| `plutus-backend` (.NET, pm2, `127.0.0.1:5100`) | **Yes — this is the migration** |
| MySQL 9.6 (Homebrew), schema `plutus` + staging `plutus_t1` | **Your call — see [§1](#1--the-real-problem-the-mysql-auth-channel)** |
| Caddy — TLS, the two static bundles, reverse-proxy `/api/*` → 5100 | Optional |
| The static till + portal bundles at `/srv/apps/PLUTUS/{web,portal}/current` | Only if Caddy moves |
| ⚠⚠ **ETRIE** — a **live, unrelated production app** | ❌ **NEVER. It is untouchable** |

**Three sane shapes:**

| | Topology | Pros | Cons |
|---|---|---|---|
| **A** | **Backend only** moves to Linux. MySQL, Caddy, the bundles and ETRIE stay on the Mac; Caddy reverse-proxies `/api/*` across the LAN to the Linux box | Smallest change. Hostname, certs, DNS, router forwarding and every enrolled till are untouched. Reversible in one Caddy line | ⚠ **Forces the [§1](#1--the-real-problem-the-mysql-auth-channel) decision** — the socket is gone. Adds a LAN hop to every query |
| **B** | **Backend + MySQL** move; Caddy, bundles and ETRIE stay on the Mac | Keeps the unix socket, so no auth redesign. Certs/DNS/tills still untouched | A database migration, and the backup/rotation scripts move with it. Two boxes to keep patched |
| **C** | **Everything except ETRIE** moves | One box to reason about; the Mac keeps only ETRIE | ⚠ Certs re-issue, the router's 80/443 forward changes, and **both hostnames** move — the largest blast radius, and ETRIE shares that edge today |

**Recommendation: B if the goal is to leave the Mac behind, A if the goal is only to get .NET off
macOS.** Do **not** start with C: it changes the edge, the certs and the DNS at the same time as the
runtime, and ETRIE is behind that same edge.

⚠ **The router SNATs WAN→LAN**, which is why Caddy IP allowlists do not work here (auth is the gate,
not IP) — so do not plan any part of this around source-IP restrictions.

---

## 3. What moves — the inventory

Everything the running process depends on that is **not** in the tarball:

| # | Thing | Where it is now | Note |
|---|---|---|---|
| 1 | **The MySQL connection string** | the **pm2 env**, not `appsettings.json` | ⚠ The committed `appsettings.json` holds a *dev* value (`Server=plutus-mysql-db…Port=3307`) and `appsettings.Development.json` holds `host.docker.internal`. **Neither is live.** Whatever supervises the process on Linux must carry the real one |
| 2 | ⚠⚠ **`TEST_TOKEN_SECRET`** | `~/PLUTUS/secrets/mysql.env` | The HMAC signing secret for every bearer token. **It must move verbatim.** Regenerate it and every live operator session and device token is void until re-minted |
| 3 | **`ASPNETCORE_URLS` / the port** | pm2 env — **it is in no config file in the repo** | Verified: no `5100`, `Urls` or `UseKestrel` in the DBService config. Bind `127.0.0.1:5100` on the new host if Caddy is remote-proxying, or `0.0.0.0:5100` behind a firewall — **decide explicitly, don't inherit a default** |
| 4 | **Webstore secrets** | `~/PLUTUS/secrets/webstore-secrets.json` | `Startup.cs:77` defaults to `<user profile>/PLUTUS/secrets/webstore-secrets.json` via `SpecialFolder.UserProfile` — cross-platform, resolves on Linux, but the file must be there. ⚠ **Or set `Webstore:SecretsFile`** and stop depending on a home-directory layout. This is also the file the WP6.1 wc-auth callback WRITES, so it must be writable and must survive deploys |
| 5 | **Process supervision** | pm2, app `plutus-backend`, from `~/PLUTUS/plutus-ecosystem.config.js` | pm2 is Node and runs fine on Linux, but **systemd is the better fit**: it gives you `Restart=always`, journald, and env from a unit file. ⚠ Either way, the app writes no log files — logs are **stdout**, so whatever supervises it owns log retention |
| 6 | **The four ops scripts** | [`ops/mac/plutus-nightly-backup.sh`](../../ops/mac/plutus-nightly-backup.sh), [`ops/mac/verify-tillrelease.sh`](../../ops/mac/verify-tillrelease.sh), [`ops/rotate-mysql-password.sh`](../../ops/rotate-mysql-password.sh), [`ops/recover-mysql-auth.sh`](../../ops/recover-mysql-auth.sh) | All hardcode `/opt/homebrew/bin/mysql{,dump}` and `--socket=/tmp/mysql.sock`. On Linux: `/usr/bin`, and the socket is usually `/var/run/mysqld/mysqld.sock`. ⚠ **They move only if MySQL moves.** Rename the folder `ops/mac/` → `ops/host/` if it stops being a Mac |
| 7 | **The nightly backup schedule** | cron/launchd on the Mac | ⚠ **This one already failed silently for two days** and logged `backup ok`. It must be re-proven **both ways** on the new host — a real run (≈63 MB / 101 `CREATE TABLE`) *and* a deliberately broken one exiting 1 with `⚠ BACKUP FAILED` and the good dump untouched |
| 8 | **The side tools** | `Plutus.SeedMigrator`, [`tools/Plutus.TenantRestore`](../../tools/Plutus.TenantRestore/RUNBOOK.md) — both published `osx-arm64` on the Mac | ⚠ Easy to forget. The RBAC re-seed and the tenant-restore path both need a **Linux publish**, or the first deploy that adds a permission has no tool to run |
| 9 | **ICU** | the OS | The backend does **not** set `InvariantGlobalization` (only the two tools do), so it needs the host's ICU libraries. Present by default on Debian/Ubuntu; ⚠ **on Alpine you must install `icu-libs` AND publish `linux-musl-x64`** |
| 10 | **Caddy's upstream** | `reverse_proxy` → `127.0.0.1:5100` | Becomes the Linux box's LAN address under topology A. One line, and it is also the rollback |

**Not moving, and worth saying:** the schema (MySQL is MySQL), EF migrations, every API contract, the
web till, the portal, the MAUI till, and the Plutus Till Agent (`win-x64`, on the till PC).

---

## 4. The plan

⚠ **Rehearse on a throwaway box first.** Every step below is reversible, but the point of a rehearsal
is to find the ICU/socket/env problems without a shop waiting.

**Phase 0 — prove it runs at all** (half a day, no risk to anything live)

1. `dotnet publish -c Release -r linux-x64 --self-contained true`, tar, copy to the Linux box.
2. `chmod +x Plutus.DBService`, point it at a **restored copy** of the database (never the live one),
   set the connection string + `TEST_TOKEN_SECRET` + `ASPNETCORE_URLS` in the environment, run it in
   the foreground.
3. Watch it start. This is where ICU, a bad RID and a missing secrets file show up — all loudly.
4. Run the [§5](#5-verification--nothing-is-done-until-these-pass) probes against it directly on
   `:5100`, with no Caddy in front.

**Phase 1 — decide and prove the database channel** ([§1](#1--the-real-problem-the-mysql-auth-channel))

5. Implement the chosen option against the restored copy. **Restart mysqld to cold the cache, then
   connect again.** A connection that only works while the cache is warm is not proof.

**Phase 2 — supervision and edge**

6. Write the systemd unit (or port the pm2 ecosystem file): env, `Restart=always`, a dedicated
   non-login user, `WorkingDirectory` at the extracted folder.
7. Confirm it survives `systemctl restart` **and a reboot** — pm2's resurrect behaviour is not
   something to assume you have reproduced.
8. Repoint Caddy's `reverse_proxy` at the new host. ⚠ **Check ETRIE still answers 200 immediately
   after touching the Caddyfile** — it shares that edge.

**Phase 3 — cut over**

9. Take a fresh verified dump. Note the rollback: the Mac's `~/PLUTUS/backend` folder and its pm2
   app stay in place, stopped, untouched.
10. Stop the Mac's `plutus-backend`. Start the Linux service. Flip Caddy.
11. Run every [§5](#5-verification--nothing-is-done-until-these-pass) probe.
12. **Roll back by flipping Caddy back and starting pm2** — one line and one command, which is why
    the old install is left in place rather than deleted.

**Phase 4 — the operational tail** (the part that gets skipped)

13. Move the nightly backup + its cron, and prove it **both ways**.
14. Publish the two side tools for Linux.
15. Update [`../repo-runbook.md`](../repo-runbook.md): the RID, the supervision commands, the socket
    path, and the deploy sequence. ⚠ **A runbook describing a host you no longer run is worse than no
    runbook**, because it will be followed at 2am.
16. Update `HANDOVER.md` §1 and the memory note `plutus-test-environment.md`.

---

## 5. Verification — nothing is done until these pass

The runbook's existing probes, and they are the right ones:

| Probe | Pass |
|---|---|
| `GET /api/v1/ping` | reports the **expected `apiVersion`** — see the `0.0.0` trap in [§6](#6-traps--each-one-already-cost-somebody-a-day-here) |
| ⚠ **`POST /api/v1/tokens/device`** with a junk id, field `clientSecret` | **401 "Device not enrolled or revoked."** — this is the **DB-path probe**: a **500** means the schema and the model disagree, and a wrong payload shape gives a misleading 400 |
| `GET /swagger/v1/swagger.json` | 200 — ⚠ **and it proves almost nothing**: it touches no database and answered 200 throughout the 2026-08-09 outage in which every till got a 500 |
| ⚠⚠ **ETRIE health** (`huggett.dscloud.me/health`) | **200**, before and after every edge change |
| Till + portal hosts | 200 **and the right bundle** — check size or content, not the status code: both have an SPA fallback that 200s on anything |
| **A real sale, end to end** | Ring one up on a till and see it in the portal within ~60s. That exercises token mint → ingest → outbox → rollup, which no curl does |
| **The heartbeat** | a till's "last online" moves in Locations & Tills within a few minutes |
| **A cold-cache database connect** | restart mysqld, then confirm the backend reconnects ([§1](#1--the-real-problem-the-mysql-auth-channel)) |

---

## 6. Traps — each one already cost somebody a day here

- ⚠⚠ **The version will read `0.0.0` if you build from a flat copy.**
  [`Directory.Build.targets`](../../Directory.Build.targets) reads `versions/backend.txt` relative to
  the **repo root** and falls back to `0.0.0` when the file is absent. **This exact mechanism shipped
  every Mac-built web till and portal mislabelled for weeks**, because the Mac builds from flat copies
  with no `versions/` above them. **Build the backend from a real checkout**, and check `ping`'s
  version rather than trusting the build.
- ⚠ **The publish overwrites `appsettings*.json`.** Hash-compare against the live copies before
  swapping. The real connection string living in the process env is a **convention, not a guarantee**.
- ⚠ **Linux filesystems are case-sensitive; macOS's default is not.** Nothing obvious turned up, but
  this class of fault cannot be cleared by reading code — it shows up as a file that "exists" on the
  Mac and not on Linux. Phase 0 is the check.
- ⚠ **`pm2 restart <name> --update-env` does not load new keys from the ecosystem *file*.** If you
  keep pm2, restart from the file path when env keys change. The systemd equivalent is
  `daemon-reload`, and forgetting it fails the same silent way.
- ⚠ **Extract to `backend.new` and only then swap.** A tarball that fails to unpack must not be able
  to leave you with no `backend` directory at all.
- ⚠ **RBAC seeding does not run on deploy** — but `RolePermissionReconciler` **does run on every
  boot** (2026-08-11) and is additive-only, so a permission added in code reaches live tenants when
  the service starts. Do not "helpfully" run a stale `SeedMigrator` binary: it re-seeds the *old*
  permission set.
- ⚠ **A 200 from the till or portal host proves nothing** (SPA fallback). Check size or content.
- ⚠ **Do not run [`ops/keycloak/run-keycloak.sh`](../../ops/keycloak/run-keycloak.sh)**, on either
  host, ever.

---

## 7. What must not change

**⚠⚠ The public hostname. This is the one that would hurt.**

Every enrolled MAUI till stores its server address at enrolment
(`EnrolmentFlow.cs:77` → `MetaKeys.ServerUrl`) and the app's compiled default is
`TillConnection.cs:22` — `https://plutus.huggett.dscloud.me`. Nothing on the platform can change a
till's address remotely: **placement refreshes on every start, the server URL does not.** So a
hostname change means **visiting every till** (Plutus tab → server URL) or shipping a new build with
a new default, and until then a till points at a host that no longer answers while looking perfectly
healthy offline.

**Keep the hostname and the whole till estate is a no-op.** The web till is same-origin, so it follows
Caddy automatically.

Also unchanged, deliberately: **ETRIE** ([§2](#2-decide-the-topology-first)), the API contracts, and
the database schema.

---

## 8. Open decisions — Matt's call

1. **Why move?** The plan differs by motive. Escaping macOS as a server → **A**. Leaving the Mac mini
   behind → **B**. Consolidating everything → **C**, and read its warning first.
2. **Does MySQL move too?** This is [§1](#1--the-real-problem-the-mysql-auth-channel), and it is the
   only genuinely consequential question here.
3. **systemd or keep pm2?** systemd is the better fit; pm2 is the thing you already know. Either
   works.
4. **Container or tarball?** There is no Dockerfile today. A container would make the host almost
   irrelevant and give this exact question a permanent answer — perhaps half a day on top, and it
   changes the deploy procedure, so it is a decision rather than a freebie.
5. **x86-64 or ARM?** Genuinely does not matter to this code. Pick on price and what you want to
   administer.

⚠ **What is NOT a reason to hesitate: the code.** There is nothing to port, nothing to rewrite, and
no ARM dependency. If this stalls, it should stall on the database-channel decision, not on the
backend.
