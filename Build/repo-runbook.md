# Repo runbook — read before writing code

Standing conventions for working on Plutus: how to build, test, migrate and deploy, plus the
codebase pitfalls that have each cost a session at least once. Extracted from
`operator-portal-plan.md` §0 when that plan was archived, because it outlived the plan.

**Companions:** [`HANDOVER.md`](../HANDOVER.md) is the living state of the project (what's live,
what's open, rollback tags). [`plutus-platform-architecture.md`](plutus-platform-architecture.md)
wins on any design conflict. [`table-standard.md`](table-standard.md) governs every data table.
**[`till-design.md`](till-design.md) is the single source of truth for every till build** — read it
before any till work and update it in the same commit; **its C2 (the drift register) before writing
anything that computes money on a client.**

---

## Environment

Windows dev box; backend .NET 10 (`"C:\Program Files\dotnet\dotnet.exe"`, SDK 10.0.302). **No Node
on Windows** — the frontends build on the Mac test server over SSH. Work on branch
`Matt's-Horror`; push to `upstream` (seank842/Plutus): `git push upstream "HEAD:Matt's-Horror"`.
`origin` (LT-Mhuggett/Plutus) is parked on the net8 line — don't push there without coordinating.

## Build / test (from repo root, Git-Bash syntax)

```bash
DOTNET="/c/Program Files/dotnet/dotnet.exe"
"$DOTNET" build Plutus/Endpoints/Plutus.DBService/Plutus.DBService.csproj -c Debug --nologo -v q
"$DOTNET" test tests/Plutus.Tests.Unit/Plutus.Tests.Unit.csproj -c Debug --nologo
"$DOTNET" test tests/Plutus.Tests.Integration/Plutus.Tests.Integration.csproj -c Debug --nologo
"$DOTNET" test tests/Plutus.Tests.Architecture/Plutus.Tests.Architecture.csproj -c Debug --nologo
```

The current green baseline is recorded at the top of `HANDOVER.md` (Unit 333 · Architecture 6 ·
Integration 63 as at 2026-07-31). Keep it green — take the number from HANDOVER, not from here.

⚠ `Plutus.Entities.Tests` and `Plutus.Repository.Tests` (the two legacy projects) fail without a
live MySQL. That is pre-existing, not a regression.

## EF migration (exact incantation — `dotnet` must be on PATH for the ef tool)

```bash
export PATH="/c/Program Files/dotnet:$PATH"; export DOTNET_ROOT="/c/Program Files/dotnet"
"$DOTNET" ef migrations add <Name> \
  --project Plutus/Commons/Plutus.Entities \
  --startup-project Plutus/Data/Database.Migrations.Startup \
  --context MySqlDbContext -o Migrations/MySql
```

The 8.0.10-vs-9.x version warning is expected. `ef migrations remove` needs a live DB — don't;
hand-edit the migration + snapshot if you must undo. **Migrations auto-apply on backend startup**,
so dump the database before deploying anything that carries one.

⚠⚠ **AND CHECK THE DUMP IS NOT EMPTY — ON 2026-08-11 EVERY NIGHTLY BACKUP HAD BEEN 20 BYTES FOR TWO
DAYS AND THE LOG SAID `backup ok`.** Two faults, and it needed both to stay hidden:

1. **`plutus-nightly-backup.sh` connected over plain TCP** (`-h 127.0.0.1`). Rotating the
   `caching_sha2_password` account on 2026-08-09 broke every plain-TCP client — which is why the
   *backend* moved onto the unix socket that night. The backup script did not. Last good dump
   08-09; 0 bytes from 08-10.
2. **It could not tell.** `mysqldump | gzip && mv` takes its status from **gzip**, which succeeds on
   empty input — so `mv` ran, a 20-byte file landed, and the log recorded success.

⚠ The 7-day prune would then have deleted the last good dump on 2026-08-16, leaving nothing: the
deletion and the corruption driven by the same clock. Fixed with `pipefail`, `--socket=/tmp/mysql.sock`,
a **1 MB floor** (an empty gzip is 20 bytes and an empty-schema dump is a few hundred — "non-empty"
is not a test), and pruning only after a verified-good dump. Source in `ops/mac/`.

**Before any migration deploy:** run `zsh ~/PLUTUS/bin/plutus-nightly-backup.sh`, then check the size
— `gzip -dc ~/PLUTUS/backups/nightly/plutus-$(date +%Y%m%d).sql.gz | wc -c` should be ~60 MB and
`grep -c "CREATE TABLE"` ~100 (84.6 MB / 107 tables as of 2026-08-25). ⚠ **NEVER run it under
`zsh -x`** — that printed the password into a transcript on 2026-08-09 and burned the credential.

⚠ **TWO CHECKS THE SIZE FLOOR CANNOT MAKE, both worth making before you trust a dump** (2026-08-25):

1. **The completion sentinel.** `gzip -dc <dump> | tail -3 | grep -c "Dump completed on"` must be
   **1**. mysqldump writes that line only on a clean finish, so it is the one cheap test for a dump
   **truncated mid-table** — which still gzips fine, still passes `gzip -t`, and still sails past the
   1 MB floor. The nightly script does not check this yet.
2. **Restore-verify, don't eyeball.** Restore into `plutus_t1` (that is what it is for; nothing
   references it — 0 hits in the ecosystem config) and diff **exact** row counts per table:
   ```bash
   gzip -dc <dump> | mysql -u plutus -p"$MYSQL_PLUTUS_PASSWORD" --socket=/tmp/mysql.sock plutus_t1
   # then COUNT(*) every base table in both schemas and diff the two sorted lists
   ```
   Expect the **append-only telemetry tables to differ** — `JobRuns`, `TenantRequestStats`,
   `WebstoreOutboundLogs` grow *while the dump runs*. Every business table must match exactly, and
   `SUM(VatPence)` on `SalesV2` must equal `VatRollups` in both copies.
   ⚠ Build that per-table SQL in the shell, **not** with `GROUP_CONCAT` — 107 table names overflow
   the default 1024-byte `group_concat_max_len` and you get silently truncated, invalid SQL.
   ⚠ `information_schema.TABLE_ROWS` is an InnoDB **estimate** — useless here; use `COUNT(*)`.

⚠ **There is no app file storage** (checked 2026-08-25): no uploads/images/media/attachments
anywhere under `/srv/apps/PLUTUS` or `~/PLUTUS` — every `*receipt*` directory is an old frontend
rollback copy. **`plutus` + Keycloak is the complete set of irreplaceable state**, so a verified DB
dump plus the Keycloak realm really is a full backup.

⚠ **OPEN THE GENERATED MIGRATION AND READ ITS `Up()`.** On 2026-08-09 a deploy took every till on
the estate offline because `AddDeviceSyncSignals` — named for three `Device` columns — contained
only a `CreateIndex`. The properties had already reached `MySqlDbContextModelSnapshot`, so EF
correctly had nothing left to emit; the columns were simply never created by anything. EF then
built its `SELECT` from the model and MySQL answered `Unknown column 'd.LockReason' in 'field
list'`, which 500s `POST /api/v1/tokens/device` — **no till can get a token**. If a migration's
body does not match what you just changed, the snapshot already believes the work is done, and you
need a hand-written catch-up migration (see `20260809003000_AddDeviceLockAndSyncColumns`).

⚠ **After deploying a migration, verify the COLUMNS, not the history table.** A green
`__EFMigrationsHistory` proves a migration ran, never that it did what its name says:
`SELECT COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA='plutus' AND TABLE_NAME='…';`

## Frontend build (on the Mac)

`ssh -i ~/.ssh/plutus_mac_ed25519 admin@10.1.1.40`. The remote shell is **zsh**: no unquoted
word-splitting, `UID` is reserved and readonly, and `export PATH=/opt/homebrew/bin:$PATH` must come
first — node/npm/pm2/docker/mysql are not on the default non-interactive PATH.

⚠ **The Mac source directories are not git checkouts.** They are hand-synced with `scp`; there is
no `git pull` step. Copy the changed files across before building.

```bash
cd ~/PLUTUS/Plutus.Frontend.Portal && VITE_AUTH_MODE=oidc \
  VITE_OIDC_AUTHORITY=https://login.plutus.huggett.dscloud.me/realms/plutus \
  PLUTUS_APP_VERSION=1.5.0 VITE_OIDC_CLIENT_ID=plutus-portal npm run build   # portal: OIDC env REQUIRED
cd ~/PLUTUS/Plutus.Frontend.WebApp && PLUTUS_APP_VERSION=1.6.0 npm run build # till: password mode, no OIDC env
```

⚠⚠ **`PLUTUS_APP_VERSION` IS NOT OPTIONAL ON THE MAC, and omitting it ships `0.0.0`.** Both configs
read the version from `../../../versions/<app>.txt`, which resolves inside a **checkout** — and the
Mac's `~/PLUTUS/Plutus.Frontend.*` trees are **flat copies with no `versions/` above them**, so the
read throws and the `catch` returns `0.0.0`. Every web-till and portal build did this until
2026-08-11: Matt found both web tills reading **v0.0.0** in the portal's Locations & Tills list while
the MAUI till (built inside the repo, on Windows) correctly read v1.45.0. Take the number from
`versions/till-web.txt` / `versions/portal.txt` — they are still the source of truth.

⚠ **The build now WARNS on stderr** when it cannot resolve a version, because the old signal was too
quiet: `0.0.0` was supposed to read as "did not come from the release process", and instead it sat in
a settings panel for days. If you see that warning, the number in the bundle is wrong.

⚠ **Verify the version got in, not just that the build succeeded** —
`grep -c "<version>" dist/assets/index-*.js` before deploying. A bundle labelled 0.0.0 builds, serves
and looks perfect.

Deploy the built assets with `rsync -a --delete dist/ /srv/apps/PLUTUS/{portal,web}/current/`,
taking a `current.pre-<tag>` copy first. Caddy serves them statically.

⚠⚠ **THE PORTAL IS `admin.plutus.huggett.dscloud.me`. `plutus.huggett.dscloud.me` IS THE WEB TILL.**
From `/etc/caddy/Caddyfile`: `plutus.…` → `/srv/apps/PLUTUS/web/current`, `admin.plutus.…` →
`/srv/apps/PLUTUS/portal/current`. Verifying a portal deploy against `plutus.…` checks the till and
tells you nothing — hit on 2026-08-11, where a clean portal deploy "verified" against a bundle hash
belonging to a different application.

⚠ **AND A 200 FROM EITHER HOST PROVES ALMOST NOTHING**, because both have an SPA fallback: every
unknown path returns `index.html` with a **200**. Asking for a bundle you have just deleted still
answers 200. **Check the SIZE, or check the content**: the fallback is ~1 KB, a real bundle is
~490 KB. The reliable verification is three parts — the hostname is right, `curl / | grep -o
"index-[A-Za-z0-9_-]*\.js"` names the hash you just built, and grepping that bundle finds a string
only your change introduced.

## Backend deploy

Publish `-c Release -r osx-arm64 --self-contained true`, tar, `scp` to `~/PLUTUS/staging/`, then on
the Mac: pm2 stop → `mv ~/PLUTUS/backend ~/PLUTUS/backend.pre-<tag>` → extract → `chmod +x
backend/Plutus.DBService` → `pm2 restart plutus-backend --update-env` → poll
`curl -s -o /dev/null -w "%{http_code}" http://127.0.0.1:5100/swagger/v1/swagger.json` = 200.

⚠ **Extract to `backend.new` FIRST and only then swap** — a tarball that fails to unpack must not
be able to leave you with no `backend` directory at all.

⚠ **`/swagger` = 200 IS NOT A DEPLOY VERIFICATION.** It touches no database, so it answered 200
throughout the 2026-08-09 outage in which every till was getting a 500. Always also probe an
endpoint that reads a table — `POST /api/v1/tokens/device` with a junk id is ideal: **401 "Device
not enrolled or revoked."** means the DB path is healthy; a **500** means the schema and the model
disagree.

⚠ The publish overwrites `appsettings*.json`. Compare hashes against the Mac's copies first — the
connection string lives in the pm2 env, not in the file, but that is a convention, not a guarantee.

⚠ ~~**RBAC seeding does NOT run on startup.**~~ **CORRECTED 2026-08-13 — IT DOES, and this stale
line cost a step.** `RolePermissionReconciler` is a registered hosted service (`IdentityModule.cs:27`)
that runs **once per boot**, additively and idempotently, and exists precisely because *"a forgotten
step whose failure is a polite refusal is a step that gets forgotten"*. So **a deploy that adds a
permission needs nothing extra** — grants for `pos.customers.add` were already in both tenants'
`RbacRoleGrants` before the seeder was run by hand.

⚠ **The tool still exists and is still right for an out-of-band run** (a database the backend has not
booted against): `Plutus.SeedMigrator rbac --mysql "…"` from a **freshly published** SeedMigrator —
`RbacSeeder` compiles into it, so a stale binary re-seeds the old permission set.

⚠⚠ **But `rbac` is NOT only the permission grants — it also runs `MapKapowAuthActionsAsync`**, which
maps legacy `AuthActions` onto role *assignments* for real users. Running it "just for the grants" on
2026-08-13 added **7 assignments** (all to one employee who already held `Owner`, so the net effective
change was nil — verified by diffing the new roles' codes against Owner's). It is additive and creates
no duplicates, but it is **not the no-op the header comment implies**, so do not reach for it casually
on a live database: check what it changed afterwards. ⚠ And use the **unix socket** in the connection
string — plain TCP fails since the `caching_sha2_password` rotation.

⚠ **A permission deploy still needs a sign-out/in**: login tokens cache for 12h with the user's full
effective permission set baked in (pitfall 10).

⚠ `pm2 restart <name> --update-env` does **not** load new keys from the ecosystem *file*. To pick
up new env keys, restart from the file path.

## Frontend deploy (portal / web till)

Both are built ON THE MAC (Node 26; the Windows box has none). Source is rsync/tar-synced, not a
git checkout — so **sync the whole project, never just `src/`**.

⚠ **`vite.config.ts` IS PART OF THE SOURCE.** On 2026-08-09 the portal was deployed with new `src`
against a two-day-old config, so `__APP_VERSION__` — a Vite `define` substitution declared in that
config — was never replaced. The result was `Uncaught ReferenceError: __APP_VERSION__ is not
defined` and a blank portal for every user.

⚠ **`tsc --noEmit` AND `vite build` BOTH PASSED.** A missing `define` is a *runtime* reference
error: TypeScript sees `declare const __APP_VERSION__: string` and is satisfied; Vite emits the
identifier untouched. Nothing but a browser finds it. So the deploy check is on the ARTEFACT:

```bash
# after building, BEFORE copying to current/
grep -rq "__APP_VERSION__\|__BUILD_TIME__" dist/assets/*.js && { echo "unsubstituted define — do not deploy"; exit 1; }
```

Then: back up `current` → `current.pre-<tag>`, clear, `cp -r dist/. current/`, and re-check the
same grep against the deployed bundle before declaring victory.

⚠ `appVersion()` resolves `../../../versions/portal.txt` **relative to the repo layout**. The Mac's
flattened copy has no such path, so it falls back to `"0.0.0"` — the footer version is cosmetic
there and is not evidence of a bad build.


### ⚠⚠ THE PORTAL'S BUILD ENV — the three variables, and how to prove you have them right

**Two of them are load-bearing and silently absent if you forget them:**

```bash
export PLUTUS_APP_VERSION=$(cat versions/portal.txt)
export VITE_OIDC_AUTHORITY=https://login.plutus.huggett.dscloud.me/realms/plutus
export VITE_OIDC_CLIENT_ID=plutus-portal
npm run build
```

⚠ **`VITE_AUTH_MODE` changes NOTHING in the artefact** — measured 2026-08-23 by building with and
without it and comparing the bundles byte-for-byte after normalising `__BUILD_TIME__`: **identical**.
`oidc.ts` reads `import.meta.env.VITE_AUTH_MODE` at runtime rather than as a `define`, so it is not
folded at build time. Do not spend time on it; do not cite it as a reason a build differs.

⚠⚠ **FORGETTING THE TWO `VITE_OIDC_*` VARS PRODUCES A BUILD THAT PASSES EVERY GATE AND IS WRONG.**
`tsc --noEmit` passes, `vite build` passes, the `__APP_VERSION__` grep passes, the version string is
present — and `AUTHORITY`/`CLIENT_ID` are `""` (they are `?? ""` fallbacks in `oidc.ts:30-31`), so
Keycloak sign-in has nowhere to go. It happened on 2026-08-23 and was caught only by comparing
against the deployed bundle.

**The check that catches it** — the deployed bundle contains the authority string TWICE when the vars
were set and ONCE when they were not (the single one is `ACCOUNT_CONSOLE`, hardcoded in `App.tsx`):

```bash
grep -c 'realms/plutus"' dist/assets/index-*.js     # 1 = the vars were set; 0 = they were NOT
```

#### ⚠ Proving a build matches the deployed one, when you do not know how the deployed one was built

Build the **unmodified HEAD source** at the **deployed version number**, normalise the timestamp out
of both, and `cmp`. Identical means your env matches; different means it does not, and the size delta
points at what is missing (66 bytes was the two OIDC values).

```bash
sed -E 's/20[0-9]{2}-[0-9]{2}-[0-9]{2}T[0-9:.]+Z/TS/g' dist/assets/index-*.js > /tmp/mine.n
sed -E 's/20[0-9]{2}-[0-9]{2}-[0-9]{2}T[0-9:.]+Z/TS/g' /srv/apps/PLUTUS/portal/current/assets/index-*.js > /tmp/live.n
cmp /tmp/mine.n /tmp/live.n && echo "same env"
```

⚠ **The web roots are `/srv/apps/PLUTUS/portal/current` and `/srv/apps/PLUTUS/web/current`** — NOT
under `~/PLUTUS`, which holds the *source* trees (`~/PLUTUS/Plutus.Frontend.Portal`). ⚠⚠ `/srv/apps/`
also holds **ETRIE**, which must never be touched.
### ⚠⚠ "Is what is DEPLOYED what is in the TREE?" — the bundle hash cannot answer that

`vite.config.ts` defines `__BUILD_TIME__: JSON.stringify(new Date().toISOString())`, so **every build
of identical source produces a different bundle and therefore a different content hash.** Rebuilding
and comparing `index-<hash>.js` names will always differ, which reads as *"the deployed bundle is
stale"* when it is nothing of the kind. (Encountered 2026-08-20 — two seconds of believing a clean
deploy had drifted.)

The hash IS the right check for *"did the file I just built reach `current/`"* — same build, same hash.
It is the wrong check for *"does `current/` match today's source"*. For that, normalise the timestamp
and compare the bytes:

```bash
norm() { sed -E 's/[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]{3}Z/BUILDTIME/g' "$1"; }
norm /srv/apps/PLUTUS/web/current/assets/index-*.js > /tmp/a
norm ~/PLUTUS/Plutus.Frontend.WebApp/dist/assets/index-*.js > /tmp/b
cmp /tmp/a /tmp/b && echo "deployed matches current source"
```

⚠ **The CSS has no timestamp**, so for stylesheets the hash IS a valid source-equality check — a
matching `index-<hash>.css` means the deployed CSS is exactly what the tree builds.

⚠ **And write these loops out longhand: the remote shell is zsh, which does NOT word-split an unquoted
variable.** `for p in "web App" ...; set -- $p` silently produces one argument containing a space, so a
path becomes `~/PLUTUS/ /dist` and every check reports 0 / DIFFERS / "no matches found". That failure
looks exactly like a failed verification rather than a broken script, and it happened **twice** on
2026-08-20 — once while checking bundle contents and once while checking the CSS.

## MAUI till build (Windows)

> ⚠⚠ **BUILD ONE ONLY WHEN MATT ASKS.** Matt, 2026-08-16: *"Can you only deploy new MAUI tills when I
> ask please. When doing a lot of change, you might deploy 5 and I only test the latest."*
>
> Keep bumping `versions/till-maui.txt` per slice and keep committing — that history is the release
> record and it is right. **Do not run the publish below** until he asks for a build, or says he is
> about to test. A queue of artefacts nobody asked for is churn, and worse, it implies a testing
> history that does not exist.
>
> When he does ask: build **current HEAD**, verify the artefact's version, **delete superseded
> builds** so there is no ambiguity about which to run, and point `Build/Test Maui.md`'s *Run* line
> at it. ⚠ Keep that script up to date as work lands regardless — it is cheap, and it is what makes
> a later hand-run possible at all.

⚠ **The Release MSIX is UNSIGNED and will not install.** `Configuration != Debug` sets
`WindowsPackageType=MSIX`, but nothing in the project configures a signing certificate — so
`publish` produces `AppPackages\…\*.msix` with no `.cer` beside it, and Windows refuses it with
*"The package or bundle is not digitally signed or its signature is corrupted"*. `Install.ps1`
cannot help: it trusts a certificate that was never generated.

**To test a build, go unpackaged** — no certificate, no admin, no install:

```bash
dotnet publish Plutus/Frontend/Plutus.Frontend.AppClient/Plutus.Frontend.AppClient.csproj \
  -c Release -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None -o <folder>
# then run <folder>\Plutus.Frontend.AppClient.exe
```

⚠ **Unpackaged and packaged do NOT share data.** `FileSystem.AppDataDirectory` resolves to a
per-package virtualised path when packaged and an ordinary AppData path when not — so an unpackaged
build sees no enrolment, no catalogue and no outbox from a previously installed MSIX. That is
usually what you want when testing (it exercises the fresh-till path), but it means "it says the
till isn't enrolled" is expected rather than a bug.

⚠ **Version stamping.** `versions/till-maui.txt` → `ApplicationDisplayVersion` (via
`Directory.Build.targets`) → the assembly's informational version, which is what the heartbeat
sends. It is NOT `AppInfo.VersionString`: that reads the package manifest when packaged, and the
manifest is deliberately `0.0.0.0` so the build can substitute `<display>.<ApplicationVersion>`.
A real version hardcoded there wins over the build property — which is how every MSIX shipped as
1.0.0.0, meaning Windows saw no version change and a reinstall was not an upgrade.

⚠ **MSBuild caches the evaluated version.** Bumping `versions/till-maui.txt` and re-publishing can
still emit the previous package name; delete `bin/Release` + `obj/Release` for a version bump.

Signing the MSIX properly (a cert in the store + `PackageCertificateThumbprint`) is open work — it
is needed before anyone installs this on a shop PC, and is not needed to test.

## Hard rules

- **NEVER touch ETRIE.** It shares the Mac mini but is a separate product. After any Mac change,
  verify `curl -s -o /dev/null -w "%{http_code}" -L --resolve huggett.dscloud.me:443:127.0.0.1
  https://huggett.dscloud.me/health` → 200.
  ⚠ **`-L` ADDED 2026-08-19, and without it this check now LIES.** The bare host answers **302** to
  `https://etrie.huggett.dscloud.me/health`, which is the 200. Read as written, the un-redirected 302
  looks like ETRIE is broken — I hit exactly that mid-deploy and stopped to investigate a healthy
  service. ⚠ Follow the redirect; do not "fix" it by treating 302 as success, because a 302 to
  somewhere else would then pass too.
- **NEVER run `ops/keycloak/run-keycloak.sh`** — it recreates the container and wipes the enrolled
  password/TOTP. Keycloak changes go via `kcadm.sh` inside the running container.
  ⚠⚠ **ROOT CAUSE, established 2026-08-25: `plutus-keycloak` HAS NO VOLUME.** Its only mount is the
  realm-import JSON, so the H2 database holding every operator account, group and TOTP secret lives
  in the **container's writable layer**. `docker rm`, an image bump, or any re-import therefore
  destroys operator login outright — and the committed `ops/keycloak/plutus-realm.json` is the
  stripped 2026-07-29 state (**7 KB against 63 KB live**), so re-importing it does NOT bring the
  accounts back. There is no undo.
  ⚠ A verified backup now exists — `~/PLUTUS/backups/keycloak-20260825/` (also off-machine at
  `D:\Backups\Plutus\2026-08-25\`). It holds the H2 file **and** a portable realm export carrying all
  3 users incl. `matt` with `password`+`otp`. Proven by exporting from the copy offline in a
  throwaway container (`Export finished successfully`), because a file-level copy of a *live* H2 is
  not self-evidently consistent.
  ⚠ `kc.sh export` CANNOT run against the live container — H2 holds an exclusive file lock
  (`Database may be already in use`). Copy the file out and export from the copy.

  **THE FIX — volume `plutus-keycloak-h2`. Prepared and verified 2026-08-25; ONE STEP OUTSTANDING.**
  - The volume exists, holds the current H2, and is owned **1000:0**. ⚠⚠ That ownership is not
    optional: `/opt/keycloak/data/h2` does not exist in the image, so Docker creates the volume
    **root-owned**, and Keycloak runs as uid 1000 — without the chown the container will not start.
  - `ops/keycloak/run-keycloak.sh` now creates the volume, chowns it, and mounts it, so the script
    that used to be a footgun is the mechanism. Synced to the Mac.
  - ⚠ **STILL TO RUN, needs a human: `zsh ~/PLUTUS/bin/kc-move-to-volume.sh`.** Adding a volume
    means recreating the container, so it stops the live IdP (~30s of no operator SSO). It renames
    the old container to `plutus-keycloak.pre-volume` rather than removing it, so rollback is
    instant. **Until it runs, H2 is still in the container layer and the gap is still open.**
  - ⚠ **Then prove it**: `docker rm -f plutus-keycloak && zsh ops/keycloak/run-keycloak.sh`, and log
    in as `matt` with the existing TOTP. A volume you have not tested losing the container is a
    guess.
  - ⚠ **A SECOND THING WOULD HAVE BEEN LOST, found while doing this**: the realm sets
    `loginTheme: plutus`, and on the pre-2026-08-25 container those theme files sat in the
    **writable layer with no mount at all** (`HostConfig.Binds` listed only the realm json). A naive
    recreate would have silently unbranded the login page as well as wiping the accounts. Both
    scripts now bind-mount `ops/keycloak/themes/plutus`; host and container copies were verified
    byte-identical first.
  - ⚠ `start-dev` + H2 remains dev-mode Keycloak. Moving to Postgres (already on that host for
    other stacks) is the proper answer and a separate job — the volume closes the data-loss hole,
    not that one.
- ⚠ **`ops/keycloak/plutus-realm.json` has DRIFTED from the Mac's copy** (repo 8853 bytes vs Mac
  7162 — different content, and the Mac's is the one bind-mounted and imported). Reconcile before
  any re-import. ⚠ Do **not** reconcile by committing a real realm export: exports embed password
  hashes and TOTP secrets and must never enter git. Account recovery comes from the backup.
- Deploy only when the operator asks. Commit per work-package, with the `Co-Authored-By: Claude`
  trailer.

## Codebase pitfalls (each has burned a session before)

1. Every `SaveChangesAsync` on `MySqlDbContext` requires `db.CurrentUser = "<something>"` first,
   or it throws `ObjectIdMissingException: CurrentUser not defined!`. **This includes background
   jobs** — four separate outages have come from a job that didn't set it.
2. Seeding a `Tenant` in tests requires `Entitlements = "[]"` **and** `ConnectionRef = ""` (both
   NOT NULL).
3. `StampAndGuardTenant` blocks cross-tenant writes. In integration tests either write as Kapow
   (`Plutus.Entities.Tenancy.KnownTenants.Kapow`, the ambient fallback) or build an unscoped
   context: `new MySqlDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<MySqlDbContext>>(),
   new FixedTenantContext(Guid.Empty))`.
4. New tenant-owned entities need a real `Guid TenantId` property, the CLR type added to the
   `TenantOwned` array in `MySqlDbContext.cs` (~line 150), and an entity config block. Global
   tables must **not** go in that array — their `TenantId` (if any) is plain data.
5. `perm:*` policies resolve from RBAC by userId, **not** from token scopes. Test recipe: seed
   `RbacSeeder.EnsureBuiltInRolesAsync(db, Kapow)`, assign a role ("Owner" carries every portal
   permission), then mint `PlutusAppFactory.OperatorTokenFor(userId, "pos.sell")`. See
   `tests/Plutus.Tests.Integration/CommerceConfigE2eTests.cs` for a complete example.
6. `PlutusAppFactory` is the integration host (shared in-memory SQLite; hosted services removed).
   Platform-admin token: `PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin)`.
7. Secrets in config JSON are write-only via the `"__set__"` sentinel pattern — copy
   `PlatformNotificationsController` exactly if you need it.
8. **Legacy CRUD controllers bind EF entities directly.** `LegacyEntityValidationMetadataProvider`
   (in `Plutus.Web.Infrastructure`, wired in `ConfigureControllers`) strips MVC's *inferred*
   `[Required]` from their navigation properties, collections and server-owned audit stamps —
   without it every item edit 400s demanding `Cat`, `Tax`, `CreatedBy`. Explicit `[Required]` still
   applies. Don't "simplify" this into the global suppression switch.
9. Frontend: pages render as components through the `PAGES` map (never call a page function
   inline); money renders via `gbp()` on integer pence; append `"Z"` to a date string before
   `new Date(...)`.
10. **Login tokens are cached for 12h.** They carry the user's full effective permission set, so
    after deploying anything permission-related you must sign out and back in to see the change.

### MAUI till — the UI pitfalls (all four cost the 2026-08-10 session)

11. **`App.SetLoading(true)` pushes a MODAL PAGE.** Never raise it immediately before, during or
    from inside a navigation — including from a destination page's `OnAppearing`, which fires while
    the push is still transitioning. MAUI does not serialise the modal and navigation stacks, and
    the WinUI handler resolves the collision into a **corrupted layout**: content drawn over the tab
    bar, at the wrong size, with no way back out. ⚠ And a screen must **own its own spinner** — the
    old pattern had the opening screen raise it and the opened screen lower it, so a screen that
    failed to load left the overlay over the whole app for the session.
12. **`VisualElement.Width` / `.Height` are `-1` until the element has been arranged.** Sizing
    anything from them on a first layout pass gives you negative requests. `InputAlert` did
    `Application.Current.MainPage.Width / 2` and asked for **-0.5**. Size from the values
    `OnSizeAllocated` passes in, and guard `> 0`.
13. **Every modal dialog needs a way out, and the trap is built from parts that each look fine.**
    The cash-payment dialog passed no `cancelText` (so no Cancel button was built),
    `interuptable: false` (so background clicks were refused) and inherited an
    `OnBackButtonPressed` returning `true` (so Escape was swallowed). Three reasonable decisions,
    one screen you could only leave by killing the process. **Check the combination.**
14. **`AlertDialogBase.PageClosedTaskCompletionSource` is created ONCE.** Never loop
    `while (empty) { push; await PageClosedTask; pop; }` — the second pass awaits an
    already-completed task and spins the UI thread at full speed. One push, one await, one pop in a
    `finally`, and `TrySetResult` (four code paths can fire the confirm handler). ⚠ Cancelling now
    completes with `default` — **null** for reference types, so null-check the result.

15. ⚠ **An endpoint whose job is to WRITE needs a test that reads the row back.** Asserting the
    *response* is not enough and is how a missing write hides — everything the caller can see is
    correct. `HeartbeatController` assigned `device.AppVersion` and called `SaveChangesAsync` only
    inside its `if (syncNow)` branch, so on every ordinary beat the mutation was tracked and thrown
    away with the DbContext. Six real devices beat for two days reporting `AppVersion NULL` while
    their sales arrived perfectly; the E2E test passed throughout, because it only ever looked at
    the 200. ⚠ Watch for a `SaveChangesAsync` nested inside a conditional that is narrower than the
    set of things it needs to save.

16. ⚠ **CHECK WHICH LOG YOU ARE READING.** `CrashLog` falls back to the system temp directory when
    there is no MAUI app host — which is every `dotnet test` run — and until 2026-08-10 it used the
    SAME filename as a real till's log. `%TEMP%\plutus-till-<date>.log` therefore accumulated
    thousands of lines of TEST output that reads exactly like production. Sixty deliberate
    `ParkedBasket.FromJson` JSON errors (a test feeds it bad JSON on purpose and the guard logs when
    it catches) were read as "parked baskets are broken on the live till", reported to the owner,
    and queued as the next fix. **The real till's log had none.** The test-host file is now named
    `plutus-NOT-A-TILL-testhost-*.log`; the till's own log lives under
    `FileSystem.AppDataDirectory\logs`. ⚠ **Tells for a test-host log**: errors in identical PAIRS,
    timestamps that match your own test runs rather than a shift, and
    `COMException: ClassFactory cannot supply requested class` from `MainThread`/`FileSystem`.

17. ⚠ **`AppShell` BUILDS EVERY TAB UP FRONT, so a viewmodel constructor runs ONCE — at sign-in.**
    Anything loaded there is frozen for the life of the session. `StatisticsViewModel` called
    `LoadToday()` from its constructor, so today's takings were fixed at the moment the operator
    signed in and a full day of trading never moved them. ⚠ **`OnAppearing` is not enough either**
    for anything that changes while the screen is up: the Cash tab showed "(waiting to send)"
    against money the outbox had already delivered, and the only way to find out was to leave the
    page and come back.

    ⚠⚠ **DO NOT HAND-ROLL IT ANY MORE — use `Services/Sync/LiveScreen.cs`.** Two lines in the page's
    constructor, no `OnAppearing` override, nothing to remember to unsubscribe:
    ```csharp
    private readonly Services.Sync.LiveScreen _live;          // keep it in a field
    _live = new Services.Sync.LiveScreen(this, _vm.Refresh);   // after BindingContext is set
    ```
    It hooks the page's public `Appearing`/`Disappearing`, refreshes on appearing, subscribes to
    `TillCadence.Ticked` while the page is up, unsubscribes on the way out, marshals onto the UI
    thread, and cannot throw.

    ⚠ **Pass `onCadence: false` for a long scrollable table.** A refresh that rebuilds an
    `ObservableCollection` sends a `CollectionView` back to the top, so ticking a 500-row item list
    would yank the page out from under somebody reading it — a worse fault than the staleness, and a
    self-inflicted one. Items, Loyalty and Reports are `false`; Cash, Statistics and Store
    Information are `true`.

    ⚠ **Why this is a shared class and not a documented pattern:** it was already documented as a
    pattern, right here, and the pattern is what failed. The Cash fix taught Statistics nothing,
    Statistics taught Store Information nothing — which had **no refresh at all** — and the same
    complaint arrived a third time as *"nothing updates unless you navigate away and back"*
    (§5c item 7). ⚠ **The dangerous one is the silent one.** A stuck "(waiting to send)" gets
    reported within the hour; a takings total eight hours stale looks exactly like a correct one.
    **When you find one stale screen, go and look for its siblings straight away** — and the sibling
    that bites is the one with no refresh code to notice.

    ⚠ **None of this is machine-testable.** A MAUI `Page` cannot be constructed in the test project
    at all (`BindableObject`'s constructor needs a live WinUI3 dispatcher — it is why three tests are
    skipped), so `LiveScreen` has no unit test and cannot have one. `Test Maui.md` **§G39** is the
    only check that exists.

18. ⚠⚠ **`.Translate()` ON AN ENGLISH SENTENCE CRASHES A DEBUG BUILD.** `TranslateExtension.ProvideValue`
    looks the string up as a **resource key**; when it misses it **throws `ArgumentException` in
    DEBUG** and silently returns the key itself in RELEASE. The XAML form `{i18n:Translate Foo}`
    behaves identically. So a missing key is invisible in every build we ship and fatal in the one
    developers press F5 on — and because these strings live in `DisplayAlert` calls, the crash lands
    on the money path, from an `async void`, mid-sale.

    ⚠ **I introduced 17 of these in one week and did not notice**, because the test path is a
    **Release** publish (see § MAUI till build) where the fallback hides it. `WhyThisDiscount`
    shipped in till 1.54.0 as a key that did not exist.

    **The rule this codebase already follows:** `.Translate()` takes a **short key** that exists in
    `Plutus/Shared/I18N_L10N/Resx/AppResources.resx` (`"Hmm"`, `"OK"`, `"HowMuchRefund"`). A new
    English sentence for an alert is passed **untranslated**, exactly as
    *"Something went wrong taking payment…"* already is.

    **Sweep for it before shipping — both forms:**
    ```bash
    R=Plutus/Shared/I18N_L10N/Resx/AppResources.resx
    A=Plutus/Frontend/Plutus.Frontend.AppClient
    # C# keys
    grep -rohP '"[^" ]+"\.Translate\(' --include=*.cs $A/ | sed 's/"\(.*\)"\.Translate(/\1/' | sort -u \
      | while read k; do grep -q "name=\"$k\"" $R || echo "MISSING $k"; done
    # XAML keys
    grep -rohP 'i18n:Translate \w+' --include=*.xaml $A/ | awk '{print $2}' | sort -u \
      | while read k; do grep -q "name=\"$k\"" $R || echo "MISSING $k"; done
    ```
    ⚠ A key **containing a space is always wrong** — that is a sentence, not a key.

19. ⚠ **`dotnet` on the PATH may be the x86 one, which has NO SDKs.** On this box
    `where dotnet` resolves to `C:\Program Files (x86)\dotnet\dotnet.exe`, and every SDK is installed
    under the x64 `C:\Program Files\dotnet\`. So `dotnet build` fails with **"No .NET SDKs were
    found"** and a download link — which reads like a broken machine rather than a wrong `dotnet`,
    and the same repo builds fine from a shell whose PATH happens to be ordered the other way.

    ```powershell
    (Get-Command dotnet).Source          # if this says "(x86)", that is the fault
    & 'C:\Program Files\dotnet\dotnet.exe' build …   # always works
    ```

    ⚠ **Use the absolute x64 path in any script that must not depend on PATH order.** Setting
    `DOTNET_ROOT` does not fix it: the x86 `dotnet.exe` is a different host and will not load an x64
    SDK. Cost: 2026-08-18, two failed builds before the cause was obvious.

20. ⚠⚠ **NEVER EDIT A SOURCE FILE THROUGH A PIPE THAT DOES NOT SET `binmode` / `-encoding`.** On
    2026-08-11 commit `c93e0fc2` rewrote three files through a filter that read their bytes as
    Latin-1/cp1252 and re-encoded them as UTF-8. Every `⚠` in `TillViewModel.cs` became
    `ÃÂ¢ÃÂÃÂ `, and it **shipped in every MAUI build for eight days** until Matt scanned an unknown
    barcode and got *"Nothing in the catalogue matches ÃÂ¢ÃÂÃÂ759606210602ÃÂ¢ÃÂÃÂ."* in front of a
    customer. `SourceEncodingTests` now fails the build on it — but the repair is the part worth
    writing down, because the obvious repair is wrong twice over.

    ⚠ **Detecting it.** The fingerprint is a **C1 control** (U+0080–U+009F, bytes `C2 80`–`C2 9F`),
    which legitimate text never contains. ⚠ Do NOT grep for what you SEE — the rendered `ÃÂ` contains
    invisible control characters, so a pattern typed from the screen silently matches nothing and reads
    as "clean":

    ```bash
    LC_ALL=C grep -c -a -P '\xc2[\x80-\x9f]' path/to/file      # the real detector
    LC_ALL=C grep -c -a -P '\xc3[\x82\x83](?:\xc2|\xc3)' file  # the C1-FREE class: a mangled BOM or accent
    ```

    ⚠⚠ **Do not repair with "reverse while it still looks mangled".** Two traps, both live in this repo:
    - **The depth VARIES** — 1, 2 and 3 rounds all existed inside one file, so a fixed number of
      reversals over-decodes the shallow ones into fresh garbage.
    - **The C1 test stops one round early** for any character whose own UTF-8 is `C2`/`C3 xx`:
      `·` (U+00B7) corrupted three times passes through `C3 82 C2 B7`, which holds no C1 control and is
      still wrong. And a blanket reversal **destroys correct characters** — a lone `C2 A3` is a `£` and
      `C3 97` an `×`, both of which appear here legitimately (27 and 2 times in that one file).

    **The repair that works:** enumerate the distinct corrupted byte runs, derive each one's original by
    **forward** simulation (corrupt every candidate character 1–3 times and match the bytes exactly),
    then replace those exact byte strings **longest first**. Verify three ways: C1 count → 0, the
    legitimate `£`/`×` counts UNCHANGED, and the file's **ASCII skeleton byte-identical** to `HEAD`
    (`tr -d '\200-\377' | md5sum`) — which proves only non-ASCII bytes moved.

    ⚠ **It was also triaged and written off, which is why the test exists rather than a note.** The
    2026-08-17 handover recorded it as *"Comments only, no behavioural effect, and confined to that one
    file (checked every `.cs` and `.xaml` in the repo)"*. It was 14 code lines out of 206, at least six
    of them strings a shopkeeper reads, across **three** files. This is not auditable by eye.
21. ⚠⚠ **NEVER DISPATCH AN `IsBusy`-GUARDED ACTION FROM INSIDE THE GUARD — IT RETURNS SILENTLY.**
    Every `Execute…` in the MAUI viewmodels opens `if (IsBusy) return;` and then sets it. So a method
    that holds the flag and calls another one is calling into a guard **its own caller is holding**: the
    target returns immediately, nothing opens, nothing throws, nothing logs. The operator sees the
    dialog they were on close and then nothing at all.

    **Three occurrences, and the third was reported from a shop floor.** Matt, on 1.100.0: *"in Loyalty,
    when I try to edit details or grant credit, the screen just closes."* `OpenCustomerAsync` dispatched
    `ExecuteEditMember` from inside its own `try`. The same fault had **already been found and fixed** in
    `TillViewModel.ExecuteAlterTransaction`, whose comment names the mechanism precisely — and the
    Loyalty code written afterwards reintroduced it. A sibling sweep then found a third in
    `ExecuteChangePrinter`, where "Try again" invoked itself and did nothing.

    ⚠ **The guards are RIGHT and must stay** — they are what stops a double-tap opening two dialogs.
    What is wrong is the dispatch site.

    ```csharp
    // ⚠ WRONG — the target's guard sees the flag this method is holding
    IsBusy = true;
    try { var outcome = await ShowDialogAsync(); if (outcome == Edit) ExecuteEdit(row); }
    finally { IsBusy = false; }

    // ✅ RIGHT — record the decision, act after the flag is released
    var chosen = Outcome.Closed;
    IsBusy = true;
    try { chosen = await ShowDialogAsync(); }
    finally { IsBusy = false; }
    if (chosen == Outcome.Edit) ExecuteEdit(row);
    ```

    ⚠ Releasing the flag *just before* the call (`IsBusy = false; ExecuteEdit(row);`) fixes the guard but
    leaves a second bug: the target sets `IsBusy = true` and the outer `finally` then clears it
    underneath the work it just started. Dispatch **after** the `finally`, not before it.

    ⚠⚠ **NOTHING CATCHES THIS CLASS.** No test can reach these `async void` handlers, and the failure is
    a silent early return — so it is invisible until somebody presses the button. The sweep that found
    the second and third: for each call to an `Execute…` from inside another method, check whether the
    caller holds `IsBusy` at that line and whether the target guards on it. ⚠ A crude version of that
    check reported `ExecuteAlterTransaction` as broken when it was already fixed — it looked for a
    `finally` and missed an explicit `IsBusy = false`. **Read the call site before believing the sweep.**

22. ⚠⚠ **`perl -0pi -e` DOUBLE-ENCODES EVERY ⚠ IN THE FILE — and this repo is full of them.**
    Cost time on 2026-08-21 and corrupted a source file that had to be reverted.

    With no encoding layer, perl reads the file's UTF-8 bytes as individual Latin-1 characters. The
    moment your **replacement** contains one character above U+00FF (`⚠`, `—`, `…`), perl re-encodes
    the *entire* output string as UTF-8 — so every ⚠ that was already in the file becomes `â\x9a\xa0`.
    The only warning is a single line: *"Wide character in print"*. The build still succeeds; the
    comments are just quietly mangled.

    ✅ **Pass the replacement as raw BYTES.** Either put it in an environment variable (perl reads
    `%ENV` as bytes, so nothing is ever "wide"):

    ```bash
    REPL='            // ⚠ the new line' perl -0pi -e 's{\Qold\E}{$ENV{REPL}}' File.cs
    ```

    …or spell the characters as byte escapes in a `/e` replacement: `\x{e2}\x{9a}\x{a0}` for ⚠,
    `\x{e2}\x{80}\x{94}` for —, `\x{e2}\x{80}\x{a6}` for …. ⚠ Get all three bytes: `\x{e2}\x{9a}`
    alone silently produces a `�`.

    ⚠ **And read `git diff` after every scripted edit.** Both faults above were invisible in the
    tool's own output and obvious in the diff.

    ⚠⚠ **AND IT CAUGHT ME AGAIN THE SAME DAY BY A SECOND ROUTE (2026-08-21).** The replacement was
    clean, and the damage came from the OUTPUT LAYER instead:

    ```perl
    open my $o, '>:raw:encoding(UTF-8)', $f;   # ⚠⚠ NEVER. This is the same bug wearing a hat.
    ```

    Perl had read the file as Latin-1 bytes (`<:raw`), so every ⚠ was already three separate
    characters. Writing through `:encoding(UTF-8)` encoded each of those three AGAIN — the whole
    document, not just the edited lines. 46 ⚠ became `â\x9a\xa0` in one command, and it had to be
    recovered with `git checkout`.

    ✅ **`>:raw` IN AND `>:raw` OUT, ALWAYS. Never an `:encoding` layer on either side.** Read bytes,
    edit bytes, write bytes — then the multi-byte characters are never interpreted at all, and
    interpretation is the only thing that can corrupt them.

    ✅ **AND CHECK AFTERWARDS WITH THE ARCHITECTURE SUITE, NOT WITH A GREP.**

    ```bash
    dotnet test tests/Plutus.Tests.Architecture/Plutus.Tests.Architecture.csproj
    ```

    `SourceEncodingTests` sweeps every source file for double-encoded UTF-8 and names the file and
    the occurrence count. ⚠⚠ **A `grep` FOR ONE MANGLED SEQUENCE IS NOT ENOUGH, and I proved it on
    2026-08-21:** after a "Wide character in print" warning I grepped for double-encoded `⚠`, got 0,
    and moved on — the damaged character was an **em dash**, and it sat in the tree until the
    architecture suite failed. The mangling hits whichever wide characters are in the string, not the
    one you thought of.

23. ⚠⚠ **A `perl -0pi` one-liner that reassigns `@ARGV` MID-STREAM TRUNCATES THE FILE TO ZERO BYTES.**
    `local(@ARGV, $/) = "other-file"` inside the `-e` script — a common idiom for slurping a
    replacement from disk — destroys the in-place edit's own file handle. `CheckoutDialog.tsx` went to
    0 bytes on 2026-08-21 and was recovered only because there was a commit an hour old.

    ✅ Read the auxiliary file in a **separate** `perl -e` step, or use `$ENV{}`. ⚠ **And commit before
    a batch of scripted edits, not after** — that commit is what made this a two-minute recovery
    instead of an afternoon.

    ⚠ There is no python on this box (`python3` resolves to the Microsoft Store shim and exits 49), and
    **no node either** — so perl and `sed` are the scripting tools available on Windows, and these two
    traps are the price.


⚠ **The standing check these came from:** a green suite proves a component works, never that
anything *uses* it. `OutboxPusher.DrainAsync`, the catalogue browse and `TillStore.SearchAsync` were
each fully built and tested while the screen in front of them looked broken. When a screen misbehaves,
grep for callers of the thing that should be doing the work before debugging the thing itself.


24. ⚠⚠ **AN INTEGRATION TEST THAT SEEDS WITH `Add` PASSES BY LUCK — THE FIXTURE DATABASE IS SHARED.**
    Hit **three times in two days** (WP-FY, WP10 #4's Bin, and WP-ZERO's daily series), so it is a
    pattern rather than three mistakes.

    Every test in a class calls the seed, and they all run against **one** `PlutusAppFactory`
    database. Two shapes go wrong:

    - **The seed ACCUMULATES.** `db.X.Add(new X { Id = Uuid7.New(), … })` adds a row per call, so a
      test asserting an exact total (`Assert.Equal(36.00m, …)`) passes only if it happens to run
      first. WP-ZERO's suite shipped like this and **was green** — it failed the moment a second
      seed call was added, which is how it was found.
    - **A SIBLING TEST MUTATES THE SEEDED STATE.** The Bin suite has a test that RESTORES the binned
      item; the seed short-circuited on "does the row exist", so every later test saw an empty Bin.
      It read as *"the Bin view is broken"*.

    ✅ **SEED IDEMPOTENTLY ABOUT STATE, NOT JUST ABOUT EXISTENCE.** A deterministic id
    (`DeterministicGuid.ForName`) makes a repeat insert a no-op; and where a sibling can change the
    row, **re-assert the field the test depends on** rather than returning early:

    ```csharp
    var existing = await db.Items.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.IdOne == Binned);
    if (existing is not null) { existing.BinnedAtUtc = When; await db.SaveChangesAsync(); return; }
    ```

    ✅ **OR ASSERT RELATIVELY** — `before - 1` rather than `0`, `>= 1` rather than `== 1`. The
    support-desk suite does this deliberately and is immune by construction.

    ⚠⚠ **AND PROVE IT: call the seed TWICE and re-run.** Green means idempotent; red means the suite
    was passing on ordering. It is a ten-second check and it is the only one that actually answers
    the question — all three of these were green in CI while broken.

## Exemplar files (copy these shapes, don't invent)

| Shape | File |
|---|---|
| Global entity + keyed upsert store | `Plutus/Commons/Plutus.Entities/Models/CommercialOps.cs` |
| Tenant-owned entity | `PaymentGatewaySettings` in `Models/CommerceConfig.cs` |
| Platform-admin controller | `src/Plutus.Tenancy/Controllers/PlatformBillingController.cs` |
| Tenant-facing perm-gated controller | `src/Plutus.Payments/GatewayConfigController.cs` |
| Sweep + keyed alert | `ChurnSweep` in `src/Plutus.Tenancy/CommercialOps.cs` |
| Portal screen, provider select + dynamic form | `NotificationsScreen` in `Plutus/Frontend/Plutus.Frontend.Portal/src/PlatformPage.tsx` |
| Integration test with RBAC seeding | `tests/Plutus.Tests.Integration/CommerceConfigE2eTests.cs` |
| Sale money invariants | `SaleV2.Validate()` + `tests/Plutus.Tests.Unit/SalesV2Tests.cs` |
