# Plutus Operator Platform — Implementation Plan

**For execution by Claude Sonnet, one work package (WP) at a time.**
**Authority:** `Plutus Operator Platform.md` (repo root — the requirements doc) and
`plutus-platform-architecture.md` (v3). If this plan and the architecture doc conflict, the
architecture doc wins; stop and flag the conflict.
**Continues:** `plutus-implementation-plan.md` (phases 0–12, complete except externally-gated
tails). Phase numbering continues from there: **Phases 13–18.**

---

## How to use this plan

- Execute WPs in order within a phase. Phase 13 must land first — 14 and 16 read its data.
  Phases 15, 17, 18 are independent of each other once 13 exists.
- Each WP has a **Definition of Done (DoD)**. Do not start the next WP until the current DoD
  passes, including tests.
- Do not expand scope. If a WP reveals missing groundwork, stop and report rather than
  improvising schema or contract changes.
- **All global engineering rules from `plutus-implementation-plan.md` apply unchanged** —
  isolation (D20), tenancy (D2), integer pence, UTC `datetime(6)`, UUIDv7, immutability (D5),
  minimal dependencies (rule 7: react + react-dom only, no new backend packages), testing floor,
  `/api/v1` additive-only, structured logging. Re-read them before starting.
- The permanent cross-cutting suites (WP1.7: tenant isolation, money reconciliation,
  idempotency) must stay green through every WP here. Several WPs below add operator-facing
  endpoints that read **across** tenants — these are `platform-admin` gated and must be
  explicitly exempted in the isolation suite *by policy check*, never by skipping the endpoint.

## Progress board

**Legend:** ⬜ not started · 🔨 in progress · ✅ done · ⏸ externally gated. Updated as each DoD
passes (Claude keeps this current — single source of truth for status).

| WP | Title | Status | Note |
|---|---|---|---|
| 13.1 | Usage metering | ✅ | Done 2026-07-28 (net10). TenantUsageRollup + all feeds: event-fed sales.* consumer + rebuild, nightly counted-metrics sweep (stores/tills/users.active, storage.rowsSalesV2), login hooks (logins.portal via AuthController raw upsert, logins.till via EnrolmentService — best-effort). /platform/usage(+summary,+rebuild) platform-admin endpoints + migration. Tests: Unit 172 (fold, rebuild==incremental, sweep), Integration 7 (403/200 gate). Fixed a shared-SQLite startup race by disabling RetentionSweeper in the test factory (like OutboxDispatcher). api.requests deferred to WP13.2. |
| 13.2 | Per-tenant request health | ✅ | Done 2026-07-28 (net10). TenantRequestStats + middleware (after-auth, in-memory (tenant,route-group) accumulator + fixed-bucket latency histogram, bounded route groups) → per-minute flusher (unscoped ctx, MySQL-only) writing rows + folding api.requests (closes the WP13.1 api.requests feed). 35-day retention in sweeper. /platform/health (last-hour per-tenant error rate + peak p95 + quarantine depth + consumer lag) + /platform/health/{tenantId} drill-down, platform-admin. Tests: Unit 181 (accumulator A-not-B, percentiles, flush+fold, purge, <1ms overhead), Integration 8 (403/200 gate). Flusher removed in the test factory (shared-SQLite race), integration 5/5 deterministic. |
| 13.3 | Job heartbeats + alerting seam | ⬜ | |
| 13.4 | Operator dashboard (portal) | ⬜ | |
| 13.5 | Resource controls (rate-limit + quotas) | ⬜ | Added 2026-07-27 (req §3). |
| 14.1 | Support impersonation | ⬜ | |
| 14.2 | Feature flags & kill switches | ⬜ | |
| 14.3 | Sandbox & demo tenants | ⬜ | |
| 15.1 | Announcements | ⬜ | |
| 15.2 | Status page + SLA figures | ⬜ | |
| 15.3 | Per-tenant restore | ⬜ | |
| 16.1 | Churn signals | ⬜ | |
| 16.2 | Contract & renewal tracking | ⬜ | |
| 16.3 | Margin view | ⬜ | |
| 16.4 | Dunning automation | ⏸ | Gated: billing-provider choice. |
| 16.5 | Cross-tenant product analytics | ⬜ | Added 2026-07-27 (req §6). |
| 17.1 | Connector health framework | ⬜ | |
| 17.2 | Payment gateway health | ⏸ | Gated: Phase 7 payments. |
| 17.3 | Email/SMS seam + deliverability | ⏸ | Gated: no platform mailer. |
| 18.1 | Operator MFA/SSO | ⏸ | Gated: `login.plutus` vhost. |
| 18.2 | Residency & DPA registry | ⬜ | |
| 18.3 | Incident-response runbook | ⬜ | |

## What already exists (do not rebuild)

| Capability | Where |
|---|---|
| Tenant lifecycle (Active/Suspended/Closed, D16), entitlements + plan | `Plutus.Tenancy` — `TenantLifecycleService`, `EntitlementService`, `PlatformController` |
| Provisioning (tenant + admin + Company/Store), till enrolment | `ProvisioningService`, `EnrolmentService` |
| Offboarding: export ZIP, `DeletionSchedule`, `RetentionSweeper` (timer host) | `Plutus.Tenancy` (WP10.3/10.4) |
| Billing seam (`IBillingProvider`, HMAC webhook) — **concrete adapter deferred** | `Billing.cs` (WP10.1) |
| Outbox dispatcher + per-consumer cursors (`ConsumerOffsets`), consumer lag | SharedKernel/dispatcher (WP1.5) |
| Rollups (till/day grain) + rebuild | `Plutus.Reporting` (WP3.3) |
| RBAC + `platform-admin` policy + audit | WP3.1/3.2, `AuthPolicies.PlatformAdmin` |
| Woo connector: inbound webhooks + self-healing poll + outbound dry-run journal | `Plutus.Webstore` (Phase 6) |
| Nightly MySQL backups (restore-rehearsed), pm2 resurrect, logrotate | WP12.3 (launchd, Mac mini) |
| Staging schema `plutus_t1` | Mac mini MySQL |

## Externally gated (build the seam, defer the concrete — same pattern as WP7.1/10.1)

- **WP16.4 dunning automation** → needs Matt's billing-provider choice (Stripe/Paddle, Phase 10 tail).
- **WP17.2 payment-gateway health** → needs Phase 7 payments landed.
- **WP17.3 email/SMS deliverability** → no platform mailer exists yet; land the seam only.
- **WP18.1 operator MFA/SSO** → needs the `login.plutus` Caddy vhost (Matt's sudo, Phase 9 tail).

---

## Phase 13 — Operator visibility (metering, health, job monitoring)

> The requirements doc's #1 priority: *"everything commercial and operational hangs off this
> data."* Order: 13.1 and 13.2 are independent; 13.3 before 13.4 (the dashboard displays it).

**WP13.1 — Usage metering.**
New table `TenantUsageRollup` (`TenantId`, `BusinessDay`, `Metric` varchar, `Value` bigint;
PK `(TenantId, BusinessDay, Metric)`). Metrics v1: `sales.count`, `sales.grossPence`,
`api.requests`, `logins.portal`, `logins.till`, `stores.active`, `tills.active`, `users.active`,
`storage.rowsSalesV2`. Two feeds:
- **Event-fed:** a new outbox consumer (registered like the rollup consumer, own cursor) folds
  `SaleRecorded` into `sales.*`; login events (portal + till token issue) increment `logins.*`
  via the same helper.
- **Nightly sweep:** extend the `RetentionSweeper` timer host with a metering pass computing
  the counted metrics (`stores.active`, `tills.active`, `users.active`, `storage.*`) per tenant.
`api.requests` comes from WP13.2's middleware (buffered in-memory, flushed per minute — never a
DB write per request). Endpoint: `GET /api/v1/platform/usage?tenantId=&from=&to=&metric=`
(`platform-admin`), plus `GET /api/v1/platform/usage/summary` (all tenants, latest 30 days —
the dashboard's one-call feed). Rebuild command replays `sales.*` from `Sales` (same pattern as
the WP3.3 rollup rebuild).
*DoD:* a day of simulated trading across two tenants produces per-tenant rows matching direct
SQL to the penny/count; rebuild equals incremental; non-platform-admin token → 403; isolation
suite green (endpoint exempted by policy check).

**WP13.2 — Per-tenant request health.**
Middleware in `Plutus.Web.Infrastructure` (after auth, so `TenantId` is resolved): per
`(TenantId, route-group)` in-memory accumulators — request count, 4xx, 5xx, latency
histogram (fixed buckets, no library). Flushed per minute to `TenantRequestStats`
(`TenantId`, `MinuteUtc`, `RouteGroup`, `Count`, `Err4xx`, `Err5xx`, `P50Ms`, `P95Ms`,
`MaxMs`); a retention pass in the sweeper purges past 35 days. Consumer lag per tenant is
already derivable from `ConsumerOffsets` — expose it. Endpoints (`platform-admin`):
`GET /api/v1/platform/health` → per-tenant last-hour error rate + p95 + consumer lag + quarantine
depth (from `SaleQuarantine`); `GET /api/v1/platform/health/{tenantId}?from=&to=` for drill-down.
*DoD:* a burst of 500s against tenant A shows in A's stats and not B's; middleware overhead
< 1 ms p95 (benchmark test); minute flush survives an app restart losing at most one minute;
35-day purge proven.

**WP13.3 — Job heartbeats + alerting seam.**
New table `JobRuns` (`Id` UUIDv7, `JobName`, `TenantId` nullable — null = platform-wide,
`StartedAtUtc`, `FinishedAtUtc` nullable, `Status` Running|Succeeded|Failed, `Detail` varchar).
SharedKernel helper `IJobHeartbeat.Track(jobName, tenantId?, work)` — wrap every background
job: outbox consumers (per-batch), the sweeper's passes, the Woo poll (per tenant), the metering
pass. Expected-cadence registry (code-defined, like the permission catalogue): job name →
max-silence window. A sweeper pass evaluates it: any job silent past its window, or last run
Failed, raises an **alert** through a new seam `IOperatorAlerter` (default implementation:
structured log at Error + an `OperatorAlerts` table the dashboard reads; email/webhook adapters
later — do NOT build them now). The nightly launchd backup reports in via a one-line curl to
`POST /api/v1/platform/jobs/report` (HMAC-signed, same scheme as the billing webhook).
*DoD:* killing the Woo poll for one tenant raises exactly one alert naming job + tenant within
one sweep; a Failed run alerts once (no repeat spam — alert row is keyed and updated);
`JobRuns` retention purges past 35 days except latest-per-job; backup script reports and shows.

**WP13.4 — Operator dashboard (portal, platform-admin area).**
New top-level portal section **Platform** — rendered only when the token carries
`platform-admin` (tenant users never see the tab; direct-URL access is API-403'd anyway, D20).
**Single login surface:** operators sign in on the SAME portal login page at
`admin.plutus.…` as tenant users — no separate operator app, host, or login route. The
existing login already bakes effective scopes into the token; a platform-admin account simply
comes back with the extra scope and the portal reveals the Platform section. Nothing about the
login page changes in this WP.
Same frontend discipline as every portal screen (react + react-dom, one CSS file, hand-rolled
SVG charts, generated TS types). Screens, in order:
1. **Tenants** — list (exists: `GET /api/v1/tenants`) enriched with last-30-day usage sparkline
   (WP13.1 summary) + health dot (WP13.2) + status/plan; row → tenant detail (usage, health
   drill, entitlements editor, status control, export/deletion — all existing endpoints).
2. **Health** — per-tenant error/latency/lag table, quarantine depth, `OperatorAlerts` feed.
3. **Jobs** — `JobRuns` latest-per-job grid with cadence status.
*DoD:* one screenful answers "is anyone having a bad day?" (Matt's acceptance); a tenant-scoped
login sees no Platform tab and gets 403 on every `/api/v1/platform/*` route (isolation suite);
drill from tenant list → single tenant's p95 chart works on live test-env data.

**WP13.5 — Resource controls (rate limiting + quotas). [ADDED 2026-07-27 — requirements §3
"enforcement to match the visibility"]**
The enforcement half of WP13.2's visibility. Two mechanisms, both **entitlement-driven** so
WP14.2 overrides tune them with no deploy and no token reissue:
- **Per-tenant rate limiting:** the ASP.NET Core built-in partitioned rate limiter (shared
  framework — NO new package, rule 7 holds), partitioned by resolved `TenantId`, registered
  after auth in `Plutus.Web.Infrastructure` (same seam as WP13.2's middleware, ordered before
  it so throttled requests don't skew stats). Limit sourced from a new entitlement
  `ratelimit.rps` (per-plan default; override per tenant). Breach → `429` with `Retry-After`.
  `platform-admin` and device/till token paths exempt (a busy till must never be throttled —
  D16 keeps tills selling). Noisy-neighbour isolation is the point: tenant A's flood throttles
  A only.
- **Provisioning quotas:** a central `IQuotaGuard` (SharedKernel) checked in
  `ProvisioningService`/`EnrolmentService` at store/user/till creation against entitlement
  limits (`stores.max`, `users.max`, `tills.max`), using WP13.1's counted metrics
  (`stores.active`/`users.active`/`tills.active`) as the current count. Over-limit → clean
  `409` with the limit named, never a 500. Unlimited = entitlement absent.
Surfaced on the Platform tenant detail: current usage vs each limit (WP13.1 feed), limits
editable through the existing entitlement/override path (WP14.2), not a parallel API.
*DoD:* a request flood against tenant A returns 429 for A while B stays 200 (isolation, not
global); a till token is never throttled; creating the (limit+1)th store is refused naming the
limit while the Nth succeeds; lowering a limit via a WP14.2 override takes effect with no
restart; `platform-admin` exempt; isolation + money suites green. *(Ordering: after WP13.2;
the override-driven limit tuning DoD line lands green once WP14.2 exists — until then test with
plan defaults.)*

---

## Phase 14 — Support & rollout controls

**WP14.1 — Support impersonation.**
`POST /api/v1/platform/tenants/{id}/impersonate {userId, minutes≤60}` (`platform-admin`): mints
a token via the existing HMAC issuer with the target user's effective RBAC scopes **minus a
deny-list** (`pos.refund`, `portal.users.manage`, anything destructive — code-defined list),
plus claims `impersonating=true`, `actor=<operator guid>`, short expiry, **no refresh**. Audit
on mint AND on every authenticated request carrying the claim (request-log enrichment, cheap).
Portal: an "Impersonate" action on the Platform → tenant → users list opens the portal in the
tenant's context with a fixed red banner ("Viewing as X — actions audited") and a Stop button.
Works identically under Keycloak later — impersonation stays on the HMAC device-token path the
policy-scheme already routes (Phase 9 design), so no IdP dependency.
*DoD:* impersonated session sees exactly the target's portal (matrix-tested against WP3.1
scopes); denied permissions 403 even though the real user has them; token dies at expiry with
no refresh; audit trail reconstructs the full session; minting requires `platform-admin` and is
itself audited.

**WP14.2 — Feature flags & kill switches.**
Extend the entitlement model (WP10.1) rather than adding a parallel system: entitlement entries
gain a **source** dimension — `plan` (billing-written, exists today) vs `override` (operator-set:
beta grants, temporary disables). Effective = plan ∪ overrides, deny-override wins.
New: `PlatformFlags` table for **global kill switches** (`FlagName`, `Enabled`, `Reason`,
audited) checked before tenant entitlements — one switch turns a feature off for everyone
(e.g. `woo-outbound`). `IEntitlementService` signature unchanged; callers don't know.
Endpoints: `PUT /api/v1/platform/tenants/{id}/overrides`, `PUT /api/v1/platform/flags/{name}`
(`platform-admin`, audited). Surfaced on the Platform tenant detail + a Flags screen.
*DoD:* beta-grant to one tenant flips their gated endpoint 403→200 while others stay 403; a
kill switch beats a plan entitlement for every tenant instantly (no restart, no token reissue);
billing webhook writes still work untouched; audit rows for every change.

**WP14.3 — Sandbox & demo tenants.**
`IsSandbox` flag on `Tenants` (provisioning parameter + settable via the platform API).
Consequences, enforced centrally: excluded from commercial rollups/usage summaries by default
(dashboard toggle to include), watermarked in the portal chrome ("SANDBOX"), and a
**reset** action — `POST /api/v1/platform/tenants/{id}/reset` (sandbox-only, hard-guarded)
that truncates the tenant's transactional rows and re-runs a demo seed (small deterministic
catalogue + 30 days of generated sales through the REAL ingest endpoint, so projections/rollups
populate the honest way). Provision one permanent demo tenant on the test env for sales demos
and pre-release checks; staged rollouts (WP14.2 overrides) deploy here first.
*DoD:* reset on a sandbox restores it to the identical seeded state (row counts + penny totals
deterministic); reset on a non-sandbox tenant is impossible (guard + test); sandbox sales never
appear in Platform usage summaries unless toggled; demo tenant live on the test env.

---

## Phase 15 — Comms & trust

**WP15.1 — Announcements.**
`PlatformAnnouncements` (`Id`, `Severity` Info|Maintenance|Incident, `Title`, `Body`,
`StartsAtUtc`, `EndsAtUtc`, `TenantIds` JSON nullable = all, `CreatedBy`, audited).
`GET /api/v1/announcements/active` — **tenant-scoped, any authenticated token** (portal and
till both poll it on their existing sync cadence; no new push machinery). Portal renders a
dismissible banner (dismissal in localStorage keyed by announcement id — allowed; it's not the
API token); the till shows Maintenance/Incident severities only, as a banner like the existing
pick-from-floor one. Authoring UI on the Platform section.
*DoD:* a targeted announcement reaches only the targeted tenant's portal + till within one sync
cycle and disappears at `EndsAtUtc`; dismiss survives reload; isolation suite covers the
tenant-scoping of `active`.

**WP15.2 — Status page + SLA figures.**
Two halves:
- **Public status page:** a static page (own tiny host/vhost, e.g. `status.plutus.…` — Caddy
  block staged for Matt's sudo like previous vhosts) fed by a JSON file the backend writes
  each minute (`/health` self-check + WP13.2 aggregate error rate + open Incident-severity
  announcements). Static file = it stays up when the backend is the thing that's down (write a
  stale-timestamp warning into the page's JS).
- **SLA figures:** monthly per-tenant availability computed from `TenantRequestStats`
  (minutes where 5xx-rate < threshold ÷ minutes with traffic) → `GET
  /api/v1/platform/sla?tenantId=&month=`, shown on the Platform tenant detail. Advisory-grade,
  documented as such (test env, single box).
*DoD:* stopping the backend leaves the status page reachable and it flags staleness within two
minutes; a seeded month of stats yields a deterministic, hand-checkable percentage; Caddy block
committed + staged, ETRIE untouched.

**WP15.3 — Per-tenant restore.**
Tooling + rehearsed runbook, not a new backup system (nightly dumps exist, WP12.3):
`tools/Plutus.TenantRestore` — given a nightly dump + `TenantId`: load dump into a scratch
schema (`plutus_restore`), extract that tenant's rows from every tenant-owned table (enumerate
via the EF model, same source of truth as the WP10.3 exporter — never a hand-kept list), emit
an import script targeting live. Two modes: `--verify` (default: counts + penny totals vs live,
no writes) and `--apply` (explicit, immutability-aware: event tables INSERT-only-missing, never
UPDATE — D5). Rehearse on the test env against the demo tenant (WP14.3) and record the
rehearsal in HANDOVER like the WP12.3 backup rehearsal.
*DoD:* delete the demo tenant's rows on `plutus_t1`, restore from last night's dump → row counts
and penny totals match pre-delete; other tenants' rows byte-untouched (checksum before/after);
runbook committed; rehearsal recorded.

---

## Phase 16 — Commercial operations

**WP16.1 — Churn signals.**
A sweeper pass computes per-tenant flags from WP13.1 data (thresholds code-defined, tuned
later): `usage-declining` (28-day sales count down >30% vs prior 28), `gone-quiet` (no portal
login 14 days), `support-heavy` (placeholder until a ticket source exists — seam only).
Written to `TenantSignals` (`TenantId`, `Signal`, `RaisedAtUtc`, `ClearedAtUtc` nullable),
raising an `IOperatorAlerter` alert on raise (once, keyed — WP13.3 pattern). Platform tenants
list gains a signals column.
*DoD:* seeded declining usage raises exactly one signal that clears when usage recovers;
thresholds unit-tested at boundaries; visible on the dashboard.

**WP16.2 — Contract & renewal tracking.**
`TenantContracts` (`TenantId`, `RenewalAtUtc`, `TermMonths`, `PricePenceMonthly`, `Notes`
varchar, audited) — deliberately thin; the billing provider owns money truth once it exists,
this tracks the *relationship* (negotiated terms, renewal dates) which no provider webhook
carries. CRUD on the Platform tenant detail (`platform-admin`). Sweeper raises a
`renewal-due` signal (WP16.1 table) 60/30/7 days out.
*DoD:* renewal 30 days out shows on the dashboard and alerts once per threshold; audit on edits.

**WP16.3 — Margin view (config-driven, deliberately simple).**
One box, shared infra: no per-tenant cloud billing to attribute. Config file
(`platform-costs.json`: monthly infra total + optional per-tenant direct costs) + WP13.1 usage
shares → `GET /api/v1/platform/margin` (per tenant: revenue from WP16.2 price, attributed cost
by usage share, margin). A screen on Platform. Revisit with real attribution if/when tenants
get isolated resources — do NOT build metering-based cost allocation now.
*DoD:* hand-computed example matches the endpoint to the penny; missing config → clean empty
state, not 500.

**WP16.4 — Dunning automation.** ⏸ **GATED on the billing-provider choice (Phase 10 tail).**
When the concrete adapter lands: provider dunning does the retries; this WP is only the
platform reaction — webhook events map to grace-period → `Suspended` (D16 already keeps tills
selling) → alert + dashboard state. Do not build until the adapter exists.

**WP16.5 — Cross-tenant product analytics (anonymised). [ADDED 2026-07-27 — requirements §6
"drives the roadmap with evidence rather than the loudest client"]**
Read-only, aggregate-only, **never per-tenant** in any response. Feeds entirely off existing
data — no new event capture: WP13.1 `TenantUsageRollup` (feature adoption/volume) + WP13.2
`TenantRequestStats` route-group counts (which surfaces are exercised, and a coarse drop-off
funnel: `logins.portal` → sale-started → `sales.count`). Computed on-read by summing across
tenants with a **k-anonymity floor** (a metric is suppressed unless ≥ *k* tenants contribute,
*k* code-defined, default 3) so no single tenant is re-identifiable from an aggregate.
Endpoint `GET /api/v1/platform/analytics?from=&to=` (`platform-admin`): per-feature adoption
(# tenants using, total uses), route-group activity, the funnel. A Platform → Analytics screen
(same frontend discipline). Sandbox tenants (WP14.3) excluded by default.
*DoD:* adoption/volume across ≥2 tenants equals the direct cross-tenant SQL sum; a metric with
only 1 contributing tenant is suppressed (k-anonymity proven at the boundary); **an automated
test asserts no response field carries a `TenantId` or tenant name** (anonymity is enforced,
not just intended); `platform-admin` gated (non-platform 403); isolation suite green with the
endpoint exempted by policy check. *(Depends on Phase 13 data existing.)*

---

## Phase 17 — Integration health & deliverability

**WP17.1 — Connector health framework (generalise the Woo pattern).**
Extract from `Plutus.Webstore` into SharedKernel: `ConnectorRuns` (per connector, per tenant:
last poll, last webhook, last outbound, error streak — feeding `IJobHeartbeat` so WP13.3
alerting covers connectors for free) + the outbound journal/retry-queue shape as a reusable
base. Woo becomes the first consumer of the extracted base (behaviour byte-identical —
snapshot-test the journal). Per-tenant connector status surfaces on the existing portal
Webstore tab (tenant-facing) and the Platform health screen (operator-facing).
*DoD:* Woo behaviour unchanged (existing Phase 6 tests + journal snapshot green); a second
dummy connector registers in <50 lines and inherits health/retry/journal; connector silence
raises a WP13.3 alert naming connector + tenant.

**WP17.2 — Payment gateway health.** ⏸ **GATED on Phase 7 payments.**
When gateways exist: per-tenant payment success-rate tracking (decline rate over trailing
window vs baseline) → `gateway-degraded` signal + alert. Spec only until then. *They will blame
Plutus before their acquirer* — this WP is the requirements doc's warning made operational.

**WP17.3 — Email/SMS seam + deliverability.** ⏸ **GATED — no platform mailer exists.**
Land the seam only: `IMessageSender` (SharedKernel; null default) with per-tenant sending
identity in the model from day one (the requirements doc's isolation warning — one tenant's
campaign must never poison a shared domain), and a `MessageEvents` table shape for
sent/bounced/complained ready for a provider webhook. Concrete adapter + deliverability
dashboard when a mailer is chosen. Core keeps zero reference to any concrete provider
(arch test, same as billing).
*DoD (seam):* arch test green; a fake adapter round-trips send → bounce-webhook → event row
with tenant attribution.

---

## Phase 18 — Operator security & compliance

**WP18.1 — Operator MFA/SSO.** ⏸ **GATED on the `login.plutus` Caddy vhost (Matt's sudo).**
Once Keycloak is reachable: platform-admin scope issued **only** via the `keycloak` IdP path
(the WP9.1 claims-transformation gains the rule: HMAC `test` logins never carry
`platform-admin`), and the realm requires TOTP for members of the operators group (realm export
updated + committed). Device/till tokens and tenant users unaffected.
**Still the same login page:** this does NOT introduce a separate operator login. The portal's
one login page (WP9.4 already plans this) offers email/password plus a "Sign in with SSO"
button on the same screen; an operator uses the SSO path from that page, lands back in the same
portal, and sees the Platform section. Tenant users keep using whichever path their deployment
configures.
*DoD:* HMAC login for an operator account authenticates but carries no `platform-admin` scope
(Platform tab gone, `/api/v1/platform/*` 403); Keycloak login without TOTP enrolment is forced
to enrol; realm export reproduces it.

**WP18.2 — Residency & DPA registry.**
Fields on the tenant record (`DataRegion` — constant `UK` today but modelled now,
`DpaSignedAtUtc` nullable, `DpaRef` varchar) + shown on Platform tenant detail; the WP10.3
export already provides the portability half; the WP10.4 sweeper already does retention
deletion — this WP just makes the compliance state *visible* and flags tenants with no DPA
via a WP16.1-style signal.
*DoD:* fields round-trip via API + UI, audited; missing-DPA signal raises on the dashboard.

**WP18.3 — Incident-response runbook.**
A committed doc (`ops/incident-runbook.md`), not code: severity ladder, first-15-minutes
checklist per failure class (backend down, DB down, connector storm, suspected breach —
including the token/secret rotation steps already scattered in `Build/secrets.local.md` and
HANDOVER), who/what to notify (status page + WP15.1 announcement templates), and the rollback
levers this codebase already has (bridge flag pattern, pm2 ecosystem backups, nightly dumps,
WP15.3 tenant restore). Rehearse one tabletop scenario and record it.
*DoD:* doc committed; one scenario walked end-to-end on the test env (backend kill → status
page flags → announcement posted → recovery → alert clears) and recorded in HANDOVER.

---

## Suggested execution order (mirrors the requirements doc's priorities)

1. **Phase 13 complete** (13.1→13.4, then **13.5** which needs 13.1/13.2) — the data layer
   everything else reads plus its enforcement half.
2. **WP14.1 impersonation** — immediate support payoff, no dependencies beyond 13.4's surface.
3. **WP14.2 → 14.3** — flags, then sandbox (sandbox uses overrides for staged rollout; WP13.5's
   override-driven limit tuning also lands green here).
4. **Phase 15** (15.3 restore early — it's cheap and it's the one you regret not having).
5. **Phase 16** buildable halves (16.1–16.3, **16.5** analytics), **Phase 17.1**,
   **Phase 18.2–18.3**.
6. Gated tails (16.4, 17.2, 17.3 concrete, 18.1) as their gates open.
