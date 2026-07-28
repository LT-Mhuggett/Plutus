# Plutus

A multi-tenant shop management platform: point-of-sale (till), management portal, and an
operator console for running Plutus as a service. Born as a single-shop till (Kapow Comics),
now a tenant-isolated platform.

## What's in this repo

| Piece | Where | What |
|---|---|---|
| Backend API | `Plutus/Endpoints/Plutus.DBService` (host) + `src/Plutus.*` (modules) | .NET 10 modular monolith — tenancy, identity/RBAC, sales, catalogue, reporting, cash, payments, customers, webstore (Woo), platform-operator surfaces. MySQL via EF Core/Pomelo; migrations auto-apply on startup. |
| Web till (POS) | `Plutus/Frontend/Plutus.Frontend.WebApp` | React + TS + Vite. Offline-capable; password/HMAC login. |
| Management portal | `Plutus/Frontend/Plutus.Frontend.Portal` | React + TS + Vite. Client screens (per tenant) + the operator console (`platform-admin` only). OIDC (Keycloak) login. |
| Native till (legacy) | `Plutus/Frontend/Plutus.Frontend.AppClient` + `.ClientUI` | Xamarin (NatApp, being retired) and its .NET MAUI successor. |
| Shared entities | `Plutus/Commons/Plutus.Entities` | EF model + `Migrations/MySql`. |
| Tests | `tests/` | `Plutus.Tests.Unit`, `.Architecture`, `.Integration` (in-memory SQLite host). |
| Ops | `ops/` | Incident runbook, status page, Keycloak realm + theme, staged Caddy vhosts. |
| Tools | `tools/`, `Plutus/Tools` | Per-tenant restore (`Plutus.TenantRestore` + RUNBOOK), seed/ETL migrator. |

## Getting started (backend + tests)

```bash
# Windows dev box — SDK 10.0.302
DOTNET="/c/Program Files/dotnet/dotnet.exe"
"$DOTNET" build Plutus/Endpoints/Plutus.DBService/Plutus.DBService.csproj -c Debug
"$DOTNET" test tests/Plutus.Tests.Unit/Plutus.Tests.Unit.csproj -c Debug
"$DOTNET" test tests/Plutus.Tests.Architecture/Plutus.Tests.Architecture.csproj -c Debug
"$DOTNET" test tests/Plutus.Tests.Integration/Plutus.Tests.Integration.csproj -c Debug
```

Frontends build with Node (`npm run build` in each frontend folder; the portal needs
`VITE_AUTH_MODE=oidc` + authority/client-id env vars — see HANDOVER §deploy). The test
environment (Mac mini) deploy procedure, secrets locations and environment map live in
**HANDOVER.md**.

## 📚 Documentation index

**Start here**

- **[HANDOVER.md](HANDOVER.md)** — the living state of the project: what's built and live, the
  current RESUME point, deploy procedure, loose ends, phase records. *Always current.*

**Architecture & requirements** (in `Build/`)

- [plutus-platform-architecture.md](Build/plutus-platform-architecture.md) — the platform architecture (v3); wins on conflict.
- [plutus-operator-platform-requirements.md](Build/plutus-operator-platform-requirements.md) — the operator-platform requirements doc (with built-status appendix).

**Implementation plans & progress boards** (in `Build/`)

- [plutus-implementation-plan.md](Build/plutus-implementation-plan.md) — Phases 0–12 (core platform). Complete.
- [plutus-operator-platform-plan.md](Build/plutus-operator-platform-plan.md) — Phases 13–18 (operator platform). Complete; progress board per WP.
- [operator-portal-plan.md](Build/operator-portal-plan.md) — OP1–OP4 (operator/client separation, plans, subscribers, tickets). Complete; includes the repo runbook + pitfalls list for implementing sessions.
- Historical/dated plans: [WebApp](Build/WebApp-2026-07-23-plan.md) · [OfflineMode](Build/OfflineMode-2026-07-23-plan.md) · [Migration](Build/Migration-2026-07-22-plan.md) · [BugFix](Build/BugFix-2026-07-22-plan.md) · [VAT investigation](Build/VAT-Investigation-2026-07-23-plan.md) / [VAT fix-later report](Build/VAT-FixLater-Report-2026-07-23.md) · [loyalty](Build/loyalty-usability-plan.md) · [till retrofit](Build/till-retrofit-2026-07-25.md) · [Woo SKU audit](Build/woo-sku-audit-2026-07-26.md) · [Kapow DB gap analysis](Build/kapow-db-gap-analysis.md) · [MAUI build spec](Build/plutus-maui-build-spec.md) · [Sonnet build spec](Build/plutus-sonnet-build-spec.md)

**Operations**

- [ops/incident-runbook.md](ops/incident-runbook.md) — severity ladder, first-15-minutes checklists, rollback levers. Rehearsed.
- [ops/status/README.md](ops/status/README.md) — public status page + SLA.
- [ops/keycloak/README.md](ops/keycloak/README.md) — IdP (Keycloak) setup, operator SSO/TOTP, Caddy vhost.
- [tools/Plutus.TenantRestore/RUNBOOK.md](tools/Plutus.TenantRestore/RUNBOOK.md) — per-tenant restore procedure. Rehearsed.
- `Environment_Setup_Runbook.md` (repo root, gitignored — environment specifics/secrets locations).

**Data** (gitignored, in `Build/`)

- `Build/seed-data/Kapow Comics ltd - Database - 23_07_2026 15_57_23.db` — the live-till backup used by the seed/ETL migrator (**real business data — never commit**).
- `Build/secrets.local.md` — local secrets notes. `Build/archive/` — regenerable local dev artifacts.

## Test environment

Live at `https://plutus.huggett.dscloud.me` (till), `https://admin.plutus.…` (portal),
`https://status.plutus.…` (status page), `https://login.plutus.…` (Keycloak) — a Mac mini shared
with an unrelated product (**ETRIE — never touch it**; see HANDOVER hard rules).
