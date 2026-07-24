# Handover — Plutus platform build

**Date:** 2026-07-23 (evening)
**Branch:** `Matt's-Horror` · **no git remote** (nothing pushed; commits are local only)
**Hard rule:** **DO NOT TOUCH ETRIE** — it shares the Mac mini but is a separate product. Every Plutus change keeps ETRIE's ports/processes/paths/Caddy blocks untouched; verify ETRIE health (`https://10.1.1.40/health`, `https://huggett.dscloud.me/health` → 200) after any Mac change.

This supersedes the earlier MAUI-only handover. Companion docs: `Build/` (platform architecture v3 + implementation plan + Sonnet/MAUI build specs + Kapow gap analysis), `WebApp-2026-07-23-plan.md`, `OfflineMode-2026-07-23-plan.md`, `VAT-Investigation-2026-07-23-plan.md`, `VAT-FixLater-Report-2026-07-23.md`.

---

## 1. What exists and is LIVE (test environment)

A complete **React web POS** trading against a **.NET backend + MySQL**, all on the Mac mini, seeded with the real Kapow database.

- **Till:** `https://plutus.huggett.dscloud.me` — login-first (real token auth). Logins: `dev@plutus.local` / `PlutusDev2026`, or `kapow_comics@outlook.com` / (the till's real password). Features: scan/search, basket (qty/price-adjust/reorder), discounts, returns (by receipt id **or by date**), park/retrieve, split-payment checkout, browser receipts + copy-reprint, offline/PWA (IndexedDB catalogue + checkout outbox), employee management, item add/edit, **Reporting** (Summary dashboard w/ SVG charts, Custom + Excel + sale recall, VAT calc + off-band integrity banner), editable Store Information, Settings.
- **Backend:** `Plutus.DBService` (now **.NET 8**), self-contained `osx-arm64`, under **pm2** as `plutus-backend` on `127.0.0.1:5100`. Auth = flag-gated `TestTokenAuth` (HMAC bearer, 12h) because B2C is unreachable; every `/api` endpoint 401s without a token.
- **DB:** MySQL 9.6 (Homebrew), schema `plutus`, seeded via `Plutus.SeedMigrator` from the Kapow backup (20,340 items / 21,653 sales / 74,822 lines). Credentials in `~/PLUTUS/secrets/mysql.env` (also holds `TEST_TOKEN_SECRET`).
- **Edge:** Caddy serves the static till at `plutus.huggett.dscloud.me` and reverse-proxies `/api/*` → 5100. LE cert auto-renews. (Router SNATs WAN→LAN, so Caddy IP allowlists don't work — auth is the gate, not IP.)

Full environment detail is in memory (`plutus-test-environment.md`) and `Environment_Setup_Runbook.md` is **ETRIE's** runbook (left uncommitted deliberately — not ours to commit).

## 2. Subdomain scheme (decided, DNS-verified; recorded in architecture doc §6.2)

| Host | Serves | Status |
|---|---|---|
| `plutus.huggett.dscloud.me` | Web POS / till | live |
| `admin.plutus.huggett.dscloud.me` | Management portal (React app #2) | Phase 3 |
| `api.plutus.huggett.dscloud.me` | Backend API — single isolated surface | Phase 3 cutover |

`*.huggett.dscloud.me` wildcard resolves any depth to 94.6.166.54. Until the portal lands the till keeps using `/api` on its own host.

## 3. Git state

Branch `Matt's-Horror`, local only. Recent commits (newest first):

```
cbf0b75 feat(wp0.2b): extract auth into Plutus.Identity module
57e89fa feat(wp0.2): SharedKernel primitives + unit test harness
7694a4a feat(wp0.1): retarget backend to .NET 8
1a1d56f docs: platform architecture/build specs, VAT investigation + fix-later
3781a2e feat(webapp): employees, item editor, offline PWA, reporting+VAT, store/settings
262cc92 feat(backend): auth/SetPassword, item VAT guardrail, sale summary/detail/vat-integrity
96a0c0e fix(native): BugFix-2026-07-22 bugs in NatApp and MAUI
```

**Remote (added 2026-07-24):** `origin` = `https://github.com/LT-Mhuggett/Plutus.git` (Matt's, private) · `upstream` = `github.com/seank842/Plutus.git` (Sean's original). All work is **pushed to origin/Matt's-Horror**. NOTE: the push was rebuilt into a **single squashed commit `3d2837a`** on top of upstream/master ("remove secret-bearing history") — the granular per-task commits are NOT on GitHub (content intact); new commits from here are granular again. GitHub Credential Manager (browser) — Matt authenticates.

**Deferred hygiene (Matt's call, left as-is):** tracked `appsettings*.json` carry cleartext MySQL passwords (Sean's old dockerised-dev creds, NOT the live Mac DB) — pushed to the private repo. Options when revisited: move to env/user-secrets (forward), or `git filter-repo` purge (if repo goes public).

Git identity is set **repo-locally** (`Matt Huggett` / `mhuggett@leadingtalent.co.uk`).

## 4. Platform build progress (Build/ specs — executed one T-task at a time)

**Goal:** evolve the single-tenant DBService into the multi-tenant modular-monolith platform in `Build/plutus-platform-architecture.md` (v3). Authority order: architecture doc > implementation plan > build specs.

**Phase 0 — COMPLETE (all verified live; ETRIE untouched).**
- ✅ **T0.1 — .NET 8 retarget.** 7 projects net7→net8; EF/Pomelo 6→8, Identity.Web 1→2; dropped unused AzureAD.UI + PlatformAbstractions. Endpoints byte-identical (snapshots in `Build/snapshots/`, gitignored).
- ✅ **T0.2 — module carve-up (Option A).** `src/`: `SharedKernel` (Pence, Uuid7, tenancy, events), `Web.Infrastructure` (generic controller bases + APIConventions), `Identity` (auth), `Catalogue` (ItemController + band guardrail), `Sales` (SaleController incl. transitional Summary/VatIntegrity/SaleReport), `Reporting` + `Tenancy` scaffolds. Host (`Plutus.DBService`) = composition root, registers module controllers via `AddApplicationPart` + `AddPlutus<Module>()`. Namespaces kept stable (zero concrete-controller edits). Every endpoint byte-identical at each step.
- ✅ **T0.3 — OpenAPI + codegen + CI.** `openapi.json` (64 paths) from the live Swagger; `frontends/codegen.sh` → `WebApp/src/api/types.gen.ts` (generated, tsc-clean, committed, not yet imported — wired at T2.1); `.github/workflows/ci.yml` (needs a 9.0.x SDK for .slnx; **untested until Actions enabled**).
- ✅ **T0.4 — architecture tests** (`tests/Plutus.Tests.Architecture`, 5 pass + 1 Phase-1 skip): no cross-module refs; no decimal/double money in modules; no `Guid.NewGuid()` for IDs; frontend isolation. (Query-filter rule skipped until tenant entities exist.)

Test totals: **Unit 5 + Architecture 6 (1 skip)** green. Whole `Plutus.slnx` builds clean.

Phases 2–10 not started.

## 5. RESUME HERE — Phase 1 / T1.7 (T1.1–T1.6 COMPLETE)

**✅ T1.6 COMPLETE (2026-07-24)** — commit `d2915fd`. Regenerated `openapi.json` (71 paths = 64 prior + 7 new `/api/v1`) from a locally-booted instance; diff purely additive (existing paths byte-stable). New controllers annotated with `[ProducesResponseType]` so real status codes are documented (sales ingest 201/200/202/400/403; enrol 200/400/410; device-token 200/400/401). Enabled the `openapi-drift` CI job (boots host on SQLite, dumps Swagger, fails on diff) — untested until Actions enabled. TS regen deferred (no node in this env; Phase 2 wiring anyway). **Local-boot recipe** (useful for any spec/HTTP work): `DOTNET_ROLL_FORWARD=LatestMajor ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5199 DISABLE_AUTH_DEV_ONLY=true TEST_TOKEN_SECRET=x dotnet <host.dll>` then curl `/swagger/v1/swagger.json` (this box has AspNetCore.App 10 x64 only, hence roll-forward).

**▶ NEXT — T1.7 (partial done) + T1.8.** T1.7 suites **(2) money reconciliation** and **(3) idempotency replay** are DONE (`ea9c839`, `StandingSuitesTests.cs`: 300-sale penny-exact reconciliation; 100-sale ×3 replay stable). **Still to do:** T1.7 suite **(1) route-level tenant isolation** — enumerate all `/api/v1` routes via ApiExplorer, as tenant A hit tenant-B ids → 404/empty never 200-data/403 — plus the deferred **HTTP e2e** (T1.2/T1.4) and **20-way concurrent ingest** (T1.4). All three need an HTTP host harness: `WebApplicationFactory<Startup>` overriding the `RepositoryContext` registration with a **MySqlDbContext-on-SQLite** (the DEBUG host uses SqliteDbContext, which lacks the tenancy/sales tables) + a test auth handler minting tenant-A/B/device principals. Row-level isolation itself is already proven (`TenancyTests`). Then **T1.8 Kapow migration v2**: `tools/Plutus.Migration.Kapow` library + SeedMigrator v2, mapping the Kapow SQLite → sales-v2 per `Build/kapow-db-gap-analysis.md` §5 (timestamp sale-IDs, VAT-not-on-lines, decimal→pence, barcode-PK, negative stock).

---
### (historical) T1.6 resume notes — superseded by the above

**✅ T1.5 COMPLETE (2026-07-24)** — commit `9aab9af`. `OutboxDispatcher` (BackgroundService, Web.Infrastructure, `Plutus.Infrastructure.Outbox`) polls 500ms and drains `OutboxEvents` into each registered `IEventConsumer` via the testable `OutboxDrainer`. Per event: dedupe vs `ProcessedEvents`, handle with bounded retry (1s/5s/25s then park to `ConsumerDeadLetters`), advance `ConsumerOffsets` in the SAME SaveChanges → effectively-once across restarts; poison parks + later events flow; consumers independent. Idempotency centralised in the drainer (not a per-consumer base). Lag = max(Id)−offset; `GET /api/v1/ops/deadletters` (platform-admin). New tables `ProcessedEvents`+`ConsumerDeadLetters` (migration `AddOutboxConsumerTables` → `plutus_t1`). Registered via `AddPlutusOutbox()`; inert on SQLite host. Tests: converge / offset-reset dedupe / poison-parks-then-flows / lag. **33/33 unit, 5/5 arch.**

**▶ NEXT — T1.6 contract freeze** (spec §T1.6): boot an instance, regenerate `openapi.json`, review field-by-field vs architecture §4.1 + the T1.2/T1.4 endpoint tables, commit; enable the CI drift gate from T0.3 (the commented `openapi-drift` job in `.github/workflows/ci.yml`) so a controller signature change without regen fails CI, and TS types regenerate cleanly. NOTE: the new `/api/v1/*` endpoints (tenants, tills, tokens, sales, ops) are net-new since the 64-path `openapi.json` snapshot.

---
### (historical) T1.5 resume notes — superseded by the above

**✅ T1.4 COMPLETE (2026-07-24)** — commit `7fd12db`. `SalesIngestService` (Sales module) `POST /api/v1/sales`: one tx → validate T1.3 invariants → quarantine (202) if unfixable, else insert SaleV2+lines+tenders + `OutboxEvents(SaleRecorded)` + bump `Device.LastSeenSeq=max(cur,seq)` → 201; duplicate saleId re-reads → 200; quarantine idempotent via unique `(TenantId,SaleId)` on SaleQuarantine (migration `AddQuarantineSaleId`, applied to `plutus_t1`). Provider-agnostic idempotency (re-read on conflict, no vendor error codes). Controller: tenant/device from token not body (mismatch→403), Idempotency-Key must equal saleId (else 400), TillId server-derived from device. Policy `sales.ingest` = device OR `pos.sell`. Tests: record+outbox+seq, duplicate→200 (1 row/1 event), quarantine idempotent, monotonic seq. **29/29 unit, 5/5 arch.** 20-way concurrent test deferred to T1.7 (needs MySQL; DB unique constraint is the guarantee).

**▶ NEXT — T1.5 broker-less dispatch** (spec §T1.5): `OutboxDispatcher` hosted service — per registered `IEventConsumer`, read `ConsumerOffsets[name]`, fetch next ≤100 `OutboxEvents` with Id>offset ordered by Id, `HandleAsync` sequentially, advance offset in the same tx as the last success; retry 1s/5s/25s then park to `ConsumerDeadLetters` + advance (a stuck consumer must not block others). `IIdempotentConsumer` base + `ProcessedEvents` table in SharedKernel. Lag metric + `GET /api/v1/ops/deadletters` (platform-admin). Runs in the host (`Plutus.Api`/DBService).

---
### (historical) T1.4 resume notes — superseded by the above

**✅ T1.3 COMPLETE (2026-07-24)** — commit `4b6c8cb`. Seven server-only tables on `plutus_t1`: `SalesV2` (header, named V2 to avoid the legacy `Sales` collision — renamed at T1.8 cutover), `SaleLines`, `SaleTenders`, `SaleAdjustments`, `SaleQuarantine`, `OutboxEvents`, `ConsumerOffsets`. Integer pence, UUIDv7 (char(36)). `SaleV2.Create` enforces the four money invariants (throws `InvalidSaleException`); `Validate()` re-runnable post-EF. The 4 queryable sale tables are tenant-scoped; Outbox/Offsets/Quarantine unscoped (infra). Tests: inconsistent-throws + 1000-sale property round-trip. **25/25 unit, 5/5 arch.** Legacy `Sales` (21,654 rows) + live + ETRIE untouched.

**▶ NEXT — T1.4 idempotent ingest** `POST /api/v1/sales` (spec §T1.4): device/operator token; tenantId+deviceId from token not body; validate invariants → quarantine (202) on unfixable; insert Sale+lines+tenders + OutboxEvents(SaleRecorded) + bump Device.LastSeenSeq in one tx → 201; duplicate `(TenantId,Id)` → re-read → 200. Idempotency-Key must equal body saleId. Ingest lives in the Sales module; add an `OutboxEvent` write here (dispatch is T1.5).

---
### (historical) T1.3 resume notes — superseded by the above

**✅ T1.2 COMPLETE (2026-07-24)** — commits `a786c46`,`2d33cc2`,`1b98f07`,`bb69da5`,`a4a60ef`,`97b4528`:
- **Auth:** `HttpTenantContext` resolves tid/did/scope from claims (null-safe→Kapow, platform-admin→unscoped). `PlutusTokenAuthHandler` (test-env) validates operator + device tokens → scope/tid/did claims; real scope policies `platform-admin`/`portal.tills.enrol`/`device` (names in `SharedKernel.PlutusPolicies`) replace the old all-or-nothing DevAuthBypass. Gated behind `DISABLE_AUTH_DEV_ONLY`.
- **Schema:** `EnrolmentCode` + `Device` (server-only, global/unscoped) on `plutus_t1`; `WebCredential` mapped to the existing table (guarded migration, no-op on existing DBs).
- **Endpoints (Tenancy module):** `POST /api/v1/tenants` (provision Tenant+Business+Store+admin), `POST /api/v1/tills`, `POST /api/v1/tills/enrol` (anon), `POST /api/v1/tokens/device` (anon), `POST /api/v1/tills/{id}/revoke`. Rate limiter 5/min/IP on the anon endpoints. Device tokens signed with `TEST_TOKEN_SECRET` (same secret the handler validates).
- **Crypto (SharedKernel):** Crockford32, Pbkdf2 (legacy KDF params), CompactToken (HMAC).
- **Tests:** enrolment lifecycle, wrong-secret 401, reused/expired 410, auth-handler claim emission/expiry/tamper, provisioning full-graph + admin-login verify + isolation. **23/23 unit, 5/5 arch green.**
- **DEBUG caveat:** the host runs SqliteDbContext in DEBUG (no tenancy tables) so tenancy endpoints are Release-only; service logic is covered by SQLite tests. Full HTTP e2e via the host deferred to T1.7.

**▶ NEXT — T1.3 Sales schema v2:** new `Sales`/`SaleLines`/`SaleTenders`/`SaleAdjustments`/`SaleQuarantine`/`OutboxEvents`/`ConsumerOffsets` (spec §T1.3), constructor-enforced money invariants, migration to `plutus_t1`, property-based round-trip test. Legacy sale tables stay untouched until T1.8 migrates data.

---
### (historical) T1.2 resume notes — superseded by the above

Phase 0 done; **Phase 1 authorised**. Phase 1 is specced in `Build/plutus-sonnet-build-spec.md` T1.1–T1.8.

**✅ T1.1 COMPLETE (2026-07-24)** — commits `26f03e0`, `7fe8e78`, `55c8588` on `Matt's-Horror`:
- `Tenant` entity + `Tenants` table on **MySqlDbContext only** (shared model + MAUI Sqlite untouched). Scaffold's EF6→8 spurious `AlterColumn` noise was trimmed away and **proven clean** (a probe migration scaffolds an empty `Up()`).
- Shadow `TenantId` + index + global query filter on **19 tenant-owned entities** (by convention). `Person` is scoped as the TPT root of `Employee`. Global/shared (Role, PaymentMethod, Person-as-reference→no, AuthActions*, mapping tables) stay unscoped. **Decision 2026-07-24:** Role/PaymentMethod/Person-hierarchy classification confirmed with Matt.
- `SaveChanges` stamps `TenantId` from context and throws on cross-tenant writes. `ITenantContext` (SharedKernel) via optional ctor; **null-safe default = Kapow** so every non-DI call site keeps working.
- Migration `AddTenantIdToTenantOwned` (19 ADD COLUMN + 19 indexes + in-migration Kapow backfill) **applied to `plutus_t1` only** — 21,654 Sales / 74,823 Trans backfilled, 0 rows left `Guid.Empty`. **Live `plutus` has 0 TenantId columns (untouched); ETRIE untouched.**
- Tests: `Plutus.Tests.Unit.TenancyTests` — model-metadata (right entities scoped) + SQLite two-tenant isolation + cross-tenant-write guard. **8/8 unit, 5/5 arch green.** T0.4 rule-3 skip removed.
- Kapow tenant id (stable): `0192b8a0-1a6f-7000-8000-000000000001` (`Plutus.Entities.Tenancy.KnownTenants.Kapow`).

**▶ NEXT — T1.2 (provisioning + enrolment + real ITenantContext):** fill the `Plutus.Tenancy` scaffold; `POST /api/v1/tenants` (platform-admin) and `POST /api/v1/tills/enrol` (anon) per spec §T1.2; implement the **request-scoped JWT-backed `ITenantContext`** reading `tid`/`did` claims and register it (scoped) in the DBService DI so EF picks the `(options, ITenantContext)` ctor. Until then the Kapow default drives single-tenant.

**Superseded groundwork notes (kept for context):**
- ✅ **DB copy** `plutus_t1` on the Mac (dump of live `plutus`, `--set-gtid-purged=OFF`; 20,340 items / 21,654 sales). `plutus` user granted. **All T1.1 migration work targets `plutus_t1`; live `plutus` is untouched until proven.**
- ✅ **Migration toolchain on net8**: `Database.Migrations.Startup` retargeted net7→net8 (EF Tools 8); `dotnet-ef` 8.0.10 installed global; `dotnet ef dbcontext list` discovers MySqlDbContext/SqliteDbContext. **PATH gotcha:** the ef tool needs the x64 SDK first on PATH — run with `export PATH="/c/Program Files/dotnet:$HOME/.dotnet/tools:$PATH"` or it fails "Unable to retrieve project metadata" (x86 shadow).

**KEY DECISION (Matt, 2026-07-24): evolve the schema IN PLACE** (not greenfield). Add `Tenants` above the existing hierarchy; existing **`Business` plays the Company role** (add a separate `Companies` table only if a real multi-company-per-tenant need appears); **keep existing `Stores`/`Till`**; add `TenantId` to tenant-owned tables; backfill one tenant "Kapow" (deterministic id). The live webapp keeps working throughout.

**Two complications to handle in the next increment:**
1. **Name overlap already resolved by the decision:** the spec's `Companies/Stores/Tills` map to existing `Business/Stores/Till` — do NOT create parallel tables.
2. **`RepositoryContext` is bi-modal** — MySqlDbContext (server) AND SqliteDbContext (MAUI till) share it. Tenancy is server-side; generate/apply the migration for **MySqlDbContext only** (`dotnet ef migrations add … -c MySqlDbContext -o Migrations/MySql`), and keep the model change tolerable for the Sqlite/MAUI side (columns nullable/unused locally, or guarded). Verify the MAUI Sqlite path still builds.

**Next concrete steps:** create `Tenant` entity + DbSet + config in `Plutus.Entities`; add `TenantId` (Guid, char(36) to match existing GUID mapping) to tenant-owned entities implementing `ITenantOwned`; global query filter in `RepositoryContext` reading an injected `ITenantContext` (null-safe for MAUI); `AddTenants`/`AddTenantId` migration → `dotnet ef migrations script` → apply to `plutus_t1` → verify + backfill Kapow → isolation integration test (two tenants). Then remaining T1.1–T1.8 below.

---
**Full Phase 1 task list** (spec authority):

Order (each with a DoD in the spec; commit per task; deploy the FULL publish folder):
1. **T1.1 Tenancy schema** — new `Tenants/Companies/Stores/Tills/EnrolmentCodes` (fill the `Plutus.Tenancy` scaffold); add `TenantId` to every tenant-owned table with composite indexes; EF global query filter by convention; `ITenantContext` from JWT `tid`; `SaveChanges` stamps/guards TenantId. **This is the flip-the-query-filter-test-on point** (un-skip the T0.4 rule-3 test). ⚠️ migrate a DB copy first; the live env has real Kapow data.
2. **T1.2 Provisioning + device enrolment** (Tenancy module): `POST /tenants`, `/tills`, `/tills/enrol`, `/tokens/device`, revoke.
3. **T1.3 Sales schema v2** — `Sales/SaleLines/SaleTenders/SaleAdjustments/SaleQuarantine/OutboxEvents/ConsumerOffsets`, pence + per-line VAT, UUIDv7 PKs, `(TenantId,SaleId)` unique. Legacy sale tables stay until reconciliation sign-off.
4. **T1.4 idempotent ingest** `POST /api/v1/sales` (Plutus.Sales) + transactional outbox.
5. **T1.5 broker-less dispatcher** (OutboxEvents polling + ConsumerOffsets).
6. **T1.6 contract freeze** (regenerate `openapi.json`, enable drift gate).
7. **T1.7 standing suites** (tenant isolation, money reconciliation, idempotency).
8. **T1.8 Kapow migration v2** (extend SeedMigrator per `kapow-db-gap-analysis.md` §5).

Note: the transitional `Sale/Summary`/`VatIntegrity`/`SaleReport` on Plutus.Sales migrate to Plutus.Reporting in **Phase 3** (projections), not Phase 1.

## 6. Operational how-to (for the next session)

- **SSH:** `ssh -i ~/.ssh/plutus_mac_ed25519 admin@10.1.1.40`. sudo needs Matt (password prompt) — hand sudo blocks to him.
- **Node/dotnet:** Windows dev box has **.NET 10 SDK** (`"C:\Program Files\dotnet\dotnet.exe"`, builds net8 fine) but **no Node** (by choice). The **Mac** has Node 26 (Homebrew) — the webapp is built there.
- **Backend deploy:** publish `-r osx-arm64 --self-contained`; **⚠️ when new module assemblies are added, copy the WHOLE publish folder** (a cherry-picked DLL missing `Plutus.SharedKernel.dll`/`Plutus.Identity.dll` crashed the process to 000 this session). Procedure: tar the publish dir → scp → on Mac stop pm2, swap `~/PLUTUS/backend` (keep the old as a `*_bak`), `chmod +x Plutus.DBService`, `pm2 restart plutus-backend`. Rollback layers currently on the Mac: `~/PLUTUS/backend_net7_bak`, `~/PLUTUS/backend_prev`.
- **Webapp deploy:** build on Mac (`npm run build` in `~/PLUTUS/Plutus.Frontend.WebApp`), copy `dist/.` → `/srv/apps/PLUTUS/web/current/`.
- **Verify token flow:** `POST /api/Auth/Login {email,password}` → `{token}`; use `Authorization: Bearer <token>` + `BusinessId: d5a31aac-159e-9a30-706b-02f9eb935600`.
- **Caddy edits:** stage in `~/PLUTUS/staging/`, `caddy validate`, back up, graceful reload; re-run ETRIE health checks. (Not needed for backend-only work.)

## 7. Known debts / open decisions

- **Newtonsoft in `TestTokenAuth`** — spec bans Newtonsoft; kept for now (token (de)serialisation). Migrate to System.Text.Json as a later cleanup (safe: validation works on the raw string; only issue-time JSON changes).
- **No git remote** — decide (GitHub private repo?) so work is backed up off-machine and CI can run. Currently one disk = single point of failure.
- **B2C tenant** — the real auth blocker (architecture §11); `TestTokenAuth` is the stand-in seam. Deferred by Matt.
- **VAT legacy data** — 47 off-band items + NatApp `DiscountRate=0` regression documented in `VAT-FixLater-Report-2026-07-23.md`; guardrails live, legacy data intentionally not repaired.
- **NatApp bug fixes are code-only, NOT build-verified** (no Xamarin toolchain here) — build in Visual Studio before shipping to the shop.
- **No test suite beyond SharedKernel unit tests** — the spec's standing suites (tenant isolation, money reconciliation, idempotency) arrive with Phase 1.
- **MAUI ClientUI** (net10-windows) still references the shared libs (now net8) — fine (net10 consumes net8); not re-verified this session.

## 8. One-line status

**Phase 0 + Phase 1 T1.1-T1.6 COMPLETE** (tenancy schema + row-level scoping + provisioning/enrolment/device-token API + scope-based auth; all migrations applied to staging `plutus_t1` only; live `plutus`/ETRIE untouched). All pushed to `github.com/LT-Mhuggett/Plutus` `Matt's-Horror` (latest `ea9c839`). 35/35 unit + 5/5 arch green. Live test env healthy. Resume at §5 — Phase 1 **T1.7** (standing test suites).
