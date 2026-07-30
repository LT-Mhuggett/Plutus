# Further enhancements — implementation plan

Requested by Matt 2026-07-30 (first batch FE1–FE3; second batch FE4–FE8 same day).
Suggested build order at the bottom.

1. **FE1 — Loyalty tier catalogue**: pre-defined loyalty levels (name + discount), assignable
   from a dropdown — no more free-text tiers.
2. **FE2 — Member number + barcode cards**: a unique, company-scoped ID per customer,
   renderable as a barcode so physical loyalty cards can be produced and scanned at the till.
3. **FE3 — Hardware helper agent**: the local agent (flagged in
   [WebApp-2026-07-23-plan.md](WebApp-2026-07-23-plan.md) §3.5) so the browser till can drive
   installed hardware — receipt printer, cash drawer — on the physical till PC.
4. **FE4 — Table standard everywhere**: every portal + till table sortable, searchable,
   paginated with a 25/50/100 page-size selector.
5. **FE5 — Inventory upgrade**: portal/till parity, working category filter + click-through,
   current-stock column, permission-gated bulk edit, a Bin instead of delete, untracked
   ("unlimited") stock items.
6. **FE6 — Locations & till identity cleanup**: a Tills view, re-issue enrolment codes,
   till↔store assignment; answers to "why 3 tills?".
7. **FE7 — Gift cards**: unique tracked codes, sold/redeemed lifecycle, printable.
8. **FE8 — Search refinements**: quoted "exact phrase" support (word-search itself shipped
   2026-07-30).
9. **FE9 — Users & Roles upgrade** (added 2026-07-30): password reset (admin-set + emailed
   link), guarded user removal, a roles→permissions reference table, and a collapsed
   per-user access matrix.

FE1+FE2 are one coherent loyalty slice and should ship together. FE3 is independent and
larger. FE4/FE5/FE6 are portal/till UX slices; FE7 is a full feature (schema + till + portal).

## Current state (verified 2026-07-30)

- `Membership` is per-customer with a **free-text** `Tier` string + `AutoDiscountRate`
  (`Plutus\Commons\Plutus.Entities\Models\Customers.cs`). Nothing enforces "Gold = 15%";
  every assignment re-types both values. One active membership per customer, enforced in
  `SetMembership` (`src/Plutus.Customers/CustomersController.cs`).
- `Customer` has **no human-usable identifier** — only the Guid `Id`. Till attach is by
  name/email/phone search (`GET /api/v1/customers?search=`).
- The portal and till both have the **hand-rolled Code 39 renderer** (`Barcode39.tsx`
  twins; 2026-07-30 gained a `fit` prop so long payloads scale to their container). Code 39
  charset = 0-9 A-Z '-' '.' — short codes designed for it scan reliably; 36-char UUIDs do not.
- Tier is set in two UIs: portal `CustomerDialog.tsx` and till `LoyaltyPage.tsx` MemberDialog,
  both free-text, both gated `customers.manage`.
- Barcode scanners are keyboard-wedge — they already type into search boxes.
- Receipt printing/cash drawer from the browser till: PDF/browser-print today; the agent
  (FE3) is the chosen path for silent printing + drawer kick (`ESC p`).
- **Search**: word-based matching (`ItemParameters.MatchAllWords`) SHIPPED 2026-07-30 with a
  till Settings toggle + offline parity; scan-bar search returns all matches in a scrollable
  list. Quoted-phrase handling (FE8) not yet built.
- **Tables**: the shared `DataTable` standard exists ([table-standard.md](table-standard.md),
  byte-identical twins in portal + till) with client and server modes — but only SOME pages
  use it (P1.3 landed Customers + Loyalty; earlier P-phases covered several report tables).
  The rest are hand-rolled and inconsistent (see FE4 audit).
- **Inventory category filter bug** (root cause found, portal `InventoryItems.tsx`): the
  Category dropdown filters CLIENT-SIDE over the current 25-row page
  (`items.filter(i => i.catId === catFilter)`), so any category not present on that page
  shows nothing. The legacy `api/Item/Index` has no category parameter — filtering must move
  server-side.
- **Category delete protection: ALREADY DONE** — the portal's category manager is built on
  the guarded v1 categories API (P4), which 409s a delete while items reference the category
  (and on the last category). The legacy cascade-delete `api/Category` route is deliberately
  not used by the portal.
- `Item` has `Stock` (legacy per-item stock) + platform `StockLevels` (ledger-backed, P4);
  no soft-delete flag — a Bin (FE5) needs a new column. `CatId` is REQUIRED — "remove from
  category" must mean "move to an Uncategorised category", not null.

## FE1 — Loyalty tier catalogue

> 2026-07-30, Matt: "I need to be able to set specific tiers, I can't see where I can do
> this?" — correct: **this screen does not exist yet**; it is NOT hidden behind RBAC. Once
> FE1 ships it will be gated on `customers.manage`, which Owner already holds, so no role
> change is needed.

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
| FE1.1 | Entity + migration + backfill; `LoyaltyTiersController` CRUD, audited; `SetMembership` accepts `tierId`; reads resolve via join. Tests: uniqueness, rate bounds, live-follow (edit tier → loyalty row shows new rate), backfill round-trip. | ✅ 2026-07-30 (8 unit + 1 integration) |
| FE1.2 | Portal: tier-manager dialog on Loyalty tab; CustomerDialog dropdown. | ✅ 2026-07-30 |
| FE1.3 | Till WebApp: MemberDialog dropdown. | ✅ 2026-07-30 |
| FE1.4 | Gate: unit/arch/integration green; click-test both UIs; deploy + run EF migration on test env. | ✅ 2026-07-30 deployed — rollbacks `backend.pre-fe1` + portal/till `current.pre-fe1`, DB dump `~/PLUTUS/backups/plutus-pre-fe1-20260730.sql.gz`; migration `AddLoyaltyTiers` applied, backfill created the "Club" tier from live data; full DoD sequence verified live. ⏳ Matt to click-test both UIs |

**Backfill runs at startup** (after `Database.Migrate()`, `LoyaltyTierBackfill.ApplyAsync`) and is
idempotent, so it self-heals on any future deploy. It names itself `loyalty-tier-backfill` as the
audit actor — the context refuses to save without one (caught on the first FE1 deploy attempt;
pinned by `Backfill_works_without_a_CurrentUser_set`).

**DoD:** create tier "Gold 15%" in the portal → assign from the dropdown in BOTH UIs (no
free-text input remains visible) → edit Gold to 12% → every Gold member's loyalty row and
the till's at-sale discount show 12% without re-assignment; existing free-text memberships
appear as backfilled tiers; deactivated tier vanishes from dropdowns but keeps its members.

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
- **Till**: show MemberNo in the at-sale customer panel; till auto-scan recognises the
  `C…` payload and attaches the customer directly.

### Work packages
| WP | Scope | Status |
|---|---|---|
| FE2.1 | Backend: column + counter + backfill migration; assignment on create; search match; MemberNo in all reads. Tests: uniqueness under concurrent create, check-char validation, search-by-scan. | ✅ 2026-07-30 (27 unit + 1 integration) |
| FE2.2 | Portal: MemberNo in dialog + tables; barcode; Print-card view. | ✅ 2026-07-30 |
| FE2.3 | Till: scan-to-attach in the at-sale bar; MemberNo display. | ✅ 2026-07-30 |
| FE2.4 | Gate + deploy (EF migration on test env); print a real card and scan it at the till. | ✅ 2026-07-30 deployed — rollbacks `backend.pre-fe2` + portal/till `current.pre-fe2`, DB dump `plutus-pre-fe2-20260730.sql.gz`; migration `AddMemberNumbers` applied, backfill numbered the existing customer; DoD verified live. ⏳ Matt to print a real card and scan it |

**Format as built:** `NNNNNNC` — 6-digit per-tenant sequence + a check character from
`Crockford32.Alphabet` (the codebase's existing human-keyable set: no I/L/O/U, all Code 39-safe).
Barcode payload is `C` + the number. Two things the build corrected against the original sketch:
the check character is **not** classic mod-43 (that set includes space/`$`/`%`/`+`/`.`/`/`, unusable
in a spoken or typed number), and parsing keys off **length, not "looks numeric"** — a check
character is frequently itself a digit (sequence 1 → `0000011`), which an all-digits branch would
mis-read as a bare sequence. Input is folded through `Crockford32.Normalise`, so O-for-0 and
I-for-1 mis-keys still resolve. Weighted (7,3,1) so adjacent transpositions are caught.

**Till scan behaviour:** only the `C`-prefixed payload triggers a customer lookup, and if no
customer matches it falls through to the normal item lookup — so a real SKU beginning with "C" is
never hijacked.

**DoD:** every customer (new + backfilled) has a unique MemberNo; typing or scanning it in
the till customer search attaches that customer; Print card renders CR80 with a barcode
that scans; two concurrent creates never collide (test proves it).

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
- Out of scope for v1, enabled by the seam: scales, customer-facing display, label printers
  — and FE7 gift-card voucher printing could route through it later.

### Work packages
| WP | Scope | Status |
|---|---|---|
| FE3.1 | Spike: skeleton tray app + `/status`; confirm HTTPS-page→localhost fetch on the till browser; test print through `CommonPOSLibrary` on a real deployed printer model. | ☐ |
| FE3.2 | Agent v1: endpoints, token pairing, settings window, tray health, ESC/POS receipt formatting from the till's receipt payload. | ☐ |
| FE3.3 | Till: `hardware.ts` facade; Settings "Hardware" card (agent URL default + token, test buttons); checkout wiring (silent print + drawer kick, PDF fallback); health indicator. | ☐ |
| FE3.4 | Packaging: MSI/winget, auto-start; install doc in HANDOVER.md. | ☐ |
| FE3.5 | Gate: end-to-end on a physical till PC — sale → silent receipt + drawer kick; unplug printer → PDF fallback + red indicator. | ☐ |

## FE4 — Table standard everywhere

**Requirement (Matt, 2026-07-30):** every table across the portal (and till) must be
orderable, split into pages, and have a 25/50/100 page-size dropdown. "Looking at the tables
across the portal, they are not all consistent." The standard already exists
([table-standard.md](table-standard.md): shared `DataTable` twins, client + server modes,
search box, sortable headers, pager with page-size select) — this WP finishes the rollout
instead of the current page-by-page drift.

### Work
1. **Audit** (FE4.1): sweep every portal tab + till page for `<table>`s not rendered by
   `DataTable`. Known stragglers: portal `InventoryItems` (hand-rolled Prev/Next pager),
   `UsersPage`, `StoresPage` tills tables, `PlatformPage` sub-screens, `CategoryManager`,
   reporting detail tables; till `InventoryPage` (own pager), reporting lists, parked/return
   dialog lists. Output: checklist table in this doc with per-page mode (client vs server).
2. **Server-mode plumbing** (FE4.2): the legacy `Index` endpoints already return an
   `X-Pagination` header (`TotalCount/PageSize/CurrentPage/TotalPages`) that the frontends
   currently DISCARD — expose it through the `api.ts` fetch helpers so server-mode
   `DataTable` gets real totals ("X–Y of N") instead of the blind Next-button heuristic.
   v1 endpoints that lack `skip/take` get them (additive).
3. **Port pages** (FE4.3, mechanical): swap each straggler to `DataTable`, preserving any
   page-specific cells (chips, inline edit). Small pages (<1 page of data) still use it —
   consistency IS the requirement; the pager collapses when there's one page.
4. Conventions stay as documented: `num` cells right-aligned, ids `mono small`, money via
   `gbp()`.

### FE4.1 audit — done 2026-07-30

**Classification rule** (the judgement this audit turned on): `DataTable` is for **browsable
collections** — things you sort, search and page through. It is NOT for:
- **documents** (a receipt's lines, a sale's line items, the till basket) — a pager on a
  receipt is nonsense;
- **fixed breakdowns** (VAT by band, payment-method split, a price's effective-date history)
  — 3–6 rows that ARE the answer, with no browsing to do;
- **in-dialog detail sub-lists** (credit history, a user's role assignments, tier rows) —
  they live inside a modal that is already scoped to one record.

Applying that, here is every table:

| Page / file | Tables | Verdict |
|---|---|---|
| **portal** BankingPage | pending payments, daily banking | ✅ port (client) |
| **portal** CustomReport | sales list | ✅ port (client) |
| **portal** Dashboard (report variant) | period breakdown, day's sales | ✅ port (client) |
| **portal** ItemsSoldPage | items sold | ✅ port (client) |
| **portal** PeriodsPage | financial periods | ✅ port (client) |
| **portal** StockPage | transfers, **levels**, movements | ✅ port — levels in **server** mode (endpoint has skip/take/search) |
| **portal** InventoryItems | items | ✅ port — **server** mode (legacy Index + FE4.2 totals) |
| **portal** WebstorePage | review queue, catalogue, outbound log, price diffs, name diffs, unmatched SKUs | ✅ port (client) ×6 |
| **portal** UsersPage | users | ✅ port (client) — role-assignment sub-table left to **FE9.4** |
| **portal** VatPage | off-band integrity list | ✅ port (client) — by-band totals left (fixed breakdown) |
| **portal** HelpPage | support tickets | ✅ port (client) |
| **portal** PricesPage | variance | ✅ port (client) — already has 2 DataTables; effective-date + store-override history left (breakdowns) |
| **portal** PlatformPage | 17 operator tables | ✅ port (client) — operator-only, so **last**; deferred to FE4.5 if it bloats the slice |
| **portal** StoresPage | tills-per-store, warehouses, webstores | ⏭ **defer to FE6** — that WP restructures this page (adds the flat Tills view); porting now = double work |
| **portal** SummaryReport | top items, payment split | ⏭ leave (report breakdowns) |
| **portal** SaleDialog / CustomerDialog / TierManagerDialog | receipt, credit history, tiers | ⏭ leave (document / in-dialog) |
| **till** InventoryPage | items | ✅ port — **server** mode |
| **till** EmployeesPage | staff | ✅ port (client) |
| **till** StatisticsPage | sales list | ✅ port (client) |
| **till** reporting/ReportingPage | items-sold, stock levels | ✅ port (client) — already uses DataTable elsewhere |
| **till** SummaryReport / VatReport | band + method breakdowns | ⏭ leave (breakdowns) |
| **till** SaleDetailDialog / TillPage | sale document, basket | ⏭ leave |

**Totals: 22 tables to port** (18 portal incl. PlatformPage's 17 counted as one workstream,
4 till), 3 pages deferred to other WPs, 9 deliberately left as documents/breakdowns.

| WP | Scope | Status |
|---|---|---|
| FE4.1 | Audit checklist (portal + till), agree client/server mode per page. | ✅ 2026-07-30 (above) |
| FE4.2 | X-Pagination surfaced through api helpers; skip/take on v1 lists that lack it. | ✅ 2026-07-30 |
| FE4.3 | Port all stragglers to DataTable (portal, then till). | ✅ 2026-07-30 — **13 tables** (9 portal + 4 till); PlatformPage split to FE4.5 |
| FE4.4 | Gate: click-through every tab; deploy. | ✅ 2026-07-30 deployed (`*.pre-fe4`); totals + new search verified live. ⏳ Matt to click-through |
| FE4.5 | PlatformPage's 17 operator tables (split out if FE4.3 runs long). | ✅ 2026-07-30 — **16 ported**, funnel left as a breakdown; deployed (`portal/current.pre-fe45`) |

**FE4.5 as built.** All 16 browsable operator tables now use `DataTable`: Tickets, Plans,
Notifications delivery log, Commercial margin, Analytics adoption + route groups, Comms
announcements, Flags, **Tenants** (the big one — health dot, sparkline and signal chips survive as
non-sortable `render` columns; £/mo, renewal, sales and 5xx sort via `sort:` accessors over the
side-loaded maps), TenantDetail users + entitlement overrides, Health alerts + per-tenant health +
connectors + consumer lag, and Jobs. The status-dot columns turned out to be plain columns, not
row expanders, so every table mapped cleanly.

**Left deliberately:** the login→sale **funnel** — a fixed ordered sequence where the order *is*
the meaning, so sorting or paging would destroy information (same call as VAT-by-band). Marked
with a comment in the code so it isn't mistaken for a miss.

**FE4 is now complete: 29 tables ported across both apps.** `sortable.tsx` (`useSort`/`SortTh`,
the pre-DataTable helper) has exactly ONE remaining consumer — `StoresPage`, deferred to FE6.
**Delete `sortable.tsx` when FE6 rebuilds that page.**

**FE4.2 as built.** `legacyPaged()`/`getPaged()` in each app's `api.ts` read the `X-Pagination`
header (`TotalCount`) that the legacy `Index` endpoints have always sent and every frontend threw
away — pagers could only say "page N" and guess whether Next was live. Verified live: items
**20,341** total (**790** when searched), stock levels **3,198**. `total: null` when a
non-paginating endpoint is called, so callers keep the old estimate as a fallback.
Additive backend change: `GET /api/v1/webstores/{id}/products` gained `search` (name/SKU) — it had
skip/take but no search, and the shared table always offers a search box (verified: 745 → 10).

**Ported (FE4.3).** Portal: Banking ×2, CustomReport, Dashboard ×2, ItemsSold, Periods, Stock ×3
(levels in server mode), InventoryItems (server), Webstore ×6, Users, VAT off-band, Help, Prices
variance. Till: Inventory (server), Employees, Statistics, Reporting items-sold + stock (server).

**Three incidental wins:** CustomReport, Statistics and Webstore's "web-only SKUs" all had hard
client-side caps (`slice(0, 100)` / `slice(0, 200)`) that hid rows behind an "export to see
everything" note — paging replaced them, so the full set is now reachable in the UI.

⚠ **Typecheck loop matters here.** There is no Node on the Windows dev box, so these mechanical
edits were verified with `npm run typecheck` on the Mac after each batch — it caught a real
`getKey` type error (numeric `id` vs `string`) that would have shipped. Do the same for FE4.5.

**DoD:** the FE4.1 checklist shows every data table on portal + till rendered by
`DataTable`; each has sortable headers, a 25/50/100 page-size select, a pager showing
"X–Y of N" (server mode included, via X-Pagination), and search where the page had one;
the two DataTable twins remain byte-identical.

## FE5 — Inventory upgrade (portal + till parity)

**Requirements (Matt, 2026-07-30):** inventory must behave the same on till and portal;
permission-gated bulk edit (on-screen selection or select-all-matching); add/remove
category, add/remove brand, move to Bin (never delete); a hidden RBAC-gated Bin view;
category delete protection (already done — see current state); category click-through to
filtered items; **fix the category filter showing nothing**; a "Current stock" column;
an "unlimited stock" per-item flag.

### FE5.0 — BUG: category filter shows nothing (fix first, it's small)
Root cause (verified): portal `InventoryItems.tsx` filters client-side over the current
25-row page. Fix server-side: `ItemParameters` gains `CatId Guid?` (composes with `Search`
exactly like `MatchAllWords` did); portal sends it and resets paging on change; the "in this
category on this page" empty-state hack is deleted. Till gains the same category dropdown
(parity). Applies to FE4's server-mode totals automatically.

### FE5.1 — Category click-through
In Inventory → Categories, clicking a category row navigates to the Items sub-tab with
`CatId` pre-applied (portal has the `useNav` focus pattern for cross-tab navigation
already — reuse it for sub-tab + filter state). Breadcrumb/chip shows the active category
filter with an ✕ to clear.

### FE5.2 — "Current stock" column
Items lists (portal + till) show the item's current stock level next to price. Source: the
platform stock ledger's `StockLevels` (P4), fetched batched for the visible page (extend the
items read or a `POST /api/v1/stock/levels/bulk` with the page's item ids — NOT one call per
row). Untracked items (FE5.5) show "∞".

### FE5.3 — Bulk edit (permission-gated)
- **Permission:** new `inventory.bulk` in the catalogue; seed to Owner / Company Admin /
  Store Manager (NOT Supervisor/Cashier). ⚠ deploy note: `Plutus.SeedMigrator rbac` needed
  to backfill grants (RBAC seeding doesn't run on startup).
- **Selection model** (both modes explicit in the UI):
  1. Tick rows on the current page (+ page select-all header checkbox).
  2. "Select everything matching the current filter" — banner shows the SERVER count
     ("all 412 items matching 'batman' in Comics") and the action runs server-side against
     the same criteria, not against fetched rows.
- **Actions:** set category · set brand · clear brand · move to Bin. ("Remove from
  category" = move to a per-tenant **Uncategorised** category, auto-created on first use —
  `Item.CatId` is a required FK, so bare removal is impossible by design.)
- **API:** `POST /api/v1/items/bulk` `{ action, value?, ids? | criteria? }`, gated
  `inventory.bulk`, audited with the criteria + affected count; response returns affected
  ids/count for the UI toast. Price/VAT deliberately NOT bulk-editable (keeps the P-phase
  VAT guardrail meaningful).
- **Safety:** "select all matching" always shows a confirmation with the live server count
  before running ("Move 412 items to Comics?"). No undo in v1 — but the audit row stores the
  affected ids + prior values, so a manual revert is always possible; category/brand/bin are
  all non-destructive anyway (bin restores, category/brand re-set). Cap one bulk call at
  10,000 items (409 above — refine the filter).
- Till: read-only benefit (sees the results); bulk UI is portal-only in v1.

### FE5.4 — Bin (soft delete, RBAC-hidden)
- `Item` gains `BinnedAtUtc DateTime?` (+ who, via audit). Migration, EF filter helpers.
- Binned items are excluded from: till scan/search + sale, webstore outbound listing sync,
  inventory default views, price lists. Historical sales/reports untouched (the item row
  still exists — that's the point of Bin over delete).
- **Bin view**: an Inventory sub-tab visible only with `inventory.bulk` (reuse — the same
  people who can bulk-bin can see/restore the bin; separate perm felt like catalogue bloat,
  flag if you disagree). Actions: restore, and nothing else — no hard delete anywhere.
- Attempting to sell a binned barcode at the till → "item is in the bin" notice.

### FE5.5 — Untracked ("unlimited") stock
Per-item flag `StockUntracked bool` (tick box in the item dialog on BOTH portal + till:
"Don't track stock — e.g. carrier bags, back-issues"). Behaviour:
- Sales/returns of the item post NO stock movements (skip in the `SaleRecorded` stock
  consumer + direct movement APIs); **SaleLines are recorded as normal**, so "how many
  sold" reporting is unaffected.
- Stock views/current-stock column show "∞ untracked"; negative-stock report excludes them;
  webstore outbound treats them as always-in-stock (respecting the oversell buffer = n/a).
- Existing movements for an item being flipped to untracked are left as history (level
  simply stops mattering); flipping back resumes from the ledger level — document this in
  the dialog help text.

| WP | Scope | Status |
|---|---|---|
| FE5.0 | Server-side CatId filter + portal/till dropdown fix (bug). | ✅ 2026-07-30, deployed (`*-fe58` rollbacks) |
| FE5.1 | Category → filtered Items click-through. | ☐ |
| FE5.2 | Current-stock column (batched levels) portal + till. | ☐ |
| FE5.3 | `inventory.bulk` perm + bulk endpoint + portal bulk UI (both selection modes). | ☐ |
| FE5.4 | Bin: migration, exclusions, RBAC-gated Bin view with restore. | ☐ |
| FE5.5 | `StockUntracked`: migration, consumer/API skips, UI tick box, report/outbound handling. | ☐ |
| FE5.6 | Gate: tests (bulk criteria vs ids, bin exclusions, untracked sale posts no movement), deploy + `SeedMigrator rbac`. | ☐ |

**DoD:** pick any category in portal Items → correct items appear across pages (and on the
till); click a category in Categories → filtered Items view; every item row shows current
stock (∞ for untracked); as Owner, bulk-move 3 ticked items + "all matching" a filter (with
count confirmation) between categories/brands; bulk-bin → items vanish from till search,
webstore sync, and default views, visible only in the RBAC-gated Bin with restore; a
Supervisor sees no bulk UI and the endpoint 403s; sell an untracked item → sale + reports
count it, stock level unchanged.

## FE6 — Locations & till identity cleanup

### The questions answered (2026-07-30, live data)
- **"Why 3 tills?"** — every enrolled browser is a *device* under a *till*: ①
  `Till 019f9630` = a leftover **test till** from the 24 Jul enrolment build (auto-named,
  2 test sales — rename or revoke it; delete is blocked because it has sales); ②
  `Kapow Web Till` = the real enrolled browser till; ③ `Kapow Web` = the **webstore's
  virtual till** that carries WooCommerce orders (badged "webstore" in the portal since
  2026-07-30, actions locked).
- **"Do I need cookies / logged-in user?"** — neither. Till identity = the **device
  credential** in that browser's localStorage (enrolment), independent of who logs in.
  Logins say *who*, the device says *which till*. Clearing site data / incognito loses the
  credential → re-enrol.
- **"Will a till lose connectivity / can I recreate a code?"** — connectivity loss is fine
  (offline queue); losing the *credential* (cleared storage, new PC) currently has NO
  recovery path short of creating a brand-new till — that's exactly how stray tills get
  created (the till Settings "Generate a code" button also creates a NEW till every time).
  FE6.1 fixes this properly.

### FE6.1 — Re-issue an enrolment code for an EXISTING till
- `POST /api/v1/tills/{id}/enrol-code` (gated `portal.tills.enrol`, audited): mints a fresh
  single-use code bound to that till. On enrolment with it, the till's previous Active
  device is auto-revoked — **rule: one active device per till** (a till is one counter);
  the old browser stops trading at its next sync.
- Portal: "New code" button on each till row (Locations + the FE6.2 Tills view).
- Till Settings: "Generate a code" is renamed/reworked to make the two flows explicit —
  "re-enrol THIS till elsewhere" (new code for an existing till) vs "create a NEW till"
  (moves to the portal as the primary path).

### FE6.2 — Locations IA rework
Groups become: **Stores** (as-is — each store card lists its linked tills) · **Tills**
(NEW — flat view of ALL tills: name, store/location assignment, device status, last online,
webstore badge; assign/move a till between stores via `PUT /api/v1/tills/{id}/store`,
gated `portal.tills.enrol`, audited; the webstore's virtual till appears here badged AND
under its webstore) · **Warehouses** (as-is) · **Webstores** (as-is, plus its virtual till
listed on the card).

### FE6.3 — Hygiene
One-off on the test env: rename `Till 019f9630` → "Test till (retired)" and revoke its
device (keep — it has sales history), or leave as a visible example; Matt's call.

| WP | Scope | Status |
|---|---|---|
| FE6.1 | Re-issue code endpoint + one-active-device rule + portal/till UI wording. | ☐ |
| FE6.2 | Tills group in Locations + till→store move endpoint. | ☐ |
| FE6.3 | Test-env till hygiene (user decision). | ☐ |

**DoD:** issue a new code for an existing till from the portal → enrol a fresh browser
profile with it → the till trades under the SAME till id and the old device shows Revoked;
Locations shows the new Tills group with every till's store + status (webstore till badged
in both places); moving a till between stores updates both store cards; audit rows exist
for re-issue + move.

## FE7 — Gift cards

**Requirement (Matt, 2026-07-30):** generate gift cards with a unique tracked code shown as
a barcode or QR; track when bought, when redeemed, who it's linked to; printable as a
receipt or an A4 page.

### Design
Follow the store-credit pattern (D15: append-only ledger, no mutable balance):

```
GiftCard
  Id           Guid (UUIDv7)
  TenantId     Guid
  Code         string  — unique per tenant; Crockford32, 12 chars + mod-43 check char
                         (Code 39-safe, keyboard-wedge scannable, hand-typeable)
  CustomerId   Guid?   — optional link to a loyalty customer (buyer or giftee)
  SoldSaleId   Guid?   — the sale that activated it
  IssuedAtUtc  DateTime?  — null = generated but not yet sold/active
  ExpiresAtUtc DateTime?  — tenant-configurable duration at issue (default none)
  VoidedAtUtc  DateTime?  — manual void (audited)
  CreatedAtUtc DateTime

GiftCardEntries — append-only, balance = Σ entries
  (Id, TenantId, GiftCardId, Type Issue|Redeem|Adjust|Expire, AmountPence signed,
   SaleId?, ActorUserId?, CreatedAtUtc)
```

Lifecycle: **generate** (portal, batch of N or single — codes exist, worthless until sold)
→ **sell/activate** at the till (scan the card/voucher, take payment; Issue entry for the
loaded amount, SoldSaleId set) → **redeem** as a tender at checkout (scan; partial
redemption leaves a balance — Redeem entries are negative) → optional **void/expire**.
Redeeming and selling are ordinary sale/tender flows, so all existing reporting sees them.

### Code on the card: barcode vs QR
**v1 = Code 39** via the existing `Barcode39` (a 13-char code renders compact and scans on
the keyboard-wedge scanners already in use — this is why the code format is short, unlike
saleId UUIDs). QR needs an encoder we don't have (no-library rule) — defer; the printed
voucher also shows the code in text, and phone-camera use cases can come with a QR
follow-up if wanted.

### ⚠ VAT & accounting treatment (the trap — do NOT sell cards as normal items)
Selling a gift card is **not revenue and not a VAT-able sale** — it creates a liability
(like store credit); the VAT-able sale happens at REDEMPTION, when real goods leave at
their own tax bands. A naive build that rings the card through as a standard-rated item
would charge VAT twice (once on the card, again on the redeemed goods) and corrupt every
VAT report. Under UK voucher rules a mixed-rate store's card is a multi-purpose voucher —
VAT at redemption — which is also the only model that composes with the existing pipeline:
- **Activation**: the card sale line posts at a **zero/out-of-scope tax band** (activation
  is money-in + `Issue` ledger entry; no VAT, no product revenue). The existing VAT-integrity
  guardrail must accept this line shape.
- **Redemption**: a gift card is a **tender** (like store credit) against an otherwise
  normal sale — VAT falls out of the goods lines as usual; nothing special to do.
- **Reporting**: activation amounts appear as "gift cards sold" (liability), NOT in product
  revenue; an **outstanding gift-card liability report** (Σ unredeemed balances) mirrors the
  store-credit outstanding number. Redemptions show as tender split, as with any tender.

### Offline rule
The server is the balance authority. **Activation and redemption require connectivity** —
the till blocks both with a clear "gift cards need a connection" notice when offline (normal
sales stay offline-capable). No offline gift-card queue in v1.

### Out of scope v1 (noted, not forgotten)
Refund-to-gift-card (store credit already covers refund-to-credit); reloading a card after
initial activation (buy a new one); cross-tenant/portability; QR.

### API (gated: reads any authenticated; writes `giftcards.manage`, new perm seeded to
Owner / Company Admin / Store Manager; redeem/sell at till gated `pos.sell`)
- `POST /api/v1/giftcards/generate` `{ count, amountPence?, expiresMonths? }` → codes.
- `GET /api/v1/giftcards?search=` (code/customer), `GET /api/v1/giftcards/{code}` →
  status, balance, full entry history (bought when, redeemed when/where, linked customer).
- `POST /api/v1/giftcards/{code}/activate` `{ amountPence, saleId, customerId? }`.
- `POST /api/v1/giftcards/{code}/redeem` `{ amountPence, saleId }` (409 over-balance /
  expired / void / inactive).
- `POST /api/v1/giftcards/{code}/void` + `{customerId}` link/unlink. All writes audited.

### UI
- **Portal**: new "Gift cards" section (Loyalty tab or its own tab — decide at build):
  DataTable (code, status chip, balance, customer, sold/redeemed dates), generate dialog,
  detail dialog (history + void + link customer + **Print**).
- **Print**: two print-CSS templates — receipt-width voucher (works with FE3's agent
  later) and A4 gift certificate (store name/logo line, amount, code text + barcode,
  expiry, "redeem in store" footer). Browser print in v1.
- **Till**: sell/activate flow (scan an unactivated code at the basket → "activate for
  £X"), redeem as tender in CheckoutDialog (scan → applies balance like store credit).
  Auto-scan distinguishes the `G…`-prefixed payload (same trick as FE2's `C…` member cards).

| WP | Scope | Status |
|---|---|---|
| FE7.1 | Entities + migration; generate/activate/redeem/void endpoints + ledger; `giftcards.manage` perm; tests (over-redeem, expiry, double-activate, idempotent redeem per sale, **activation posts zero VAT**). | ☐ |
| FE7.2 | Till: activate at basket + redeem tender in checkout + auto-scan prefix; offline block. | ☐ |
| FE7.3 | Portal: gift-cards table + detail/void/link + generate dialog. | ☐ |
| FE7.4 | Print templates (voucher + A4). | ☐ |
| FE7.5 | Outstanding-liability report (portal Reporting + dashboard pill next to store-credit). | ☐ |
| FE7.6 | Gate + deploy (migration + `SeedMigrator rbac`). | ☐ |

**DoD:** sell + activate a card at the till (offline attempt blocked) → VAT report for the
day shows ZERO VAT from the activation; redeem partially against a mixed-band basket → goods
VAT normal, card balance reduced, tender split correct; redeem the remainder; over-redeem
409s; liability report reconciles to Σ unredeemed balances; A4 + voucher print render with a
scannable code; scan a printed voucher's barcode into the till and it resolves.

## FE8 — Search refinements

**Shipped 2026-07-30** (for the record): word-based matching — "batman one" finds
"Batman Year One" — as `ItemParameters.MatchAllWords`, till device pref (default ON) with a
Settings toggle, offline IndexedDB parity, scan-bar search returning ALL matches in a
scrollable list.

**Remaining — quoted exact phrases:** `"batman one"` (in quotes) must match the LITERAL
phrase only, regardless of the word-matching toggle; mixed input composes, e.g.
`"year one" batman` = items containing the exact phrase *year one* AND the word *batman*.

### Design
- Tokeniser in `ItemParameters` (server) mirrored in `offline.ts` (till offline search):
  split the input into quoted segments (kept verbatim, unclosed quote = treated as opening
  a phrase to end-of-input) and remaining whitespace-separated words; every token must
  match (against name/barcode/brand). With `MatchAllWords=false`, the whole input minus
  quote characters stays one phrase (today's behaviour — quotes are then redundant but
  harmless).
- No UI change needed; update the Settings toggle description to mention quotes.
- Tests extend `ItemSearchTests`: quoted phrase misses interleaved words, mixed
  phrase+word, unclosed quote, quotes-only input.

| WP | Scope | Status |
|---|---|---|
| FE8.1 | Tokeniser in ItemParameters + offline mirror + tests + toggle help text. | ✅ 2026-07-30 |
| FE8.2 | Deploy (backend + till). | ✅ 2026-07-30 (`*-fe58` rollbacks; verified live: quoted=11 literal hits vs 40 word-mode, mixed composes) | 

**DoD:** `"batman one"` finds only literal "…batman one…" (not Batman Year One);
`"year one" batman` finds Batman Year One but not other "year one" titles; behaviour
identical offline; `ItemSearchTests` cover phrase, mixed, unclosed-quote, quotes-only.

## FE9 — Users & Roles upgrade

**Requirements (Matt, 2026-07-30):** reset users' passwords; delete users (with "are you
sure" guardrails); send password resets; a table listing the roles and what they give access
to; a visual per-user access table, collapsed by default; anything else useful.

### Current state (verified 2026-07-30)
The portal's Users & Roles tab already: lists users, creates them (optional password →
`WebCredentials` login), deactivates, assigns/unassigns roles, and fetches a user's
**effective permissions** on expand (`GET /api/v1/users/{id}/effective-permissions` exists
and works — it's just rendered as a flat blob, not a readable matrix). What's missing:
password set/reset (no endpoint at all), delete, any roles→permissions reference (the
`GET /api/v1/roles` list is names only; the grants live in `RbacSeeder`/role-grant rows),
and permission descriptions (the catalogue is bare constant keys).

Email exists as a seam: `IMessageSender` (used by the MFA heads-up) — **SIMULATED and
logged to MessageEvents until an Email provider is configured + enabled in
Platform → Notifications**. Reset emails ride the same seam and the same switch.

### FE9.1 — Password management
- `POST /api/v1/users/{id}/password` `{ password }` (min 8): admin sets a temporary
  password. Upserts the `WebCredentials` row, so this also *grants* login to a staff user
  who never had one. Gated `portal.users.manage`, audited (never logs the password).
- `POST /api/v1/users/{id}/password-reset`: mints a single-use, 48h reset token (random,
  **hashed at rest** like enrolment codes) and sends a reset link via `IMessageSender`.
  409 when the user has no email. Audited.
- Anonymous completion: `POST /api/auth/password-reset/complete` `{ token, newPassword }`
  — rate-limited like `enrol`; consuming a token invalidates the user's other outstanding
  tokens. Portal login page gains **"Forgot password?"** (self-service uses the identical
  flow) and a reset-completion form at `#reset=<token>`.
- Honest limitation, documented in the UI: existing sessions are 12h bearer tokens and
  cannot be revoked individually — a reset stops the *next* login, it does not kick a
  live session mid-flight.

### FE9.2 — Remove a user (guarded — "delete" without destroying history)
Users are referenced by sales, audit rows and role assignments — a hard DELETE would
destroy attribution, so **remove = deactivate + revoke login credential + unassign all
roles**, keeping the row for history (same philosophy as FE5's Bin). Server:
`POST /api/v1/users/{id}/remove`, gated `portal.users.manage`, audited; 400 when removing
YOURSELF (the classic lock-out); 409 when removing the tenant's last Owner-holder.
Portal guardrails: an are-you-sure dialog that requires **typing the user's name** to arm
the button, spelling out what happens ("removes login + all roles; history is kept").
Removed users vanish from the default list and all pickers; an "include removed" toggle
shows them greyed with a **Restore** action (re-activates; roles must be re-granted
deliberately, credential via FE9.1).

### FE9.3 — Roles reference table ("what does each role give?")
- `PermissionCatalogue` entries gain human descriptions (code-defined map, e.g.
  `customers.manage` → "Add/edit customers, grant credit, set membership tiers"), exposed
  as `GET /api/v1/permissions`.
- `GET /api/v1/roles` enriched with each role's grants (permission keys + caps like the
  refund limit) and live member counts.
- Portal: a **Roles** section on the tab — one row per role (built-in badge, member count),
  expanding to its permissions with descriptions; plus a compact role × permission-group
  matrix (✓s) for the "what do I hand out?" overview. Read-only in this WP (role editing
  is a separate decision — built-ins are seeder-managed).

### FE9.4 — Per-user access matrix (collapsed by default)
The expand-a-user flow already fetches effective permissions — render it properly: a
`<details>` per user (closed by default, per the requirement), containing a grouped
visual table (Portal / POS / Customers / Platform groups) with a ✓ per permission and
**which role granted it** (attribution from the user's assignments × role grants — an
answer to "why can Dave refund?"). Plus chips for caps (refund limit).

### FE9.5 — Extras (the "anything else useful")
- **Last login** column + login method (password vs SSO): stamp `LastLoginAtUtc` at token
  issue; surfaces dormant accounts — pairs with FE9.2 clean-ups.
- **Invite** variant of the reset flow for brand-new users ("set your password" copy
  instead of "reset") — same token machinery, different email template.
- **Per-user audit slice**: the user detail links to the existing `/api/v1/audit`
  filtered to that user as actor (endpoint already supports filtering).
- MFA status chip once client Keycloak provisioning ships (forward-looking; display-only).

| WP | Scope | Status |
|---|---|---|
| FE9.1 | Set-password + reset-token endpoints, IMessageSender email, login-page forgot/complete flow, rate limiting. Tests: token single-use/expiry/hashing, no-email 409, min-length. | ☐ |
| FE9.2 | Remove/restore endpoints + self/last-Owner guards; typed-name confirm dialog; pickers exclude removed. Tests: guards, credential revoked, history intact. | ☐ |
| FE9.3 | Permission descriptions + enriched roles endpoint; portal Roles section + matrix. Test: role grants in the API match RbacSeeder exactly. | ☐ |
| FE9.4 | Per-user collapsed access matrix with role attribution. | ☐ |
| FE9.5 | Last-login stamp + column; invite variant; audit slice link. | ☐ |
| FE9.6 | Gate + deploy (no migration except LastLoginAtUtc + reset-token table; `SeedMigrator rbac` not needed — no new permissions). | ☐ |

**DoD:** admin sets a temp password → user logs in with it; "send reset" → simulated email
logged in MessageEvents with a working link → completing it changes the password and kills
the token (second use 410s); removing a user requires typing their name, kills their login,
keeps their sales/audit history, blocks removing yourself or the last Owner; restore works;
the Roles table lists every built-in role with grants matching `RbacSeeder`; each user's
collapsed matrix matches the effective-permissions endpoint and names the granting role.

## FE4 click-test checklist (Matt) — 2026-07-30

Every row below is DEPLOYED and typecheck-clean, but only Matt can confirm it *reads* right.
**Hard-refresh both apps first** (Ctrl+Shift+R) — the portal shell and till bundle both changed.

On each table check the same four things: **sortable headers** (click one, ▲/▼ flips) ·
**search box** narrows rows · **Show 25/50/100** changes page length · **pager reads
"X–Y of N"** with a real N (not "page 3").

### Portal — https://admin.plutus.huggett.dscloud.me
| # | Where | Table | Watch for |
|---|---|---|---|
| 1 | Inventory → Items | items (**server** mode) | N should read **20,341**; the Category dropdown moved next to "+ Add item"; search is debounced 300 ms so it fires once you stop typing |
| 2 | Inventory → Stock ledger | stock levels (**server**) | N ≈ **3,198**; the old separate Search + Show controls are gone (now in the table toolbar) |
| 3 | Inventory → Stock ledger | "In transit" (only if transfers exist) | Receive/Cancel buttons still work |
| 4 | Inventory → Stock ledger → Detail | movement history (in dialog) | sort by When |
| 5 | Reporting → Summary | period breakdown | drill button (Sales/Drill) still opens the day |
| 6 | Reporting → Summary → a day | that day's sales | "Detail" opens the sale dialog |
| 7 | Reporting → Custom | sales list | **was capped at 100 rows** — page past 100 now; row click became an **Open** button |
| 8 | Reporting → Items sold | items sold | up to 2,000 lines — the biggest win; search by item/staff |
| 9 | Reporting → VAT | off-band integrity list ("show list") | only appears if off-band items exist; by-band totals below are deliberately NOT paged |
| 10 | Banking | unresolved payments | only if any exist; "Re-run matching" still works |
| 11 | Banking | per till per day | variance still red |
| 12 | Prices | store variance vs HQ | Edit button still opens the price dialog |
| 13 | Customers | customer list | member-no column (FE2) sorts + searches |
| 14 | Loyalty | members list | member-no column; Open still works |
| 15 | Users & Roles | users list | Roles/Deactivate buttons still work; deactivated rows still greyed |
| 16 | Company → Financial periods | periods | "Close period" only on Open rows |
| 17 | Webstore → Review queue | pending SKUs | Bind/Create item/Ignore only on Pending rows |
| 18 | Webstore → Catalogue | products (**server**) | N ≈ **745**; **search is new** (name/SKU — try "batman", expect ~10); "view on site" link moved to the actions column |
| 19 | Webstore → Outbound | journal | failed rows still red |
| 20 | Webstore → Alignment | price differences | sorted biggest-difference first |
| 21 | Webstore → Alignment | name drift | — |
| 22 | Webstore → Alignment | web-only SKUs | **was capped at 200** — page past it now |
| 23 | Help | your tickets | Open still opens the thread |

### Till — https://plutus.huggett.dscloud.me
| # | Where | Table | Watch for |
|---|---|---|---|
| 24 | Inventory Management | items (**server**) | N = **20,341**; Category filter kept; Add item + Edit still work |
| 25 | Reporting → Items sold | items sold | search by item/category/staff |
| 26 | Reporting → Stock | stock levels (**server**) | old Search/Show controls replaced by the table toolbar |
| 27 | Reporting → Negative stock | same table, negative filter | empty-state message differs |
| 28 | Reporting → Custom | sales list | **was capped at 100** — page past it; row click became **Open** |
| 29 | Settings/Staff → Employees | staff | "Set password" still works |

### Platform tab — operator login only (FE4.5, deployed 2026-07-30)
Only reachable with the operator/SSO login, and only worth checking if you use it. 16 tables:
| # | Sub-screen | Tables |
|---|---|---|
| 30 | Tenants | subscriber list — **health dot, sparkline and signal chips must still render**; £/mo, Renewal, Sales and Err 5xx are now sortable |
| 31 | Tenants → Open | that tenant's users + entitlement overrides (Remove still works) |
| 32 | Health | open alerts · per-tenant request health · connector health · consumer lag |
| 33 | Jobs | job cadence grid (status dot intact) |
| 34 | Tickets | inbox (Open still opens the thread) |
| 35 | Plans | plan list (Edit/Delete; Delete still disabled while tenants are on a plan) |
| 36 | Notifications | delivery log |
| 37 | Commercial | per-tenant margin (Margin column still red/green) |
| 38 | Analytics | feature adoption · route groups (the **funnel below them stays unsorted by design**) |
| 39 | Comms | announcements (Delete still works) |
| 40 | Flags | feature flags (Kill/Enable still works) |

### Deliberately unchanged (don't report these as missed)
Receipts and the sale-detail dialog · the till basket · VAT-by-band and payment-split
breakdowns · the Platform **login→sale funnel** (fixed ordered stages) · a price's effective-date
history · credit history and the tier list inside dialogs · **Locations**
(tills/warehouses/webstores — deferred to FE6, which rebuilds that page).

If a table misbehaves, the rollback is `portal/current.pre-fe4` (or `current.pre-fe45` for the
Platform tab alone) and `web/current.pre-fe4`.

## Suggested build order

1. **FE5.0** (category-filter bug — small, it's broken today) + **FE8.1** (quoted search —
   tiny, same file as the shipped word search).
2. **FE1 + FE2** (the loyalty slice — tiers then member cards).
3. ~~**FE4** (table rollout)~~ — ✅ **COMPLETE 2026-07-30** (FE4.1–4.5, 29 tables).
4. **FE9** (users & roles — self-contained, and password reset is an operational need). ← **next**
5. **FE5** remainder (stock column → bulk edit → bin → untracked stock).
6. **FE6** (till identity + Locations IA).
7. **FE7** (gift cards — biggest new surface, benefits from FE2's scan-prefix pattern).
8. **FE3** (hardware agent — independent; schedule around physical access to a till PC).

## Decisions — DEFAULTS ARE BINDING for an autonomous build
An agent building from this doc follows these WITHOUT asking; Matt can veto any of them
before (or after — they're all cheap to change) the relevant WP starts:
1. **FE2 member-number format**: 6-digit per-tenant sequence + mod-43 check char, `C` prefix
   in the barcode payload. (Alternative if vetoed: random 8-digit.)
2. **FE1 live-follow**: tier edits apply to all members immediately.
3. **FE3 agent scope v1**: printer + drawer only.
4. **FE5.4 Bin visibility**: reuse `inventory.bulk` (no dedicated bin permission).
5. **FE6.1 one-active-device rule**: re-enrolling a till auto-revokes its previous device.
6. **FE7 QR**: deferred; Code 39 only in v1.
7. **FE7 placement**: a **"Gift cards" section on the portal Loyalty tab** (not a new tab —
   the tab bar is already 12 wide; promote later if it earns it).
8. **FE5.3 "remove from category"**: moves items to an auto-created per-tenant
   "Uncategorised" category.
9. **FE7 gift-card expiry default**: none (no expiry) unless set at generation.
10. **FE9.2 "delete" semantics**: remove = deactivate + revoke credential + unassign roles,
    row kept for history (no hard delete anywhere). Restore does NOT restore roles.
11. **FE9.1 reset tokens**: 48h, single-use, hashed at rest; email via the existing
    `IMessageSender` seam (simulated until the platform Email provider is enabled).
12. **FE9.3 role editing**: out of scope — the roles table is read-only reference;
    built-in roles stay seeder-managed.

## Build notes for an autonomous agent (Sonnet)

**Read first:** `HANDOVER.md` (architecture + conventions), `Build/table-standard.md`
(DataTable contract), `src/Plutus.Customers/CustomersController.cs` (the v1 controller
idiom this plan's endpoints copy: `[Authorize(Policy="perm:…")]`, `_db.Audit(…)`, tenant
context, UUIDv7 ids), `src/Plutus.Identity/RbacSeeder.cs` + `PermissionCatalogue` (how
permissions are declared/seeded).

**Hard conventions:**
- New v1 endpoints = the CustomersController pattern; NEVER extend the legacy generic CRUD
  controllers for new features (additive query params on legacy `Index` are fine — see
  `ItemParameters.MatchAllWords` as the exemplar).
- Every write: audited, permission-gated, tenant-scoped. New permissions go in
  `PermissionCatalogue` + `RbacSeeder` + a `RbacTests` case.
- Migrations: EF, MySql folder; tests use EnsureCreated so migration + model must agree.
  ⚠ live deploys auto-apply migrations on startup — the deploy step (human) dumps the DB
  first. RBAC changes additionally need `Plutus.SeedMigrator rbac` run on the env (human).
- Frontends: React+TS, no new npm dependencies (hand-rolled per the no-library rule);
  portal/till shared components (`DataTable.tsx`, `Barcode39.tsx`) are byte-identical
  twins — change BOTH or neither; money is pence + `gbp()`; till device prefs live in
  `prefs.ts` (localStorage).
- Frontend TS is NOT compiler-verified on the Windows dev box (no Node — by choice); write
  conservatively to existing idioms. The Mac builds catch errors at deploy time.

**Gate (every feature, before it's called done):**
```bash
DOTNET="/c/Program Files/dotnet/dotnet.exe"
"$DOTNET" build Plutus/Endpoints/Plutus.DBService/Plutus.DBService.csproj -c Debug
"$DOTNET" test tests/Plutus.Tests.Unit/Plutus.Tests.Unit.csproj -c Debug
"$DOTNET" test tests/Plutus.Tests.Architecture/Plutus.Tests.Architecture.csproj -c Debug
"$DOTNET" test tests/Plutus.Tests.Integration/Plutus.Tests.Integration.csproj -c Debug
```
Plus each feature's **DoD block** — the click-test parts are for Matt on the test env; the
testable parts must be pinned by automated tests.

**What an agent CANNOT do alone (plan around it):**
- **FE3.1/FE3.5** — needs a human at a physical till PC with the real printer (the whole
  spike exists to de-risk exactly what can't be simulated). Build FE3.2's agent + FE3.3's
  facade headless; hardware verification is Matt's.
- **Deploys to the test env** — builds/publishes are scriptable, but DB dumps, pm2 swaps and
  `SeedMigrator rbac` on the Mac follow the session runbook; treat deploy WPs as handover
  points, not build steps.
- **FE6.3** — a data-hygiene choice on live data (Matt decides rename vs revoke).
- **FE4.1's audit** produces a checklist for review before the mechanical port — one
  checkpoint, not a question storm.

**Sequencing within a feature:** backend + tests → portal → till → DoD sweep. Keep WPs as
separate commits (`FEx.y: …` prefix) so a bad slice rolls back alone.
