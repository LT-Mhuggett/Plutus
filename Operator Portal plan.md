# Operator Portal plan

**Date:** 2026-07-28 · **Trigger:** Matt's first real operator login (Keycloak SSO, WP18.1).
**The observation:** logging in as an operator lands you in the *client* portal — Banking, Stock,
Prices, Customers, Loyalty, Webstore tabs — with the operator surface tucked into one "Platform"
tab at the end. An operator should never casually see client money/stock/customer data, and the
things an operator *does* need (subscribers, subscriptions & pricing, billing connectors, support
tickets) either live under Platform where they're hard to find, or don't exist yet.

---

## 1. What you saw, and why

The portal is ONE app serving two audiences. Tenant staff get the client tabs; a token carrying
`platform-admin` *additionally* gets the Platform tab. Nothing hides the client tabs from an
operator, so you get a client's chrome with (until RBAC denies you) a client's data.

**Your per-tab questions answered:**

| Tab you saw | What it is | What an operator should see instead |
|---|---|---|
| Banking | A client's real banking/cash-up data | Nothing (client data). "Banking connectors" as a concept = future per-tenant integrations, configured by the client like the payment gateway |
| Stock / Prices / Customers / Loyalty | Client operational data | Nothing — client data. Operator reaches it ONLY via the audited **impersonation** flow (already built, WP14.1) |
| Webstore | That client's Woo connection + its SKU queue | Correct instinct: the operator's cross-tenant **connector view already exists** — Platform → Health → "Connector health" (per connector, per tenant: last poll/webhook/outbound, error streak). It just isn't where you looked |

**⚠ Security finding (must fix first):** it's worse than cosmetic. The tenant-context resolver
falls back to the Kapow tenant when a token has no `tid` claim (a Phase-1 convenience from the
single-tenant days). Verified live: an operator token GETs `/api/v1/customers` → **200 with
Kapow's customers**, not 403. Endpoints gated by `perm:*` RBAC correctly 403 (that's most of the
write surface), but plain `[Authorize]` reads leak. An operator should be **hard-403'd from all
tenant data unless impersonating** (which stamps a tenant + audits every request).

---

## 2. Already built — you just can't see it (or it's mislabelled)

| You asked for | Exists today? | Where |
|---|---|---|
| "Users — people who signed up for a subscription" | ✅ mostly | Platform → **Tenants**: every tenant with status (Trial/Active/PastDue/Suspended/Closed), plan, signals, usage sparkline, drill-down. What's missing is the *word* — it should read as **Subscribers** and be the operator's landing page |
| Subscriptions: active / inactive | ✅ partially | Tenant **Status** *is* the subscription state; **Contract & renewal** (renewal date, term, £/month) is on the tenant detail (WP16.2); renewal-due signals fire at 60/30/7 days. What's missing: a **plan catalogue** (named plans with prices you set once), rather than per-tenant free-text plan + price |
| Set the price of a subscription | 🔶 per-tenant only | Contract editor sets a tenant's negotiated £/month. No central "Standard = £99/mo" plan list yet |
| Subscription connectors (Stripe etc.) | ✅ built today | Platform → **Billing**: pick Manual / Stripe Billing / Paddle / Chargebee, store keys (write-only). Adapter goes live when you open an account |
| Webstore connectors overview | ✅ | Platform → Health → Connector health (cross-tenant) |
| "Ask for help" → tickets to operators | ❌ not built | New build — and it's the missing source for the `support-heavy` churn signal (WP16.1 left it as a seam waiting for exactly this) |
| Notifications / comms | ✅ | Platform → Notifications (providers, test-send, delivery log) + Comms (announcements) |

---

## 3. The plan

### OP1 — Operator/client separation (the security + UX fix). *Do first.*
**Server:** a principal whose only authority is `platform-admin` (no tenant identity) gets a
**null tenant context, not the Kapow fallback** — every tenant-scoped endpoint 403s. The Kapow
fallback survives only for the legacy single-tenant paths behind `DISABLE_AUTH_DEV_ONLY` review
(and is removed once verified unused). Operator access to client data goes through **impersonation
only** (existing WP14.1: time-boxed, deny-listed, audited banner).
**Portal:** if the token is operator-only → render the **Operator Portal**: no client tabs at all;
the Platform screens become the top-level nav (Subscribers · Health · Billing · Tickets ·
Notifications · Comms · Analytics · Settings). A user with BOTH identities (operator + tenant
employee) gets a workspace switcher.
*DoD:* operator token → every `/api/v1/{tenant-data}` endpoint 403s (isolation test enumerates
routes); operator login shows zero client chrome; impersonation still works and is the only door.

### OP2 — Subscription plans & pricing
`SubscriptionPlan` (global): name, monthly price (pence), entitlement bundle, active flag; CRUD on
the operator portal (audited). Tenant detail: assign plan (replaces free-text `Plan`), price
defaults from the plan, contract (WP16.2) records negotiated overrides. Margin (16.3) and the
tenants list read plan price automatically. When a billing provider goes live, plans map 1:1 to
provider prices/products.
*DoD:* create "Standard £99/mo" once → assign to a tenant → shows on Subscribers, feeds margin;
changing a plan price is audited and reflected everywhere it's displayed.

### OP3 — Subscribers (the operator landing page)
Platform → Tenants, promoted and renamed **Subscribers**: signup date, plan + price, status,
renewal countdown, open signals, MRR total across the top. Add per-tenant **user list**
(read-only: the tenant's portal/till users + last login from WP13.1 `logins.*`) so "who signed up
/ who's active" is answerable at a glance. Active/inactive filter = status filter.
*DoD:* one screen answers: how many subscribers, on what plans, worth how much MRR, who's at risk
(signals), who hasn't logged in.

### OP4 — Ticket system ("Ask for help")
**Client side:** a "Help" button in the client portal (and till Settings) → subject + message →
`POST /api/v1/support/tickets` (tenant-scoped; any authenticated user). Client sees their tickets
+ replies thread.
**Operator side:** Operator Portal → **Tickets** inbox: open/waiting/closed, per-tenant, reply,
assign, close. New-ticket → `IOperatorAlerter` alert (existing keyed pattern) so it shows in the
alert feed; replies can ride the **Notifications framework** (email the client when a mailer is
wired — until then in-portal only).
**Feeds the churn model:** ticket volume per tenant finally powers the `support-heavy` signal
(WP16.1 seam → real).
*DoD:* client raises ticket → operator alerted, replies → client sees reply; ticket counts raise
`support-heavy` above threshold; all tenant-isolated (client A never sees client B's tickets).

### OP5 — later / gated
- Self-serve signup (public "start a trial" flow → provisions a Trial tenant) — needs OP2 + a
  billing adapter for card capture.
- Billing automation: provider webhooks flip subscription state automatically (16.4 reaction is
  already wired; needs the provider account).
- Client-side "banking connectors" (Open Banking feeds etc.) — a per-tenant integration configured
  by the client, same pattern as the payment gateway; not an operator surface.

**Order:** OP1 (security first, small) → OP2 → OP3 (mostly re-labelling + additions) → OP4
(the one genuinely new subsystem) → OP5 as gates open.

---

## 4. What does NOT change
- Client portal experience for tenant staff — identical (they never see operator screens).
- Impersonation remains the only way an operator touches client data, and it's audited.
- Everything shipped in Phases 13–18 keeps working; this plan re-homes and extends it.
