# Plutus Operator Platform

Platform-level administration for operating Plutus as a multi-tenant service. Each client (till + portal deployment) is a tenant; this layer sits above all tenants and is used by the platform operator only.

---

## 1. Tenant Lifecycle

### Provisioning
- Self-serve or one-click tenant creation: database/schema, subdomain, branding, and seed data spun up without manual work.
- Onboarding checklist per tenant (payment method, sites configured, users invited, go-live).

### Suspension & Offboarding
- Graceful degradation for non-payment: read-only mode before full cutoff.
- Clean offboarding: full data export for the client, then automated deletion after the contractual retention period.

### Plans & Entitlements
- Features, site limits, and user limits toggled per tenant from the admin panel — upgrades and downgrades without a deploy.
- Entitlement checks enforced in the application, managed centrally.

---

## 2. Commercial

### Subscription & Billing Management
- Plan, billing status, invoice history, and payment method per tenant.
- Dunning: automated retries and escalation emails for failed payments, with a queue of accounts needing manual chasing.

### Usage Metering
- Per-tenant metrics: transactions processed, API calls, storage, active sites, active users.
- Supports usage-based billing and identifies tenants outgrowing their plan (upsell signals).

### Margin Visibility
- Infrastructure cost attributed per tenant vs revenue per tenant.
- Identifies unprofitable tenants and informs pricing.

### Churn Signals
- Declining transaction volume, reduced logins, support ticket spikes.
- Falling usage predicts cancellation before the email arrives — surface it early.

### Contract & Renewal Tracking
- Renewal dates, negotiated terms, and price increases due, per tenant.
- Surfaced alongside usage/churn data so renewals are approached informed.

---

## 3. Operations

### Per-Tenant Health Dashboard
- Error rates, response times, and job queue depth broken down **by tenant**, not just aggregate infrastructure load.
- Noisy-neighbour detection: one tenant hammering the system looks like "general slowness" without this.

### Resource Controls
- Rate limiting and resource quotas per tenant — enforcement to match the visibility.

### Support Impersonation
- Log into any tenant's admin as them, fully audited.
- The single biggest reducer of support resolution time.

### Version & Migration Management
- Track which tenants are on which schema/config version.
- Staged rollouts: deploy to a small set of friendly tenants before all.

### Feature Flags
- Scoped per tenant: beta features for some, kill switches for all.

---

## 4. Communications & Trust

### Status Page & Announcements
- Public status page plus in-app announcements for maintenance, incidents, and new features — no manual mass emails.

### SLA Monitoring
- Per-tenant uptime figures where contracts promise them.

### Backup & Restore
- Per-tenant restore: recover one client's data without touching the others.
- Restores tested regularly, not just backups taken.

---

## 5. Payments & Integrations

### Payment Gateway Management
- Each tenant runs their own merchant account/gateway credentials: central credential management with rotation.
- Per-tenant gateway health monitoring — alert when a tenant's payment success rate drops. They will blame Plutus before their acquirer.

### Webhook & Integration Health
- Delivery monitoring and retry queues per tenant for outbound integrations (accounting, ecommerce, etc.).
- The Webstore (Woo) connector pattern — inbound webhooks + self-healing poll, outbound with a dry-run journal — is the template to generalise for future connectors.

### Email/SMS Deliverability
- Receipts and loyalty comms are sent on behalf of tenants: monitor sender reputation, bounce rates, and blacklisting.
- One tenant's spammy campaign must not poison a shared sending domain — per-tenant sending identities or isolation.

---

## 6. Platform Engineering

### Scheduled Job Monitoring
- Per-tenant visibility of background work: outbox consumers (rollups, stock ledger, webstore outbound), end-of-day reconciliations, subscription renewals, connector polls, backups.
- A silently failed job for one tenant is invisible in aggregate monitoring — alert on missed heartbeats per job per tenant.

### Sandbox & Demo Tenants
- Dedicated tenants for sales demos and pre-release testing, never real client data.
- Staged rollouts deploy here first (the staging schema `plutus_t1` is the seed of this).

### Cross-Tenant Product Analytics
- Anonymised usage across tenants: which features are used, where users drop off.
- Drives the roadmap with evidence rather than the loudest client.

### Operator Security
- 2FA/SSO on the operator panel itself (Keycloak, Phase 9) — it holds the keys to every client's data.
- Secrets management with documented rotation (gateway keys, DB credentials, webhook secrets).
- Incident-response runbook: what to do, who to notify, in what order.

---

## 7. Compliance

- Registry of where each tenant's data lives (region/residency).
- DPA status tracked per tenant.
- Automated deletion after offboarding retention periods.
- Platform-level audit log of all operator actions (impersonation, entitlement changes, suspensions).

---

## Suggested Build Priority

1. **Per-tenant usage & health metrics** — everything commercial and operational hangs off this data.
2. **Support impersonation** — immediate, ongoing time savings.
3. **Automated provisioning & suspension** — the manual work that hurts most past ~10 clients.

Cheap to build early, painful to retrofit: sandbox/demo tenants and per-tenant job monitoring.

---

## Appendix — Current State vs This Document (repo, 2026-07-27)

Already built in `Plutus.Tenancy` and ops:

| Doc item | Status |
|---|---|
| Tenant lifecycle (status, suspension) | ✅ `TenantLifecycleService`, `PlatformController` (platform-admin gated, audited) |
| Provisioning | ✅ `ProvisioningService` |
| Plans & entitlements | ✅ `EntitlementService`, set per tenant via API |
| Offboarding (export + scheduled deletion) | ✅ `RetentionSweeper`, data export + grace-period deletion |
| Billing | 🟡 Provider-agnostic seam (`IBillingProvider`) built and testable; concrete adapter (Stripe/Paddle) awaits commercial choice (Phase 10) |
| Operator audit log | ✅ Lifecycle mutations audited |
| RBAC | ✅ Permission catalogue, roles, `perm:*` policies |
| Backups | ✅ Nightly MySQL (launchd), restore-rehearsed; pm2 resurrect; logrotate |
| Staging tenant | 🟡 `plutus_t1` schema exists — formalise as sandbox/demo tenants |
| Webhook/integration health | 🟡 Woo connector has self-healing poll + dry-run journal; generalise the pattern |

Not yet started: usage metering & margin visibility, churn signals, per-tenant health dashboard, rate limiting/quotas, impersonation, status page/announcements, SLA monitoring, per-tenant restore, gateway health monitoring, email/SMS deliverability, job-heartbeat monitoring, cross-tenant analytics, operator 2FA/SSO (Keycloak Phase 9 pending), contract tracking.
