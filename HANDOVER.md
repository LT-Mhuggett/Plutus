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

Everything is committed **except** two deliberately-untracked files: `Environment_Setup_Runbook.md` (ETRIE doc) and `Plutus/Frontend/Plutus.Frontend.WebUI/Plutus.code-workspace` (stray). Gitignored & never committed: `publish/`, the Kapow `*.db`, `Build/snapshots/` (real sale figures), `node_modules/`, `dist/`.

Git identity is set **repo-locally** (`Matt Huggett` / `mhuggett@leadingtalent.co.uk`).

## 4. Platform build progress (Build/ specs — executed one T-task at a time)

**Goal:** evolve the single-tenant DBService into the multi-tenant modular-monolith platform in `Build/plutus-platform-architecture.md` (v3). Authority order: architecture doc > implementation plan > build specs.

**Phase 0:**
- ✅ **T0.1 — .NET 8 retarget.** All 7 backend/tool projects net7→net8 (Authentication netstandard2.1→net8); EF Core+Pomelo 6→8, Identity.Web 1→2, Swashbuckle/Z.EntityFramework.Plus→8; dropped unused AzureAD.UI + PlatformAbstractions. Zero source changes. Endpoints byte-identical (snapshots in `Build/snapshots/`, gitignored). Deployed.
- ✅ **T0.2a — SharedKernel.** `src/Plutus.SharedKernel` (no refs/packages): `Pence`, `Uuid7` (RFC 9562, big-endian, monotonic), `ITenantOwned`/`ITenantContext`, `DomainEvent`/`SaleRecorded`/`IEventBus`/`IEventConsumer`. `tests/Plutus.Tests.Unit` (xunit) — 5 tests green. Root `Plutus.slnx` created (10 platform projects; builds clean).
- 🔶 **T0.2b — module carve-up (IN PROGRESS).** First slice done: `src/Plutus.Identity` (TestTokenAuth + DevAuthBypassEvaluator + `AddPlutusIdentity`), verified live. **Remaining below.**
- ⬜ **T0.3 — CI + OpenAPI.** ⚠️ **No git remote → CI pipeline can't actually run here.** Can still: generate `openapi.json` artifact + `frontends/codegen.sh` TS types. Flag to Matt: decide a remote (GitHub) or treat CI as local scripts.
- ⬜ **T0.4 — architecture tests** (`tests/Plutus.Tests.Architecture`): no cross-module refs; no `decimal`/`double` money; query filters present; no `Guid.NewGuid()` for entity IDs; frontend isolation.

Phases 1–10 (tenancy schema, idempotent ingest, broker-less outbox, portal, MAUI sync, stock, Woo, payments, etc.) not started.

## 5. RESUME HERE — finish T0.2b via **Option A** (decided by Matt)

The entity controllers (Sale, Item, Employee, Business, Store, Stock, Tax, Category) all inherit generic bases in `Plutus/Endpoints/Plutus.DBService/Controllers/Bases/` (`ApiControllerBase{R,CR,CRU,CRUD}` + composite variants + `APIConventions`). They must get a shared home before concrete controllers can split into modules. **Option A (chosen): a shared web-infra project.**

Steps (commit per step; keep endpoints byte-identical; snapshot-diff after each; **deploy the FULL publish folder** — see §6 gotcha):

1. **`src/Plutus.Web.Infrastructure`** (net8, `FrameworkReference Microsoft.AspNetCore.App`, refs SharedKernel + Plutus.Contracts/Repository/Entities/Reports as the bases need). Move `Controllers/Bases/*` + `APIConventions` + any shared `IHttpContextAccessor`/`IRepositoryWrapper` plumbing into it.
2. **`src/Plutus.Catalogue`** — move `ItemController` (+ its inline band-validation guardrail) and `ItemParameters.Search`; `AddPlutusCatalogue()`.
3. **`src/Plutus.Sales`** — move `SaleController`'s sales/detail; the **ingest** path proper is Phase 1, so for now just relocate existing sale endpoints.
4. **`src/Plutus.Reporting`** — move `Sale/Summary` + `Sale/VatIntegrity` (reporting concerns) out of `SaleController`. Note: this splits `SaleController` — Summary/VatIntegrity→Reporting, Detail→Sales; check nothing else refs them.
5. **`src/Plutus.Tenancy`** — scaffold (empty module + `AddPlutusTenancy()`) ready for Phase 1.
6. **Host wiring:** `Plutus.DBService` stays the composition root; for each module lib add `builder.Services.AddControllers().AddApplicationPart(typeof(<AModuleType>).Assembly)` (or `.PartManager`) so MVC discovers module controllers; call each `AddPlutus<Module>()`. Employee/Business/Store/Stock/Tax/Category can stay in the host initially (they're generic CRUD) or move to a `Plutus.Catalogue`/dedicated module later — not required for T0.2 DoD.
7. **T0.4 architecture test** proving no module references another module (SharedKernel + Web.Infrastructure excepted).
8. Verify: solution builds; login/401/200; `Sale/Summary`, `Sale/Detail`, `Item` search byte-identical vs `Build/snapshots/`; deploy; ETRIE still 200/200.

Then T0.3 (OpenAPI artifact + codegen; CI-as-local-scripts pending a remote decision), and Phase 0 is done → **stop and request the Phase 1 work in detail** (spec says don't run Phase 1 from the summary alone).

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

Phase 0 ~75% done (T0.1 ✅, T0.2a ✅, T0.2b first slice ✅); resume at §5 Option A to finish the module carve-up, then T0.3/T0.4, then request Phase 1. Live test env healthy; ETRIE untouched; all work committed locally on `Matt's-Horror`.
