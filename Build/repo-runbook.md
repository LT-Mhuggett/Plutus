# Repo runbook — read before writing code

Standing conventions for working on Plutus: how to build, test, migrate and deploy, plus the
codebase pitfalls that have each cost a session at least once. Extracted from
`operator-portal-plan.md` §0 when that plan was archived, because it outlived the plan.

**Companions:** [`HANDOVER.md`](../HANDOVER.md) is the living state of the project (what's live,
what's open, rollback tags). [`plutus-platform-architecture.md`](plutus-platform-architecture.md)
wins on any design conflict. [`table-standard.md`](table-standard.md) governs every data table.

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

⚠ **RBAC seeding does NOT run on startup.** A deploy that adds a permission must be followed by
`Plutus.SeedMigrator rbac --mysql "…"` from a **freshly published** SeedMigrator — `RbacSeeder`
compiles into it, so a stale binary re-seeds the old permission set.

⚠ `pm2 restart <name> --update-env` does **not** load new keys from the ecosystem *file*. To pick
up new env keys, restart from the file path.

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
