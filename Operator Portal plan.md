# Operator Portal plan — implementation spec

**Date:** 2026-07-28 · **Trigger:** Matt's first real operator login (Keycloak SSO, WP18.1).
**Audience:** this document is written to be executed WP-by-WP by an implementing model
(Sonnet-grade) with no prior context. Read §0 and §1 fully before writing any code.

**The product ask (Matt, operator/owner):** logging in as an operator lands in the *client*
portal (Banking, Stock, Prices, Customers, Loyalty, Webstore) with the operator surface tucked
into one "Platform" tab. Operators must not see client data at all. Operators DO need:
subscribers ("users who signed up"), subscriptions (active/inactive, set prices), billing
connectors (exists: Platform → Billing), and a ticket system ("Ask for help" → operator inbox).

---

## 0. Repo runbook — read first, applies to every WP

**Environment:** Windows dev box; backend .NET 10 (`"C:\Program Files\dotnet\dotnet.exe"`, SDK
10.0.302). No Node on Windows — frontends build on the Mac test server over SSH. Work on branch
`Matt's-Horror`; push to `upstream` (seank842/Plutus): `git push upstream "HEAD:Matt's-Horror"`.

**Build / test commands (from repo root, Git-Bash syntax):**
```bash
DOTNET="/c/Program Files/dotnet/dotnet.exe"
"$DOTNET" build Plutus/Endpoints/Plutus.DBService/Plutus.DBService.csproj -c Debug --nologo -v q
"$DOTNET" test tests/Plutus.Tests.Unit/Plutus.Tests.Unit.csproj -c Debug --nologo
"$DOTNET" test tests/Plutus.Tests.Integration/Plutus.Tests.Integration.csproj -c Debug --nologo
"$DOTNET" test tests/Plutus.Tests.Architecture/Plutus.Tests.Architecture.csproj -c Debug --nologo
```
Baseline before this plan: Unit 211 · Architecture 6 · Integration 31 — all green. Keep them green.

**EF migration (exact incantation — dotnet must be on PATH for the ef tool):**
```bash
export PATH="/c/Program Files/dotnet:$PATH"; export DOTNET_ROOT="/c/Program Files/dotnet"
"$DOTNET" ef migrations add <Name> \
  --project Plutus/Commons/Plutus.Entities \
  --startup-project Plutus/Data/Database.Migrations.Startup \
  --context MySqlDbContext -o Migrations/MySql
```
(The 8.0.10-vs-9.x version warning is expected. `ef migrations remove` needs a live DB — don't;
hand-edit migration + snapshot if you must undo.) Migrations auto-apply on backend startup.

**Frontend build (on the Mac).** SSH: `ssh -i ~/.ssh/plutus_mac_ed25519 admin@10.1.1.40`.
Remote shell is **zsh** (no unquoted word-splitting; `UID` is a reserved readonly variable; put
`export PATH=/opt/homebrew/bin:$PATH` first — node/npm/pm2/docker/mysql are not on the default
non-interactive PATH). Sync changed files with `scp`, then:
```bash
cd ~/PLUTUS/Plutus.Frontend.Portal && VITE_AUTH_MODE=oidc \
  VITE_OIDC_AUTHORITY=https://login.plutus.huggett.dscloud.me/realms/plutus \
  VITE_OIDC_CLIENT_ID=plutus-portal npm run build     # portal is OIDC mode now — env REQUIRED
cd ~/PLUTUS/Plutus.Frontend.WebApp && npm run build   # till stays password mode — no env
```
Deploy dist: `cp -r dist/. /srv/apps/PLUTUS/portal/current/` (portal) /
`/srv/apps/PLUTUS/web/current/` (till). Backend deploy: publish
`-c Release -r osx-arm64 --self-contained true`, tar, scp to `~/PLUTUS/staging/`, on the Mac:
pm2 stop → `mv ~/PLUTUS/backend ~/PLUTUS/backend.pre-<tag>` → extract → `chmod +x
backend/Plutus.DBService` → `pm2 restart plutus-backend --update-env` → poll
`curl -s -o /dev/null -w "%{http_code}" http://127.0.0.1:5100/swagger/v1/swagger.json` = 200.

**Hard rules:**
- **NEVER touch ETRIE** (same Mac, separate product). After any Mac change verify
  `curl -s -o /dev/null -w "%{http_code}" --resolve huggett.dscloud.me:443:127.0.0.1
  https://huggett.dscloud.me/health` → 200.
- **NEVER run `ops/keycloak/run-keycloak.sh`** — it recreates the container and wipes Matt's
  enrolled password/TOTP. Keycloak changes go via `kcadm.sh` inside the running container
  (`docker exec plutus-keycloak /opt/keycloak/bin/kcadm.sh config credentials --server
  http://127.0.0.1:8080 --realm master --user admin --password admin-change-me`).
- Deploy only when the operator asks; commit per-WP with the `Co-Authored-By: Claude` trailer.

**Codebase pitfalls (each has burned a session before):**
1. Every `SaveChangesAsync` on `MySqlDbContext` requires `db.CurrentUser = "<something>"` first —
   otherwise `ObjectIdMissingException: CurrentUser not defined!`.
2. Seeding a `Tenant` in tests requires `Entitlements = "[]"` AND `ConnectionRef = ""` (NOT NULL).
3. `StampAndGuardTenant` blocks cross-tenant writes: in integration tests either write as Kapow
   (`Plutus.Entities.Tenancy.KnownTenants.Kapow` — the ambient fallback) or build an unscoped
   context: `new MySqlDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<MySqlDbContext>>(),
   new FixedTenantContext(Guid.Empty))`.
4. New tenant-owned entities: real `Guid TenantId` property + add the CLR type to the
   `TenantOwned` array in `MySqlDbContext.cs` (~line 150) + entity config block. Global tables:
   do NOT add to the array; TenantId (if any) is plain data.
5. `perm:*` policies resolve from RBAC by userId, NOT token scopes. Test recipe: seed
   `RbacSeeder.EnsureBuiltInRolesAsync(db, Kapow)`, assign role ("Owner" carries all portal
   perms), mint `PlutusAppFactory.OperatorTokenFor(userId, "pos.sell")`. See
   `tests/Plutus.Tests.Integration/CommerceConfigE2eTests.cs` for a complete example.
6. `PlutusAppFactory` is the integration host (shared in-memory SQLite; hosted services removed).
   Platform-admin token: `PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin)`.
7. Secrets in config JSON are write-only via the `"__set__"` sentinel pattern — copy
   `PlatformNotificationsController` exactly if you need it.
8. Frontend: pages render as components via the `PAGES` map (never call page functions inline);
   money renders via `gbp()`/pence; dates append `"Z"` before `new Date(...)`.

**Exemplar files (copy these shapes, don't invent):**
- Global entity + keyed upsert store: `Plutus/Commons/Plutus.Entities/Models/CommercialOps.cs`
- Tenant-owned entity: `PaymentGatewaySettings` in `Models/CommerceConfig.cs`
- Platform-admin controller: `src/Plutus.Tenancy/Controllers/PlatformBillingController.cs`
- Tenant-facing perm-gated controller: `src/Plutus.Payments/GatewayConfigController.cs`
- Sweep + keyed alert: `ChurnSweep` in `src/Plutus.Tenancy/CommercialOps.cs`
- Portal screen with provider select + dynamic form: `NotificationsScreen` in
  `Plutus/Frontend/Plutus.Frontend.Portal/src/PlatformPage.tsx`
- Integration test w/ RBAC seeding: `tests/Plutus.Tests.Integration/CommerceConfigE2eTests.cs`

---

## 1. The security model — understand before OP1

`src/Plutus.Tenancy/HttpTenantContext.cs` resolves the ambient tenant per request:
- principal has `platform-admin` → **`Guid.Empty`** → the global query filter's bypass branch
  (`CurrentTenantId == Guid.Empty || TenantId == CurrentTenantId`) matches **every tenant's rows**;
- else `tid` claim if present; else **Kapow fallback** (Phase-1 single-tenant convenience — all
  current till/portal logins rely on it; do NOT remove it in this plan).

**Verified consequence:** an operator token GET `/api/v1/customers` returns 200 with cross-tenant
data. `perm:*`-gated endpoints correctly 403 (operator has no RBAC), but plain `[Authorize]`
tenant reads leak. Platform endpoints *rely* on `Guid.Empty` for cross-tenant reads — that must
keep working. Therefore the fix is a **route-level gate**, not a change to the filter mechanics.

---

## OP1 — Operator/client separation (SECURITY — do first)

### OP1.1 Backend: block operators from tenant-data routes
New file `src/Plutus.Web.Infrastructure/OperatorBoundary.cs`:
- Middleware `OperatorBoundaryMiddleware` (+ `UsePlutusOperatorBoundary()` extension, and
  registration comment pointing at `Plutus/Endpoints/Plutus.DBService/Startup.cs` — insert in
  `Configure` immediately AFTER `UseAuthentication`/`UseAuthorization` and BEFORE the request-health
  middleware, mirroring how `UsePlutusImpersonationAudit` is wired; find its call site and put this
  next to it).
- Logic, exactly:
  ```
  var user = context.User;
  if (user?.Identity?.IsAuthenticated == true
      && user.HasClaim("scope", PlutusPolicies.PlatformAdmin)   // HasClaim, NOT FindFirst — multi-scope tokens
      && user.FindFirst("tid") == null                          // no tenant identity…
      && user.FindFirst("impersonating") == null                // …and not impersonating
      && IsTenantDataPath(context.Request.Path))
  { context.Response.StatusCode = 403; write json {"detail":"Operators access client data via impersonation only."}; return; }
  await _next(context);
  ```
- `IsTenantDataPath`: path starts with `/api/` AND does NOT start with any of (case-insensitive):
  `/api/v1/platform`, `/api/v1/tenants`, `/api/v1/announcements`, `/api/Auth`,
  `/api/v1/support` (future, OP4 operator routes live under /platform anyway — this prefix is for
  the CLIENT ticket surface which operators must NOT hit; so do NOT allow-list it — leave it
  blocked), `/api/v1/payments/gateway/catalogue` (harmless metadata). Everything else under
  `/api/` is tenant data → blocked. Non-`/api/` paths pass.
- ALSO fix `HttpTenantContext.IsPlatformAdmin` to use
  `User?.HasClaim("scope", "platform-admin") == true` instead of `FindFirst("scope")` (first-claim
  order bug with multi-scope tokens).

### OP1.2 Portal: operator-only experience
`Plutus/Frontend/Plutus.Frontend.Portal/src/App.tsx`:
- Add `isOperatorOnly()` to `auth.ts`: `isPlatformAdmin() && !tokenClaims()?.tid` (mirror existing
  helpers; `tid` is absent for pure operators).
- In `App`: if operator-only → render ONLY the Platform surface: title "Plutus Operator",
  tabs = the PlatformPage sub-screens promoted to top level (simplest implementation: render
  `<PlatformPage/>` as the sole page and hide the client `TABS` entirely; do NOT restructure
  PlatformPage). Client users see exactly today's portal (Platform tab still appended when a
  tenant user also has platform-admin — rare; acceptable).
- Keep impersonation working: `beginImpersonation` swaps to an HMAC session token carrying `tid`,
  so the client chrome reappears during impersonation — that is correct and desired.

### OP1.3 Tests (`tests/Plutus.Tests.Integration/OperatorBoundaryE2eTests.cs`)
1. Operator token (`OperatorToken(PlutusPolicies.PlatformAdmin)`): `/api/v1/customers` → **403**;
   `/api/v1/platform/health` → 200; `/api/v1/tenants` → 200.
2. Plain staff token (`OperatorToken("pos.sell")`): `/api/v1/customers` → NOT 403-by-boundary
   (any of 200/401 per existing behaviour — assert `!= 403` is wrong; assert status equals what a
   pre-change probe shows: it's 200 today via Kapow fallback — assert 200).
3. Token with BOTH platform-admin and a `tid` claim (mint via `OperatorTokenFor` + `tid:` arg) →
   `/api/v1/customers` passes the boundary (tenant identity present).
4. Impersonation flow still works end-to-end (copy the arrange from `ImpersonationE2eTests`).
*DoD:* the four above green; full suites green; deployed; ETRIE 200. Matt re-tests: operator login
shows ONLY operator screens; client tabs gone; no tenant API returns data to his session.

---

## OP2 — Subscription plans & pricing

### OP2.1 Entity + migration (`AddSubscriptionPlans`)
`Models/CommercialOps.cs` — add GLOBAL entity `SubscriptionPlan`:
`Guid Id (Uuid7)`, `string Name` (max 100, unique index), `long PricePenceMonthly`,
`string EntitlementsJson` (nullable — JSON array of entitlement strings, same vocabulary as
`Tenant.Entitlements`), `bool Active`, `DateTime CreatedAtUtc`, `UpdatedAtUtc`, `string UpdatedBy`
(max 128). Register DbSet + config in `MySqlDbContext` next to `BillingSettings` (global — NOT in
`TenantOwned`). Add `Guid? PlanId` to `Tenant` (nullable — keeps existing free-text `Plan` for
display back-compat; do not drop it).

### OP2.2 Endpoints (`src/Plutus.Tenancy/Controllers/PlatformPlansController.cs`, platform-admin)
- `GET /api/v1/platform/plans` — list (include tenantCount per plan via a grouped count).
- `POST /api/v1/platform/plans` / `PUT /api/v1/platform/plans/{id}` — upsert, audited
  (`_db.Audit(Guid.Empty, Actor, "plan.set", ...)`), 400 on empty name, 409 on duplicate name.
- `DELETE` → only if no tenant references it, else 409.
- `PUT /api/v1/tenants/{id}/plan` body `{ planId }` — sets `Tenant.PlanId` AND copies
  `plan.Name` into `Tenant.Plan` + `plan.EntitlementsJson` into `Tenant.Entitlements` (so every
  existing entitlement read keeps working), audited. Null planId = unassign (leaves strings).
- Margin (16.3): in `PlatformCommercialController.Margin()`, revenue currently reads
  `TenantContracts.PricePenceMonthly`; change to: contract price if a contract row exists, else
  the assigned plan's `PricePenceMonthly`, else 0. (Contract = negotiated override; plan = list price.)

### OP2.3 Portal
`PlatformPage.tsx`: new sub-screen **Plans** (copy the `FlagsScreen` table+form shape): list
(name, £/mo, entitlements, active, #tenants), inline create/edit. Tenant detail: replace the
free-text plan display with a plan `<select>` (options from `fetchPlans()`) + "Assign plan"
button; show plan price beside it. `api.ts`: `fetchPlans/savePlan/deletePlan/assignPlan`.

### OP2.4 Tests
Plan CRUD + duplicate-name 409 + delete-in-use 409; assign plan → tenant list shows plan name and
entitlement reads (`GET /api/v1/tenants`) reflect the bundle; margin uses plan price when no
contract. *DoD:* create "Standard £99/mo" once → assign → shows on Subscribers + margin.

---

## OP3 — Subscribers (operator landing page)

Rename + extend, no new tables:
- `TenantsScreen` in `PlatformPage.tsx` → label "Subscribers", make it the default screen (it
  already is first). Add columns: plan price (OP2), renewal date (from `fetchContract` — N+1 is
  fine at this scale, or add a bulk `GET /api/v1/platform/contracts` returning all rows — prefer
  the bulk endpoint, platform-admin, trivial). Add an **MRR strip** above the table: sum of
  (contract price ?? plan price) over non-sandbox Active/Trial tenants + counts by status.
- Per-tenant users: new endpoint `GET /api/v1/platform/tenants/{id}/users` (platform-admin) —
  read-only list from the RBAC/user store: join `RbacRoleAssignments` (that tenant) → distinct
  userIds → employee name/email where resolvable (`People`/`WebCredentials` — follow how
  `AdminController` (src/Plutus.Identity) reads users for its list; reuse its query shape) + last
  portal login day from `TenantUsageRollups` (`logins.portal` — note: that metric is per-tenant
  not per-user; show it as tenant-level "last portal activity" instead of per-user if per-user
  isn't derivable — do NOT invent per-user tracking in this WP).
- Tenant detail: render the users list section.
*DoD:* operator landing answers: how many subscribers, plans/MRR, status mix, who's at risk
(signals badge already there), per-tenant user list visible. Tests: users endpoint 403 non-admin,
returns seeded RBAC users for Kapow.

---

## OP4 — Ticket system ("Ask for help")

### OP4.1 Entities + migration (`AddSupportTickets`)
`Models/Support.cs`:
- `SupportTicket` — **tenant-owned** (real TenantId; ADD to `TenantOwned` array):
  `Guid Id`, `Guid TenantId`, `string Subject` (max 200), `byte Status` (0 Open, 1 WaitingOnClient,
  2 Closed), `byte Severity` (0 Question, 1 Problem, 2 Urgent), `Guid RaisedByUserId`,
  `string RaisedByName` (max 100), `DateTime CreatedAtUtc`, `UpdatedAtUtc`,
  `string AssignedTo` (nullable, max 100 — operator display name). Index (TenantId, Status).
- `SupportMessage` — tenant-owned: `Guid Id`, `Guid TenantId`, `Guid TicketId` (index),
  `bool FromOperator`, `string AuthorName` (max 100), `string Body`, `DateTime AtUtc`.

### OP4.2 Client endpoints (`src/Plutus.Customers/SupportController.cs` — or a new
`src/Plutus.Tenancy/Controllers/SupportController.cs`; either module works, pick Tenancy)
Route `/api/v1/support/tickets`, ALL `[Authorize]` (any authenticated tenant user — cashiers may
ask for help), ambient tenant scoping does the isolation:
- `POST` `{subject, body, severity}` → creates ticket + first message (RaisedByName from
  `User.Identity.Name` fallback "user"); raises `IOperatorAlerter` alert keyed
  `ticket:{ticketId}` kind "ticket" (copy the alerter usage in `ChurnSweep.RaiseAsync`).
- `GET` → this tenant's tickets (newest first). `GET {id}/messages` → thread.
- `POST {id}/messages` `{body}` → appends client message, Status → Open (0), updates UpdatedAtUtc.
**OP1 interaction:** `/api/v1/support` is NOT in the operator allow-list — operators use the
platform routes below. Correct; leave blocked.

### OP4.3 Operator endpoints (in the same controller, platform-admin, cross-tenant)
Reads must bypass the tenant filter — the controller runs with `Guid.Empty` ambient context for
operators, and the query filter bypass makes `_db.SupportTickets` return all tenants. (For safety
use `.IgnoreQueryFilters()` explicitly like `PlatformController.Export` does.)
- `GET /api/v1/platform/tickets?status=` — all tenants, tenant name joined from `Tenants`.
- `POST /api/v1/platform/tickets/{id}/reply` `{body}` → operator message (`FromOperator=true`,
  AuthorName "Plutus support"), Status → WaitingOnClient, clears/updates the alert.
  ⚠ write path: the operator context is Guid.Empty and `StampAndGuardTenant` must not block —
  set the row's TenantId explicitly to the ticket's TenantId and save via an unscoped context
  (`FixedTenantContext(Guid.Empty)` on fresh options — copy `SandboxController`'s pattern for
  writes-as-platform).
- `PUT /api/v1/platform/tickets/{id}` `{status?, assignedTo?}` — audited.

### OP4.4 `support-heavy` signal (closes the WP16.1 seam)
In `ChurnSweep.EvaluateAsync` add: count this tenant's tickets created in the last 28 days
(`db.SupportTickets.IgnoreQueryFilters()`); threshold in `ChurnThresholds`
(`SupportHeavyTickets28d = 5`, pure + unit-tested like the others) → raise/clear
`TenantSignals.SupportHeavy` exactly like the other signals.

### OP4.5 Frontends
- **Client portal:** "Help" tab (last tab, always visible): ticket list + "New ticket" form
  (subject/severity/message) + thread view with reply box. Plain fetch helpers in `api.ts`.
- **Till (WebApp):** Settings page gains an "Ask for help" card → subject+message POST → "Ticket
  raised" confirmation (no thread UI on the till; keep it minimal).
- **Operator portal:** `PlatformPage.tsx` new sub-screen **Tickets**: inbox table (tenant, subject,
  severity chip, status, updated) with status filter, click → thread + reply + status/assign
  controls. Badge count of Open tickets on the tab label if cheap (`fetchTickets("open").length`).

### OP4.6 Tests
- Isolation: tenant A creates a ticket; tenant B token lists tickets → does not see it; operator
  lists → sees it with tenant id.
- Flow: client POST → operator alert raised (query `/api/v1/platform/alerts` or the OperatorAlerts
  table) → operator reply → client thread shows reply, status WaitingOnClient → client reply →
  Open again.
- Signal: seed 5 tickets in 28 days → ChurnSweep raises support-heavy; boundary unit tests on the
  threshold.
- Till path: authenticated pos.sell token can POST a ticket (any-auth check).
*DoD:* the full loop above green + visible in both UIs; suites green.

---

## Addendum (2026-07-28 self-review) — items added after a second pass

### OP1 additions
- **Escape hatch:** wrap the boundary in config —
  `OPERATOR_BOUNDARY_DISABLED=true` (pm2 env) bypasses the middleware. It's a security control on
  the live login path; if it ever mis-fires it must be switch-off-able without a rollback deploy.
  Default off (i.e. boundary ACTIVE). Log a startup warning when disabled.
- **Verify the allow-list, don't trust it:** for each allowed prefix, grep every controller on
  that route and confirm each action is `[Authorize(Policy = PlutusPolicies.PlatformAdmin)]` or
  anonymous-by-design (billing webhook, jobs HMAC report). Verified today: `/api/v1/tenants` has
  a single platform-gated action — re-verify at implementation time in case routes moved.
- **Operator actor identity (audit gap):** OIDC operators have no Plutus EmployeeId, so `Actor`
  resolves to `Guid.Empty` and every platform audit row Matt writes says actor
  `00000000-…`. Fix alongside OP1: in platform controllers' audit calls, include the operator's
  display name from the token (`User.Identity.Name` → `preferred_username`, fallback "operator")
  in the audit detail object. Cheap: add a shared `ActorName` property (copy the one in
  `WebstoresController`) to the platform controllers as they're touched; do NOT invent a new
  identity system.
- **"My account" link:** in the operator-only header, link "Account & MFA" →
  `https://login.plutus.huggett.dscloud.me/realms/plutus/account` (password/TOTP self-service —
  answers "can I change my password" permanently).

### OP2 addition
- **Precedence note:** `TenantEntitlementOverride` (WP14.2 grant/deny) still wins over the plan
  bundle, and `PlatformFlag` kill switches win over everything — `Entitlements.ComputeEffective`
  already implements this; assigning a plan only rewrites the tenant's BASE entitlements. Don't
  touch the compute path.

### OP4 additions
- **Anti-spam cap:** max 20 OPEN tickets per tenant → `POST` returns 409 with a friendly detail.
  (The per-tenant rate limiter already throttles request floods; this caps queue abuse.)
- **Explicit non-goals for this WP:** no attachments, no email notifications (framework exists
  but is provider-less until 17.3 un-parks), no SLA timers, no ticket categories. Resist scope.
- **Retention:** tickets are business records — NOT added to the retention sweeper. Closed-ticket
  archival is a future decision.

### Ops hardening (schedule with OP1, it protects the login itself)
- **Keycloak persistence:** the container is stateless — a `docker rm`/re-run (or image upgrade)
  wipes enrolled passwords/TOTP back to the seed; only the big warning in `run-keycloak.sh`
  protects it today. Fix: add a named volume for Keycloak's H2 data dir
  (`-v plutus-keycloak-data:/opt/keycloak/data/h2`) in `run-keycloak.sh` — **coordinate the
  one-off recreation with Matt** (he must re-enrol once), then his state survives restarts and
  upgrades. Note: `--import-realm` skips import when the realm already exists in the volume, so
  future realm-JSON changes are applied via `kcadm` (or a deliberate volume reset).

## OP5 — later / gated (do NOT build in this plan)
Self-serve signup (needs OP2 + billing adapter); billing automation (16.4 reaction exists — needs
a provider account); client-side "banking connectors" (client-configured, payment-gateway
pattern); per-user login tracking (would extend WP13.1 metering).

## Execution order & sizing
| WP | Size | Migration | Deploy risk |
|---|---|---|---|
| OP1 | S (1 middleware + portal gate + tests) | none | low — additive 403s; verify staff tokens unaffected |
| OP2 | M | AddSubscriptionPlans | low |
| OP3 | S–M | none (unless bulk contracts endpoint) | low |
| OP4 | L (new subsystem, 2 entities, 3 UIs) | AddSupportTickets | medium — new tenant-owned tables |

After each WP: run all three suites, compile BOTH frontends on the Mac (portal needs the OIDC env
vars), commit with a descriptive message, and deploy only when the operator asks. Update
`HANDOVER.md`'s RESUME section and this file's checkboxes:

- [ ] OP1 backend boundary + portal separation + tests
- [ ] OP2 plans & pricing
- [ ] OP3 subscribers landing
- [ ] OP4 tickets end-to-end + support-heavy signal
