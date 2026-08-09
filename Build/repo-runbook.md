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
  VITE_OIDC_CLIENT_ID=plutus-portal npm run build     # portal is OIDC mode — env REQUIRED
cd ~/PLUTUS/Plutus.Frontend.WebApp && npm run build   # till stays password mode — no env
```

Deploy the built assets with `rsync -a --delete dist/ /srv/apps/PLUTUS/{portal,web}/current/`,
taking a `current.pre-<tag>` copy first. Caddy serves them statically.

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

⚠ **RBAC seeding does NOT run on startup.** A deploy that adds a permission must be followed by
`Plutus.SeedMigrator rbac --mysql "…"` from a **freshly published** SeedMigrator — `RbacSeeder`
compiles into it, so a stale binary re-seeds the old permission set.

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
