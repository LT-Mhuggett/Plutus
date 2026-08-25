# Plutus

A multi-tenant shop management platform: point-of-sale (till), management portal, and an
operator console for running Plutus as a service. Born as a single-shop till (Kapow Comics),
now a tenant-isolated platform.

## What's in this repo

| Piece | Where | What |
|---|---|---|
| Backend API | `Plutus/Endpoints/Plutus.DBService` (host) + `src/Plutus.*` (modules) | .NET 10 modular monolith — tenancy, identity/RBAC, sales, catalogue, reporting, cash, payments, customers, webstore (Woo), platform-operator surfaces. MySQL via EF Core/Pomelo; migrations auto-apply on startup. |
| Web till (POS) | `Plutus/Frontend/Plutus.Frontend.WebApp` | React + TS + Vite. Offline-capable; password/HMAC login. |
| Management portal | `Plutus/Frontend/Plutus.Frontend.Portal` | React + TS + Vite. Client screens (per tenant) + the operator console (`platform-admin` only). Email-first login: password → client portal, or OIDC/Keycloak + TOTP for operators and MFA-required tenants. |
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

Frontends build with Node (`npm run build` in each frontend folder; the portal's email-first
login needs `VITE_OIDC_AUTHORITY` + `VITE_OIDC_CLIENT_ID` set so it can route MFA/operator users
to Keycloak — see HANDOVER §deploy). The test environment (Mac mini) deploy procedure, secrets
locations and environment map live in **HANDOVER.md**.

## 📚 Documentation index

**Start here**

- **[HANDOVER.md](HANDOVER.md)** — the living state of the project: what's built and live, the
  current RESUME point, deploy procedure, loose ends, phase records. *Always current.*

**Design docs, plans and standards** (in `Build/`)

- **[Build/index.md](Build/index.md)** — the index for everything in `Build/`: which documents are
  standards, which plans still have work in them, and which are archived as delivered. Audited
  against the code 2026-08-07.

The four you'll reach for most often:

- [plutus-platform-architecture.md](Build/plutus-platform-architecture.md) — the platform architecture (v3); **wins on conflict** with any plan.
- [plutus-operator-platform-requirements.md](Build/plutus-operator-platform-requirements.md) — the operator-platform requirements doc (with built-status appendix).
- [repo-runbook.md](Build/repo-runbook.md) — build / test / migrate / deploy commands, hard rules, and the codebase pitfalls that have each cost a session. Read before writing code.
- [table-standard.md](Build/table-standard.md) — the shared `DataTable` (sort / search / 25-50-100 / pagination) all tables must use, across till, portal, and operator console.

Two bodies of work are open, both about the **native till**:
[Test Maui.md](Build/Test%20Maui.md) — **the one live MAUI document**. The retrofit itself
is **built and archived** (2026-08-23); what remains is a hand-run. And
[NatApp-Translation-Agent](Build/To%20do/NatApp data translation agent and scripts.md) (move the
legacy shop data in). Everything else lives in [Build/archive/](Build/archive/), banner-stamped with
what shipped, what was gated, or what later document superseded it.

**Operations**

- [ops/incident-runbook.md](ops/incident-runbook.md) — severity ladder, first-15-minutes checklists, rollback levers. Rehearsed.
- [ops/status/README.md](ops/status/README.md) — public status page + SLA.
- [ops/keycloak/README.md](ops/keycloak/README.md) — IdP (Keycloak) setup, operator SSO/TOTP, Caddy vhost.
- [tools/Plutus.TenantRestore/RUNBOOK.md](tools/Plutus.TenantRestore/RUNBOOK.md) — per-tenant restore procedure. Rehearsed.
- `Environment_Setup_Runbook.md` (repo root, gitignored — environment specifics/secrets locations).

**Data** (gitignored, in `Build/`)

- `Build/seed-data/Kapow Comics ltd - Database - 23_07_2026 15_57_23.db` — the live-till backup used by the seed/ETL migrator (**real business data — never commit**).
- `Build/secrets.local.md` — local secrets notes.
- `Build/archive/Database.db*` — regenerable local dev artifacts. (The **documents** in `Build/archive/` are tracked and are the delivered plans — see [Build/index.md](Build/index.md).)
- `Archive/` (repo root, **gitignored**) — local-only holding area for files we've pulled out of the tree but aren't ready to delete. See `Archive/README.md`.

## Test environment

Live at `https://plutus.huggett.dscloud.me` (till), `https://admin.plutus.…` (portal),
`https://status.plutus.…` (status page), `https://login.plutus.…` (Keycloak) — a Mac mini shared
with an unrelated product (**ETRIE — never touch it**; see HANDOVER hard rules).
