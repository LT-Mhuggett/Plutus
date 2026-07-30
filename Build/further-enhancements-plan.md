# Further enhancements — implementation plan

Requested by Matt, 2026-07-30. Three items, in priority order:
1. **FE1 — Loyalty tier catalogue**: pre-defined loyalty levels (name + discount), assignable
   from a dropdown — no more free-text tiers.
2. **FE2 — Member number + barcode cards**: a unique, company-scoped ID per customer,
   renderable as a barcode so physical loyalty cards can be produced and scanned at the till.
3. **FE3 — Hardware helper agent**: the local agent (flagged in
   [WebApp-2026-07-23-plan.md](WebApp-2026-07-23-plan.md) §3.5) so the browser till can drive
   installed hardware — receipt printer, cash drawer — on the physical till PC.

FE1+FE2 are one coherent loyalty slice and should ship together. FE3 is independent and
larger; it can follow.

## Current state (verified 2026-07-30)

- `Membership` is per-customer with a **free-text** `Tier` string + `AutoDiscountRate`
  (`Plutus\Commons\Plutus.Entities\Models\Customers.cs`). Nothing enforces "Gold = 15%";
  every assignment re-types both values. One active membership per customer, enforced in
  `SetMembership` (`src/Plutus.Customers/CustomersController.cs`).
- `Customer` has **no human-usable identifier** — only the Guid `Id`. Till attach is by
  name/email/phone search (`GET /api/v1/customers?search=`, CustomersController.cs:46-58).
- The portal already has a **hand-rolled Code 39 renderer**
  (`Plutus\Frontend\Plutus.Frontend.Portal\src\Barcode39.tsx`, WP11.2 — used for receipt
  saleId barcodes). Code 39 charset = 0-9 A-Z '-' '.' — a member number designed for it
  costs nothing. The till WebApp has **no** Barcode39 twin yet.
- Tier is set in two UIs: portal `CustomerDialog.tsx` (Membership section, free-text) and
  till `LoyaltyPage.tsx` MemberDialog (free-text). Both gated `customers.manage`.
- Barcode scanners are keyboard-wedge — they already type into the till's customer-search
  box; scanning a card only needs the search to match member numbers.
- Receipt printing/cash drawer from the browser till: **not possible directly** (browser
  sandbox). The WebApp plan §3.5 chose a local agent as the primary path, PDF receipts as
  the agentless fallback; PDF is what's live today. Cash drawer = ESC/POS kick pulse
  (`ESC p`) sent to the receipt printer — solve printing and the drawer comes free.

## FE1 — Loyalty tier catalogue

### Design
New tenant-owned entity (Phase-8 conventions: UUIDv7 ids, TenantId, audited writes):

```
LoyaltyTier
  Id            Guid (UUIDv7)
  TenantId      Guid
  Name          string   — unique per tenant (case-insensitive), e.g. "Club", "Gold"
  AutoDiscountRate decimal — 0..1, same semantics as Membership
  DurationMonths  int    — default 12; SetMembership derives RenewalDay = start + duration
  Active        bool     — soft-delete; inactive tiers can't be assigned but keep history
  SortOrder     int      — display order in dropdowns
  CreatedAtUtc  DateTime
```

`Membership` gains `TierId Guid?` (nullable FK). **Rate semantics — live-follow:** reads
(till sync, `/api/v1/loyalty`, customer GET) resolve tier name + rate **via the join** when
`TierId` is set, so editing a tier (Gold 10% → 15%) updates every Gold member at once — that
is the point of a catalogue. The existing `Tier`/`AutoDiscountRate` columns stay as the
legacy/free-text path (`TierId == null`) and as a snapshot fallback; sale records are
immutable regardless, so history never retro-changes.

### API (all gated `customers.manage` except the read, all writes audited)
- `GET  /api/v1/loyalty/tiers` — active tiers (any authenticated principal — the till
  dropdown needs it, same openness as customer lookup).
- `POST /api/v1/loyalty/tiers` — create (validate: name unique per tenant, rate 0..1).
- `PUT  /api/v1/loyalty/tiers/{id}` — rename / re-rate / re-order / activate-deactivate.
- `DELETE` deliberately **not** offered — deactivate instead (memberships reference it).
- `POST /api/v1/customers/{id}/membership` body gains `tierId` (Guid). When present, tier
  name/rate/renewal come from the catalogue and `tier`/`autoDiscountRate` in the body are
  ignored. Free-text body stays accepted for back-compat until both UIs are switched, then
  free-text can be rejected (400) in a follow-up.

### Backfill
EF migration adds the table + FK. A one-off backfill (SeedMigrator verb or SQL in the
migration): `SELECT DISTINCT Tier, AutoDiscountRate FROM Memberships WHERE Active` per
tenant → create a `LoyaltyTier` per pair → set `TierId` on matching memberships. Existing
data becomes catalogue-managed with zero operator effort.

### UI
- **Portal Loyalty tab**: new "Manage tiers" button (visible with `customers.manage`) →
  dialog listing tiers (name, discount %, duration, active, member count) with add/edit/
  deactivate. Member count per tier comes free from the loyalty rows already loaded.
- **Portal `CustomerDialog.tsx`**: Membership section's free-text Tier + Discount % inputs
  become a **select of active tiers** (discount shown read-only from the tier) + Set
  membership. "No membership" option = expire path (future; out of scope here).
- **Till `LoyaltyPage.tsx` MemberDialog**: same select (fetch tiers on dialog open).
- Rows/tables keep showing tier *name* — no visual change needed in DataTables.

### Work packages
| WP | Scope | Status |
|---|---|---|
| FE1.1 | Entity + migration + backfill; `LoyaltyTiersController` CRUD, audited; `SetMembership` accepts `tierId`; reads resolve via join. Tests: uniqueness, rate bounds, live-follow (edit tier → loyalty row shows new rate), backfill round-trip. | ☐ |
| FE1.2 | Portal: tier-manager dialog on Loyalty tab; CustomerDialog dropdown. | ☐ |
| FE1.3 | Till WebApp: MemberDialog dropdown. | ☐ |
| FE1.4 | Gate: unit/arch/integration green; click-test both UIs; deploy + run EF migration on test env. | ☐ |

## FE2 — Member number + barcode cards

### Design
`Customer` gains `MemberNo string` — **unique per tenant**, assigned at creation, immutable,
backfilled for existing customers.

**Format (assumption — confirm before FE2.1):** `{6-digit per-tenant sequence}{mod-43 check
character}`, e.g. `000482K`, rendered on cards as Code 39 with a `C` prefix in the barcode
payload (`C000482K`) so till auto-scan can distinguish a member card from a product EAN or a
receipt saleId. Properties: short enough to type by hand, Code 39-safe charset, check
character catches mis-scans/typos, per-tenant sequence via a `MemberNoCounters` row locked
in the create transaction (low write volume — no contention concern). Sequential numbers do
reveal rough member counts; accepted for this product's scale.

### API
- `MemberNo` added to: customer create/GET responses, `/api/v1/loyalty` rows, customer
  search results.
- `GET /api/v1/customers?search=` also matches exact `MemberNo` (with or without the `C`
  prefix / check char) — this alone makes **scan-to-attach** work at the till, since
  scanners are keyboard-wedge into the existing search box.
- Migration backfills `MemberNo` for existing customers (per-tenant, ordered by
  CreatedAtUtc) + unique index `(TenantId, MemberNo)`.

### UI
- **Portal `CustomerDialog.tsx`**: show MemberNo (mono) + the Code 39 barcode
  (reuse `Barcode39.tsx`) + a **"Print card"** action → print-CSS view sized CR80
  (85.6 × 54 mm): company/store name, customer name, tier, barcode. Browser print to a card
  printer or onto adhesive card stock — no new hardware dependency.
- **Portal Loyalty/Customers DataTables**: MemberNo column (`mono small`, searchable).
- **Till**: copy `Barcode39.tsx` in (byte-identical twin, per the DataTable convention in
  [table-standard.md](table-standard.md)); show MemberNo in the at-sale customer panel;
  till auto-scan recognises the `C…` payload and attaches the customer directly.

### Work packages
| WP | Scope | Status |
|---|---|---|
| FE2.1 | Backend: column + counter + backfill migration; assignment on create; search match; MemberNo in all reads. Tests: uniqueness under concurrent create, check-char validation, search-by-scan. | ☐ |
| FE2.2 | Portal: MemberNo in dialog + tables; barcode; Print-card view. | ☐ |
| FE2.3 | Till: Barcode39 twin; scan-to-attach in the at-sale bar; MemberNo display. | ☐ |
| FE2.4 | Gate + deploy (EF migration on test env); print a real card and scan it at the till. | ☐ |

## FE3 — Hardware helper agent ("Plutus Till Agent")

Elaborates WebApp-2026-07-23-plan.md §3.5 / Phase 2 into buildable shape. Purpose: the
browser till cannot reach OPOS/ESC-POS hardware; a small installed agent on each till PC
bridges `localhost` HTTP → printer/drawer, reusing the native apps' `CommonPOSLibrary`
printing code.

### Shape
- **.NET 10 tray app** (not a Windows service — OPOS/printer drivers generally want an
  interactive user session; tills auto-log-in anyway). Auto-start via Run key. Tray icon
  shows health + a minimal settings window (choose printer, test print, drawer test).
- **Kestrel on `http://127.0.0.1:9123`, loopback-only bind.** CORS allow-list = the till
  origin(s) only.
- **Pairing/auth:** first run generates an agent token shown in its settings window; the
  till's Settings page ("Hardware" card, visible with `pos.settings.manage`) stores it;
  every call sends `X-Agent-Token`. Stops other local processes/websites driving the
  drawer. localStorage per device — consistent with existing till device settings.

### Endpoints (v1 — deliberately tiny)
| Endpoint | Behaviour |
|---|---|
| `GET /status` | `{ agentVersion, printer: { name, online }, drawerSupported }` — no auth, safe |
| `POST /print` | body = the receipt render payload the till already builds for PDF; agent formats via `CommonPOSLibrary` ESC/POS and prints silently |
| `POST /drawer/open` | ESC/POS kick pulse (`ESC p`) to the receipt printer |
| `POST /print/test` | settings-window test print |

Client side is plain `fetch` — zero new npm dependencies (per §3.5's contract sketch).

### Till integration
- `hardware.ts` facade: `agentAvailable()` (poll `/status`, cached ~10 s), `printReceipt()`,
  `openDrawer()`; **graceful degradation is the core rule** — agent absent/unhealthy →
  exactly today's behaviour (PDF receipt, no drawer), never a blocked sale.
- Checkout: on completed cash sale → `openDrawer()`; receipt → agent print when healthy,
  else PDF fallback. Header/Settings show a printer-health indicator.

### Packaging & update
- Self-contained single-file publish → MSI (WiX) or `winget` manifest; per-machine install.
- Auto-update **deferred** (v1: version surfaces in `/status` and on the portal's till list
  via the existing device-heartbeat path, so stale agents are visible; update = reinstall).

### Risks / notes
- Mixed-content: the till is served over HTTPS calling `http://127.0.0.1` — browsers treat
  loopback as *potentially trustworthy*, so this works in Chromium and Firefox; verify on
  the actual till browser early (FE3.1 spike).
- OPOS driver variance per printer model is the real-world risk — `CommonPOSLibrary`
  already encapsulates what the Xamarin NatApp ships with; start with the printer models
  actually deployed.
- Out of scope for v1, enabled by the seam: scales, customer-facing display, label printers.

### Work packages
| WP | Scope | Status |
|---|---|---|
| FE3.1 | Spike: skeleton tray app + `/status`; confirm HTTPS-page→localhost fetch on the till browser; test print through `CommonPOSLibrary` on a real deployed printer model. | ☐ |
| FE3.2 | Agent v1: endpoints, token pairing, settings window, tray health, ESC/POS receipt formatting from the till's receipt payload. | ☐ |
| FE3.3 | Till: `hardware.ts` facade; Settings "Hardware" card (agent URL default + token, test buttons); checkout wiring (silent print + drawer kick, PDF fallback); health indicator. | ☐ |
| FE3.4 | Packaging: MSI/winget, auto-start; install doc in HANDOVER.md. | ☐ |
| FE3.5 | Gate: end-to-end on a physical till PC — sale → silent receipt + drawer kick; unplug printer → PDF fallback + red indicator. | ☐ |

## Open decisions (assumed for now, cheap to change before build starts)
1. **FE2 member-number format** — 6-digit sequence + check char assumed. Alternative:
   random 8-digit (hides member counts). Decide before FE2.1.
2. **FE1 live-follow** — tier edits apply to all members immediately (assumed, recommended).
   Alternative: snapshot-at-assignment with an explicit "apply to existing" action.
3. **FE3 agent scope v1** — printer + drawer only (assumed). Label/card printing via the
   agent could later replace FE2's browser-print path for cards.
