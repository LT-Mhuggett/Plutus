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
`grep -c "CREATE TABLE"` ~100. ⚠ **NEVER run it under `zsh -x`** — that printed the password into a
transcript on 2026-08-09 and burned the credential.

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

## MAUI till build (Windows)

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
  verify `curl -s -o /dev/null -w "%{http_code}" --resolve huggett.dscloud.me:443:127.0.0.1
  https://huggett.dscloud.me/health` → 200.
- **NEVER run `ops/keycloak/run-keycloak.sh`** — it recreates the container and wipes the enrolled
  password/TOTP. Keycloak changes go via `kcadm.sh` inside the running container.
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
    page and come back. **Subscribe to `Services.Sync.TillCadence.Ticked`** in `OnAppearing` and
    **unsubscribe in `OnDisappearing`** — it is a static event, so a page that stays attached is
    held alive for the life of the process along with every query it makes each minute. Handlers
    arrive on the cadence thread, must marshal their own UI work, and must not throw.
    ⚠ **The dangerous one is the silent one.** A stuck "(waiting to send)" gets reported within the
    hour; a takings total eight hours stale looks exactly like a correct one. **When you find one
    stale screen, go and look for its siblings straight away.**

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

⚠ **The standing check these came from:** a green suite proves a component works, never that
anything *uses* it. `OutboxPusher.DrainAsync`, the catalogue browse and `TillStore.SearchAsync` were
each fully built and tested while the screen in front of them looked broken. When a screen misbehaves,
grep for callers of the thing that should be doing the work before debugging the thing itself.

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
