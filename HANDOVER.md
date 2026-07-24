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

## 5. RESUME HERE — Phase 1 (foundations: multi-tenant core + ingest)

Phase 0 is done. Phase 1 is specced task-by-task in `Build/plutus-sonnet-build-spec.md` T1.1–T1.8 (authority: architecture doc). **Awaiting Matt's explicit go-ahead** — T1.1 is a real schema migration on the live seeded MySQL, so start on a COPY.

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

**Phase 0 COMPLETE** (T0.1–T0.4; modular monolith, OpenAPI + codegen + CI file, arch tests). All work committed **and pushed** to `github.com/LT-Mhuggett/Plutus` (squashed — see §3). Live test env healthy; ETRIE untouched. Resume at §5 — Phase 1 (needs Matt's go-ahead; T1.1 migrates a DB copy first).
