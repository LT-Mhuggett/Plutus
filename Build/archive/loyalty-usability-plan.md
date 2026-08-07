> **📦 ARCHIVED — implemented.** LP1–LP3 done 2026-07-27; `customers.manage` is live in
> `Plutus.SharedKernel/Permissions.cs` and enforced across `Plutus.Customers`. The flagged
> follow-up (BankingPage / PricesPage / StockPage reading the bearer straight from
> `localStorage`) is **also fixed** — only `auth.ts` and `session.ts` touch that key now.

# Loyalty Usability — implementation plan

**Detour from `plutus-operator-platform-plan.md` (Phase 13 paused).** Fixes the
customer/loyalty experience the user flagged: "can't add customers on the till; can't
edit anything on the portal."

## Findings (verified 2026-07-27)
- Backend CAN create customers / issue credit / set membership — all in
  `src/Plutus.Customers/CustomersController.cs`. No UPDATE (name/email/phone are write-once).
- Portal edit surface **exists** but on the **Customers** tab (the **Loyalty** tab is read-only
  by design). Writes gated on `portal.users.manage` (overloaded — no customer-specific perm).
- Portal `CustomersPage` (+ Banking/Prices/Stock) read the bearer token straight from
  `localStorage["plutus.portal.session"]` instead of the `auth.ts` facade → will 401 under the
  Phase 9 OIDC flip. Fixing CustomersPage here; others flagged as follow-up.
- Till (React WebApp) has **no** add/edit customer UI — only search+attach existing + redeem.
  Operators DO log in with an operator token carrying RBAC `scope` claims (`sessionScopes()`),
  so a permission-gated create works from the till. MAUI clients have no loyalty at all.

## Design decisions (user, 2026-07-27)
- **Single `customers.manage` permission** (surface-neutral), gates all customer writes.
- **Till: supervisors/managers only** — grant to Owner, Company Admin, Store Manager, Supervisor
  (NOT Cashier). Front-line still attaches existing + redeems.

## Work packages
| WP | Scope | Status |
|---|---|---|
| LP1 | Backend: add `customers.manage` to catalogue; grant to Owner/Company Admin/Store Manager/Supervisor in `RbacSeeder`; re-gate Create/IssueCredit/SetMembership off `portal.users.manage`; add `PUT /api/v1/customers/{id}` (update name/email/phone), audited. No schema migration — grants backfill via `EnsureBuiltInRolesAsync`. | ✅ 2026-07-27 |
| LP2 | Portal `CustomersPage`: switch token read to `auth.ts` facade; add `updateCustomer`; add edit form; friendly 403 handling. | ✅ 2026-07-27 |
| LP3 | Till: `createCustomer`/`updateCustomer` in `api.ts`; `canManageCustomers()` in `pipeline.ts`; "Add customer" flow in the at-sale bar, gated + auto-attach. | ✅ 2026-07-27 |
| LP0 | Verify: backend tests green (below). ⏳ USER: click-test the Customers tab; DEPLOY: re-run `Plutus.SeedMigrator rbac` on the test env to backfill the new grant onto existing tenants' roles. | 🔨 |

## Gate — PASSED (net10, SDK 10.0.302)
Unit **169**, Architecture **5**, Integration **6** — all green, incl. new tests:
`RbacTests.BuiltIn_roles_grant_customers_manage_…` and
`CustomersLoyaltyE2eTests.Customer_writes_are_gated_on_customers_manage_and_edit_round_trips`.
Frontend (portal + till WebApp) edits follow existing conventions but are **not** compiler-verified
locally (no npm/node on this box) — they build on the Mac.

## Deploy note
`customers.manage` grants reach existing tenants ONLY when `EnsureBuiltInRolesAsync` runs — i.e.
re-run **`Plutus.SeedMigrator rbac --mysql "<conn>"`** on the test env after deploy, else current
supervisors/managers won't yet hold the new permission and writes will 403.

## Known follow-up (out of scope, flagged)
`BankingPage.tsx`, `PricesPage.tsx`, `StockPage.tsx` still read the bearer straight from
`localStorage["plutus.portal.session"]` (same latent OIDC bug fixed in `CustomersPage`). Migrate
them to the `auth.ts` facade before the Phase 9 Keycloak flip.
