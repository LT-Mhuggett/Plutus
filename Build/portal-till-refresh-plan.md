# Portal & Till Refresh Plan — dashboard pills, report parity, inventory, loyalty, till hardening

**Date:** 2026-07-29 · **Requested by:** Matt · **Status:** IN PROGRESS — P1, P2, P3 (substance) + P4.1 done & LIVE; remainder pending
**Scope:** Plutus.Frontend.Portal, Plutus.Frontend.WebApp (web till), backend modules (`src/Plutus.*`), RBAC.
**Read first:** `Build/operator-portal-plan.md` §Repo runbook (build/test/deploy commands, pitfalls) — everything there applies here too.

### Progress snapshot (updated 2026-07-29)
Legend: ✅ done & deployed · 🟡 committed, not yet deployed · ⬜ not started.

| WP | State | Notes |
|---|---|---|
| **P1** WP1.1 DataTable + table-standard.md | ✅ | Twin `DataTable.tsx` in portal + till; `Build/table-standard.md` written + linked in README; PlatformPage converted. |
| **P1** WP1.2 Portal nav plumbing | ✅ | `NavContext`/`useNav`/hash routing; StoresPage honours `focus`. |
| **P1** WP1.3 Migrate remaining legacy tables (sweep) | ⬜ | Opportunistic follow-up; not run. |
| **P2** WP2.1 Dashboard KPI endpoint | ✅ | `GET /api/v1/reports/dashboard` + integration test. |
| **P2** WP2.2 Pills + remove table + day labels | ✅ | 7 clickable pills; `variant="dashboard"` hides tables; label thinning fixed. |
| **P3** WP3.1 Portal Summary = till Summary | ✅ | Committed, not deployed. New `SummaryReport.tsx` (summary-rich): period select, inc/ex-VAT toggle, previous-period deltas, daily chart, top items, payment split — portal-native styling (not the till's classes). Reporting→Summary now this; Dashboard tab keeps rollup chart + pills. |
| **P3** WP3.2 Portal Custom report | ✅ | Committed, not deployed. New `CustomReport.tsx`: date-range sales list + per-sale drill-in (shared `SaleDialog.tsx`, extracted from Dashboard) + client-side CSV. Category column deferred (the v1 sales list has no line-level category; lives in Items sold). |
| **P3** WP3.3 Backend category on sold-lines | ✅ | `category` on items-sold JSON+CSV, category-sales, best-sellers. |
| **P3** WP3.4 Items sold: Category column | ✅ | Both platforms; category column added. |
| **P3** WP3.5 Portal VAT = till VAT (+integrity) | ✅ | Committed, not deployed. `VatPage.tsx` rewritten to the till UX (month/quarter + year, stat tiles, by-band table, off-band integrity banner) but numbers stay on VatRollups (period-lock-respecting) per the audit — Σ band VAT == headline VAT, no unallocated row. |
| **P3** WP3.6 Prices browsable landing | ✅ | `GET /api/v1/prices/list`; DataTable landing + "deviate only" toggle. |
| **P3** WP3.7 Category sales report | ✅ | Both platforms. |
| **P3** WP3.8 Best sellers report | ✅ | Both platforms. |
| **P3** WP3.9 Negative stock report → edit | ✅ | `filter=negative` on stock/levels; portal report + till toggle. |
| **P4** WP4.1 Rename Stock → Inventory | ✅ | Committed `7b510c9`; Inventory tab now the sub-tabbed page below. |
| **P4** WP4.2 Portal item CRUD | ✅ | New Inventory→Items sub-tab; add/edit via legacy `api/Item`+`api/Tax` (VAT guardrail) through an `api.ts` legacy bridge (businessId resolved from the tenant's first company + cached). Initial stock routed via the v1 ledger, not legacy `/api/Stock`. |
| **P4** WP4.3 Category column on both inventory lists | ✅ | Category column on portal Items + till InventoryPage (catId→name); portal Items also has a page-scoped category filter. |
| **P4** WP4.4 Category manager UI (webstore-critical) | ✅ | **Deviation from plan (safer):** built a *guarded v1* `categories` controller instead of raw legacy `api/Category`, whose DELETE cascade-deletes every item in the category. New surface carries item counts, blocks delete while items reference it (409) and refuses the last category, adds bulk `/reassign`. Reads `perm:portal.reports.view`, writes `perm:portal.stock.adjust`. Portal Inventory→Categories sub-tab (add/rename/reassign+delete). Integration test green. |
| **P5** WP5.1–5.3 Loyalty edit + webstore link | ✅ | LIVE 2026-07-30 (`f75ea9c`+`97ee0d4`). **5.1** portal Loyalty editable (shared `CustomerDialog`, Add member); **5.2** till Loyalty add/edit gated on customers.manage; **5.3** webstore→loyalty link by email — `CustomerExternalRef` entity + migration `AddCustomerExternalRef` (applied on deploy, verified in `__EFMigrationsHistory`), `WooOrder.billing.email`, `WebstoreCustomerLink.ApplyAsync` (link-only, idempotent) on BOTH webhook + reconciler, `externalRefs` on the customer detail endpoint + "Linked accounts" in the dialog. Rollbacks `backend.pre-p5`, portal/till `current.pre-p5`; pre-migration DB dump `~/PLUTUS/backups/plutus-pre-p5-20260730.sql.gz`. |
| **P6** WP6.1–6.3 Store info / un-enrol approval / Help | ⬜ | 6.2 carries a `DeviceStatus.PendingRemoval` migration. |

**Live deploy tag:** P1+P2+P3(substance)+**P4 (all)** deployed 2026-07-30 (portal 200, till 200, `/api/v1/categories` 401, ETRIE 200). P4 rollbacks: `backend.pre-p4`, portal/till `current.pre-p4` (earlier `.pre-p3`).
**P3 fully deployed 2026-07-30** (`721f956` + VAT fix `f27b4fd`): backend swap + portal build; verified portal 200, till 200, backend routes 401, ETRIE 200. Rollbacks: `backend.pre-p3ports`, portal `current.pre-p3ports`. Till not touched this round. Suites Unit 214 · Arch 6 · Integration 46; portal typechecks clean.
**Resume point:** **P1–P5 are DONE and LIVE.** Next: P6 (till store-info, un-enrol approval [migration], Help + RBAC perms). (P1.3 legacy-table sweep still optional.)

---

## Hard rules (unchanged from every prior plan)

1. **DO NOT TOUCH ETRIE.** After any Mac change: `curl https://huggett.dscloud.me/health` → 200.
2. Deploy only when Matt asks. Backend swap keeps a `backend.pre-<tag>` rollback; portal/till keep `current.pre-<tag>`.
3. Suites must stay green: Unit 214 · Architecture 6 · Integration 43 (was 39; grew as WPs added tests — never shrink).
4. Tenancy: every new backend query runs under the ambient tenant filter; new tenant-owned entities go in the `TenantOwned` list in `MySqlDbContext` (~line 150).
5. No chart libraries (hand-rolled SVG only — enforced by comment convention in `Dashboard.tsx:49` and `SummaryReport.tsx`).
6. Commit per work package. Don't push without Matt's ask (bare `git push` goes to `upstream` = seank842).

---

## Answers to the questions embedded in the request

**"The Prices tab appears to be a report that should really be under reports? Correct me if you think different." / "Prices doesn't make sense? It shows one item?"**
Root cause found (2026-07-29 follow-up): the Prices landing shows ONLY the **variance table** — items whose store price deviates from HQ (`PricesPage.tsx:58-76`). With one store override in the data, exactly one item appears; every other item is reachable only by typing its barcode into the lookup box. So the page isn't a report — it's a full **editor** (policy, HQ price + scheduling, store overrides, force-reset) with a landing view that only makes sense once overrides exist. **Fix (WP3.6, resolves D1):** keep Prices as a management tab but give it a real landing — a paginated DataTable of ALL items (barcode, name, category, band, policy, HQ price, overrides/Δ) with per-row Edit opening the existing PriceDialog; "only where stores deviate" becomes a filter toggle. This also absorbs the "Price list report" idea — one screen serves browse + edit, nothing moves under Reports.

**"I cannot see where I can edit categories?"**
Correct — you can't. Category CRUD exists in the API (`api/Category`, legacy `CategoryController`, full Index/Get/Post/Put/Delete) but **no UI anywhere consumes it** (portal or till; the only reference is the generated `types.gen.ts`). The till's item dialog *assigns* an item to a category (`InventoryPage.tsx:222-231`) but nothing can create/rename/delete categories. WP4.4 builds the manager.

**"I assume there is a 'Read' and 'Edit' RBAC role for settings?"**
There isn't. The permission catalogue (`src/Plutus.SharedKernel/Permissions.cs`, 15 permissions) has **no settings.\* permission at all** — the closest is `portal.company.manage`. Help/support endpoints today are open to any authenticated tenant user. WP6.3 adds the new permissions this plan needs.

---

## Phase P1 — Foundations (do first; everything else leans on these)

### WP1.1 Shared standard table component + the table standard doc
The requested standard: **sortable columns, search, page size 25/50/100, pagination** — "all tables in the webtill, portal and operator portal should have the same functionality as the Plutus Portal Stock page".

Current reality (why this is a WP, not a sweep):
- Portal `sortable.tsx` = client-side **sorting only** (`useSort` + `SortTh`). Search/paging are hand-rolled per page.
- Portal StockPage (the exemplar) does search + paging **server-side** (`/api/v1/stock/levels?search=&skip=&take=`); most other endpoints (customers, loyalty, tickets, platform tables) have **no skip/take**.
- The till has **no shared table helper at all**; page sizes vary (25 fixed, 2000 cap, 100 cap…).

Build:
1. `DataTable.tsx` in the portal (and a copy in the till — the two apps don't share a package; keep the files byte-identical, note the twin in a header comment): props `columns` (key/label/numeric/sortable/render), `rows`, and either `server` mode (`{total, skip, take, onPage, onSearch}`) or `client` mode (sort/search/paginate in-component). Renders: search box, page-size select (25/50/100), sortable `<th>` (reuse `useSort` logic), pager with "X–Y of N".
2. **Doc:** `Build/table-standard.md` — the rules (sort/search/25-50-100/pagination, `num`/`mono small`/`muted` cell classes, server-vs-client mode guidance: server mode whenever the endpoint supports skip/take, client mode acceptable ≤ ~500 rows), plus "how to use DataTable" with one example. Link it from README's doc index. **All new tables must use it; migrate existing tables opportunistically as each WP touches its page** (a big-bang migration of every existing table is a follow-up sweep, WP1.3).
3. Convert the operator-portal tables (`PlatformPage.tsx`: subscribers, tickets, plans, health, jobs) to DataTable client mode — they're small datasets, this is the cheapest proof pass.

**DoD:** DataTable exists in both apps; table-standard.md written + linked in README; PlatformPage tables converted; suites green.

### WP1.2 Portal tab navigation plumbing (needed by the clickable pills)
`App.tsx` tab state is a local `useState` — nothing can navigate programmatically and reload always lands on Dashboard.

Build: a tiny `NavContext` (`{ tab, go(tab, focus?) }`) provided by App; nav buttons and pills call `go()`. Mirror the tab into `location.hash` (`#reporting`) and read it on boot, so tabs survive reload and are deep-linkable. `focus` is an optional string a target page may consume (e.g. `go("Locations", "warehouses")` auto-opens that `<details>` group in StoresPage — the groups are all default-closed today).

**DoD:** any component can `go(tab)`; hash deep-links work; StoresPage honours a focus param for stores/warehouses/webstores groups.

### WP1.3 (follow-up sweep, schedulable anytime after WP1.1) Migrate remaining legacy tables
CustomersPage (unsortable, fixed take=100), LoyaltyPage, ItemsSoldPage, VatPage, StoresPage sub-tables, till InventoryPage/ItemsSoldView. Backend: add `skip/take/search` to `/api/v1/customers` and `/api/v1/loyalty` (trivial — copy the stock/levels pattern).

---

## Phase P2 — Dashboard

### WP2.1 Backend: one KPI endpoint for the pills
`GET /api/v1/reports/dashboard` (policy `portal.reports.view`) returning one JSON object:
```
{ salesTodayPence, salesWeekPence, weekStart, activeUsers, activeTills, activeStores, activeWarehouses, activeWebstores }
```
- **Sales today:** `SalesRollups` day bucket for today (company level) — same source as `reports/summary`.
- **Sales this week — ACTIVE week, not last-7-days:** Monday→today of the current ISO week, server-local time (server tz = store tz per HANDOVER). Return `weekStart` so the UI can label it ("w/c 27 Jul").
- **Active users:** `People` active + not-deleted count (mirror `/api/v1/users`' filter).
- **Active tills:** tills having ≥1 `Device` with `DeviceStatus.Active`.
- **Active stores:** count of `Store` rows for the tenant.
- **Active warehouses:** `StockLocations` where `Type == Warehouse`.
- **Active webstores:** webstore connections where `Enabled`.
- One endpoint (not seven client calls) because the individual sources have mixed policies — `/api/v1/stores` needs `portal.company.manage` which a reports viewer may not hold. Counts are not sensitive; the endpoint computes them server-side under its own single policy.

**DoD:** endpoint + integration test (seed a till/store/warehouse, assert counts); policy `portal.reports.view`.

### WP2.2 Dashboard pills + remove the table + fix day-view dates
`Dashboard.tsx`:
1. **Remove the per-period table below the graph** (lines 238-259) **and the drill-down sales table** (261-286) *from the Dashboard tab only*. The same component currently doubles as Reporting → Summary; WP3.1 replaces that usage, so after both WPs: Dashboard = KPIs + chart, Reporting → Summary = the rich till-style report. (Sequencing note: if WP2.2 ships before WP3.1, keep the table temporarily behind a `variant` prop so Reporting doesn't lose drill-down in the gap.)
2. **Day-view dates bug:** `chartLabel()` already formats days as "18 Jul", but labels only render when `buckets.length <= 20` (line 56) — the default 30-day day view shows **no labels at all**. Fix: always label, thinning to every `ceil(n/6)`th bucket exactly as the till does (`SummaryReport.tsx:233-239`).
3. **Pills row** (above the chart, styled on the existing `stat-row`/`stat` classes, rendered as buttons): Sales Today · Sales This Week ("w/c {weekStart}") · Active Users · Active Tills · Active Stores · Active Warehouses · Active Webstores. Click targets via `go()`:
   - Sales Today / This Week → `go("Reporting")` (Summary)
   - Active Users → `go("Users & Roles")`
   - Active Tills → `go("Locations", "stores")` (tills live inside store cards)
   - Active Stores → `go("Locations", "stores")`
   - Active Warehouses → `go("Locations", "warehouses")`
   - Active Webstores → `go("Webstore")`

**DoD:** pills render from one `/reports/dashboard` call, each navigates; no table below the Dashboard graph; day view shows thinned date labels at every granularity/window.

---

## Phase P3 — Reports parity (portal ⇄ till)

The till is the reference: its Summary (`reporting/SummaryReport.tsx`, `/api/v1/reports/summary-rich`) and VAT (`reporting/VatReport.tsx`) are "good". Design intent stays "available on both surfaces, not pixel-identical" (ReportingPage.tsx:15-17) — but structure/feature-match them.

### WP3.1 Portal Summary = till Summary, company-wide
Port `SummaryReport.tsx` into the portal as `ReportingPage`'s Summary sub-tab (replacing the reused `<Dashboard/>`): period select (7/30/90/365), previous-period **deltas**, inc/ex-VAT toggle, daily SVG chart with crosshair + thinned date labels, top-items table, payment-method split. Backend `summary-rich` is already tenant-wide (SalesV2 across all stores) — that IS the company-wide view; verify and add `?storeId=` filter only if Matt asks later. Add portal `api.ts` wrapper `fetchSalesSummary` (copy till's, api.ts:335).

**DoD:** portal Reporting → Summary functionally matches the till's; Dashboard keeps the simpler rollup chart + pills.

### WP3.2 Portal Custom report (net-new) + Category in the header — both platforms
The portal has **no Custom report today**. Port the till's `StatisticsPage.tsx` (sales-line listing + `SaleDetailDialog` drill-in) to a portal Reporting → Custom sub-tab. Add a **Category column** to the table header on **both** platforms — requires WP3.3's backend category exposure. Use DataTable.

**DoD:** Custom exists on both; both show Category; drill-in works on the portal.

### WP3.3 Backend: category on sold-line reports (prereq for 3.2/3.4/3.5/3.7/3.8)
Reports join `SaleLine.ItemIdOne → Items.IdOne` for names only; `Item.CatId → Category.Name` is one extra hop (Item entity `Item.cs:55-59`; field is **`CatId`, not CategoryId**). Add `category` to: `reports/items-sold` rows + its CSV export, and the custom-report line source. Missing/unmatched items → `category: null`, render "—". Watch: the join is by barcode string, ~83k SaleLines — keep it in SQL (single query), never per-row lookups.

**DoD:** items-sold JSON + CSV carry `category`; integration test asserts a categorised item round-trips.

### WP3.4 Items sold: Category column — both platforms
Portal `ItemsSoldPage.tsx` and till `ItemsSoldView` (ReportingPage.tsx:67-118): add the Category column + a category filter select (populate from categories endpoint, WP4.4). Convert both tables to DataTable.

### WP3.5 VAT report: replicate the till's onto the portal
Replace portal `VatPage.tsx` internals with a port of till `VatReport.tsx`: Month/Quarter toggle, year select, **vat-integrity guardrail banner** (off-band offenders table — the portal currently doesn't surface this at all), stat tiles, by-band table + unallocated row. Both use `summary-rich` + `vat-integrity` (endpoints exist; portal api.ts needs the `fetchVatIntegrity` wrapper).

**⚠ VAT pre-flight audit (2026-07-30, before starting this WP) — findings BINDING on the port:**
- Live cross-check: SalesV2 headers == Σ SaleLines == VatRollups == SalesRollups **to the penny** (gross 55,708,170p / VAT 2,555,970p). The projection pipeline has zero drift.
- **Bug found & FIXED in `summary-rich`**: its byTaxRate table filtered `ItemIdOne != null` (the topItems barcode filter applied too broadly), silently dropping 8,118 ETL reconciliation-sentinel lines — the whole **19.81% legacy band disappeared** and the zero band under-reported £4,847.59 gross (£3.72 VAT dumped into "unallocated"). Fix: byTaxRate now aggregates ALL lines; only topItems keeps the barcode filter. Pinned by `VatBandCoverageE2eTests` (Σ band VAT == header VAT).
- **Source decision for the PORTAL port: keep `/api/v1/reports/vat` (VatRollups) as the numbers source** and port only the till's *UX* (month/quarter, tiles, integrity banner via `vat-integrity`). Reason: rollups honour financial-period locks (`EffectiveDay` late-post redirect); `summary-rich` buckets by raw `BusinessDay`. Today that's a no-op (0 closed periods, 0 late posts) but the portal is the financial surface — it must stay on the lock-respecting source. The till keeps `summary-rich` (now exact after the fix).
- FYI: 21,646 of 21,680 sales have `VatReconstructed=true` (ETL'd legacy data) — consistent (headers==lines), presentational note only.

**DoD:** portal VAT matches till's UX including the integrity banner; portal numbers still come from VatRollups; band table on BOTH surfaces sums exactly to the headline VAT.

### WP3.6 Prices page gets a real landing (resolves the "shows one item" confusion)
Prices stays a management tab. Replace the landing: a paginated **DataTable of ALL items** — barcode, name, category, band, policy, HQ price (falls back to legacy price), override count / max Δ — per-row **Edit** opens the existing `PriceDialog` unchanged. The current variance view becomes a **"Only where stores deviate"** filter toggle (same data, no longer the only view). Backend: needs a paged price-list endpoint — extend `/api/v1/prices` with a `GET /api/v1/prices/list?search=&skip=&take=` that joins Items (+CatId→name after WP3.3) with current central price + override counts. Keep the barcode quick-open box. No separate Reports entry.

### WP3.7 NEW: Category sales report — both platforms
Backend `GET /api/v1/reports/category-sales?from&to&granularity` (policy `portal.reports.view`): group sold lines by `Items.CatId` → per-category `{ category, qty, grossPence, discountPence, share% }`, optional per-period buckets for a stacked/simple bar view. UI: new sub-tab on both platforms — stat tiles (top category, categories sold), DataTable, simple SVG bar breakdown. Uncategorised lines roll into "(no category)".

### WP3.8 NEW: Best-selling report — both platforms
Backend `GET /api/v1/reports/best-sellers?from&to&by=qty|gross&take=`: reuse/generalise the `topItems` aggregation already inside `summary-rich` (ReportsController.cs:161ff) — extract to a shared private method, don't duplicate. Include `category`. UI: sub-tab on both platforms — rank, item, category, qty, gross, share%, delta vs previous equal period (reuse the till's Delta pattern). Sort toggle qty/gross.

### WP3.9 NEW: Negative items report → edit
Backend: extend `GET /api/v1/stock/levels` with `filter=negative` (`Quantity < 0` instead of `> 0` — same query shape, StockController.cs:50-77; no schema change needed). UI: Reporting → **Negative stock** sub-tab on the portal (and till Reports → Stock gains a "Negative only" toggle): DataTable of item/name/location/qty; **row action opens the existing stock adjustment dialog** (portal `ItemDialog` in StockPage — after WP4 it's the Inventory page's dialog) pre-focused on that item/location, so "you can then edit" is one click.

**DoD (phase):** every report exists on both surfaces per the matrix below; suites green.

| Report | Till | Portal |
|---|---|---|
| Summary (rich, deltas) | ✅ has | WP3.1 |
| Custom (+category) | has → WP3.2 adds category | WP3.2 (new) |
| VAT (+integrity) | ✅ has | WP3.5 |
| Items sold (+category) | WP3.4 | WP3.4 |
| Stock levels | ✅ has (Reports) | stays the Inventory page (+ a levels view is already in it); optional read-only Reports sub-tab if Matt wants strict symmetry |
| Price list | — (portal-only) | WP3.6 |
| Category sales | WP3.7 | WP3.7 |
| Best sellers | WP3.8 | WP3.8 |
| Negative stock | WP3.9 toggle | WP3.9 |

---

## Phase P4 — Inventory (rename, parity, categories)

### WP4.1 Rename portal "Stock" tab → "Inventory"
`App.tsx` TABS + PAGES map, page `<h2>`s. Keep the URL hash `#inventory`. Trivial but do it in this phase's first commit so later WPs read naturally.

### WP4.2 Portal Inventory = till inventory functionality (item CRUD)
Portal StockPage today = stock-ledger ops only (adjust/stock-take/transfer/locations). The till additionally has **item catalogue management** (`InventoryPage.tsx`: add/edit item — name, brand, description, cost, price-inc-tax with ex-tax derived, tax band, **category**, initial stock on create). Port that into the portal Inventory page as a second section/sub-tab ("Items" + "Stock ledger"):
- Reuse the **legacy endpoints the till already uses** (`api/Item` Index/Post/Put, `api/Tax`, `api/Category`) — matches till behaviour exactly, VAT guardrail included (ItemController Post/Put overrides). These need a `businessId` header: the portal gets it from `fetchCompanies()` (first company id) — cache it once in `api.ts` like the till caches BUSINESS_ID. *(Building fresh v1 item CRUD is deliberately out of scope — the guardrailed legacy path works on both surfaces; revisit only when the legacy bridge is retired.)*
- Item list: DataTable with **Category column + category filter**, barcode, name, brand, price, band.

### WP4.3 Category column/filter on BOTH inventory lists
Till `InventoryPage.tsx` list (currently Barcode/Name/Brand/Price only — the dialog already assigns categories, the list just doesn't show them) + the new portal Items list: Category column + filter select. Item Index serialises `CatId`; map id→name client-side from the categories fetch (both apps already have or gain `fetchCategories`).

### WP4.4 Category manager UI (the missing editor — webstore-critical)
Portal Inventory → **"Categories"** section: DataTable of categories with **item counts**, add / rename / delete. Delete is guarded: if `Items.Any(i => i.CatId == id)` block with "reassign first" (offer a bulk-reassign select). Endpoints: legacy `api/Category` CRUD (exists, zero UI today). `Item.CatId` is `[Required]` — every item must have a category, so seed/keep a default "(uncategorised)" category per business and never allow deleting the last one.

**DoD (phase):** portal tab says Inventory and does everything the till's inventory does; both lists show/filter category; categories are fully manageable; suites green (+ integration test: create category → create item in it → category-sales report shows it).

---

## Phase P5 — Loyalty (edit everywhere + cross-channel linking)

### WP5.1 Portal: make loyalty editable where you look at it
Root cause of "still not editable": the edit surface EXISTS but lives on the **Customers** tab (`CustomersPage.tsx` — create, edit, issue credit, set membership tier/discount, gated `customers.manage`), while the **Loyalty** tab is a read-only overview. Fix by wiring, not rebuilding: Loyalty rows get an **Edit/Open button that opens the same `CustomerDialog`** (extract it to a shared file), plus an "Add member" button (create customer → set membership in one flow). Convert to DataTable. Keep both tabs (Loyalty = membership/credit lens; Customers = full book) — or merge later if Matt prefers.

### WP5.2 Till: add + edit loyalty members
Till LoyaltyPage is read-only despite the API layer already having `createCustomer` and `canManageCustomers()` (customers.manage — held by Owner/Company Admin/Store Manager/Supervisor). Add: "Add member" (name/email/phone → create + optional tier) and per-row edit (details + tier), shown only when `canManageCustomers()`. Needs till api.ts wrappers for `PUT customers/{id}` + `POST .../membership` (endpoints exist).

### WP5.3 Cross-channel identity (till ⇄ webstore) — design + first slice
Today there is **no linkage**: `Customer` has no external ref; the Woo order webhook ignores `customer_id`/emails entirely. First slice (kept deliberately small):
1. Schema: `CustomerExternalRef { CustomerId, Provider("woo"), ExternalId, Email }` (new tenant-owned table; migration).
2. Woo order ingest: when the order payload carries an email, match case-insensitively to `Customer.Email` → record the ref; no match → create nothing (report-only until Matt opts in to auto-create).
3. Portal CustomerDialog shows linked refs ("Woo customer #123").
4. Till: loyalty lookup already searches by email/phone — unchanged, it just benefits.
**Decision needed** (see Decisions): auto-create loyalty customers from webstore orders, or link-only?

**DoD (phase):** members can be added/edited on till and portal; Woo orders link to loyalty customers by email; suites green (+ webhook integration test with a matching email).

---

## Phase P6 — Till platform alignment

### WP6.1 Store information pulled from the portal's data (read-only till)
Till `StoreInformationPage` currently reads/EDITS via legacy `/api/Business/{id}` + `/api/Store/{id}`. Wanted: the portal is the source of truth.
1. Backend: new `GET /api/v1/stores/{id}/info` — name, address lines, city, postcode, country, phone, opening hours — gated like the receipt-template read (`sales.ingest`, StoresController.cs:154 pattern) so till operators/devices can read it without `portal.company.manage`.
2. Till page becomes **read-only**: renders that info + "Edit in the management portal → Company / Locations". Remove the till's Business/Store edit forms (portal Company + Locations already edit all of it).

### WP6.2 Un-enrol device: RBAC gate + confirm + portal approval
Today "Forget this device" (SettingsPage.tsx:113-125) is **ungated, unconfirmed, local-only** (clears localStorage; no API call). Target flow:
1. **Gate the button** on `portal.tills.enrol` via the existing `canEnrolTills()` — that scope is held by exactly Owner / Company Admin / Store Manager (RbacSeeder), which matches "Owner or Store manager". No new permission needed. Others see an explanatory note instead of the button.
2. **"Are you sure?"** confirm dialog (destructive-styled, names the till + device).
3. **Portal approval as the final step:** new `UnenrolRequest` state — device calls new `POST /api/v1/tills/unenrol-request` (device-token auth, marks its Device `PendingRemoval`); portal Locations till card shows the pending request with **Approve** (→ existing revoke path, `EnrolmentService.RevokeTillAsync` semantics but per-device) / **Reject**. The till polls request status (or just detects its device token going 401 after approval) and only then clears the local credential. Add `DeviceStatus.PendingRemoval` (enum currently Active/Revoked only). A pending device **keeps trading** until approved.
4. Keep a break-glass: portal revoke (existing) always works regardless of any request.

### WP6.3 Help: top-right, own section, history, own permission
1. **Move Help out of Settings** on the till: a **"Help" button in the appbar top-right next to the 👥 user button** (App.tsx:152-183) opening a full-screen Help panel.
2. **Help panel** = raise-ticket form (reuse `AskForHelp` fields incl. Urgent flag) + **historical tickets** with status chips + threaded messages + reply box. All endpoints exist (`GET /api/v1/support/tickets`, `GET/POST .../{id}/messages` — SupportController) — the till just never consumed them; add till api.ts wrappers. Announcements banner stays where it is.
3. **RBAC:** correcting the assumption — there's no settings read/edit permission today; support endpoints are open to any authenticated tenant user. Add TWO new permissions to the catalogue + seeder:
   - `support.tickets` — raise/read/reply to support tickets. Seed to **all** built-in roles (a cashier alone at 9am with a dead till must be able to shout for help — flag to Matt if he'd rather exclude Cashier). Gate the Help UI + `api/v1/support/*` client endpoints on it.
   - `pos.settings.manage` — the till Settings tab's device-level toggles (receipt behaviour, carrier-bag barcode, printer, enrolment section visibility). Seed to Owner / Company Admin / Store Manager. Settings tab renders read-only (or hides cards) without it.
   Backfill happens automatically: `EnsureBuiltInRolesAsync` re-seeds new permissions into built-in roles on deploy.
4. Portal already has a Help tab with history — after the till panel exists, extract/align the thread UI so both look the same.

**DoD (phase):** store info read-only from v1; un-enrol is gated+confirmed+portal-approved (integration test: request → approve → token 401); Help is top-right with full history on the till; the two new permissions exist, are seeded, and gate what they should; suites green.

---

## Decisions — ALL RESOLVED by Matt, 2026-07-29 (nothing blocks implementation)

| # | Decision | Resolution |
|---|---|---|
| D1 | Prices under Reports? | **Resolved:** the confusion was the variance-only landing (one override → one row). WP3.6 rebuilds the landing as a full browsable price list; Prices stays a management tab. |
| D2 | Can Cashiers raise Help tickets? | **Yes — everyone incl. Cashiers.** Seed `support.tickets` to all built-in roles. |
| D3 | Webstore→loyalty | **Link existing only** (match by email; no match → nothing). Auto-create becomes a config flag later if wanted. |
| D4 | Un-enrol UX | **Wait for portal approval** — till marks itself pending, keeps trading, forgets only after approval. |
| D5 | Duplicate read-only Stock report on portal? | **Skip** — Inventory covers it. |

## Suggested build order & sizes

P1 (M) → P2 (M) → P3 (L — biggest; 3.1/3.5 are ports, 3.2/3.7/3.8 are new, 3.3 is the backend keystone — do 3.3 first) → P4 (M) → P5 (M; 5.3 is the only risky bit) → P6 (M). Each phase is independently shippable; deploy cadence per phase, Matt's call.

## Pitfalls for the implementing session (learned the hard way)

- **Duplicate controller = 500.** The ItemController AmbiguousMatch outage (2026-07-29) — before adding any controller/route, grep for the class name AND route in both `Plutus/Endpoints/Plutus.DBService/Controllers` and `src/*`. Consider adding the arch test that asserts no two actions share a route (proposed after that outage, still unwritten — good WP1 stowaway).
- **`Item.CatId`**, not CategoryId. Composite keys `(Id, BusinessId)` on legacy entities; `ItemIdOne` = barcode string join.
- Legacy endpoints (`api/Item`, `api/Category`, `api/Tax`) want a **`businessId` header**; v1 endpoints use ambient tenant. Don't mix conventions in one page silently.
- EF: no computed `DateTime`/`TimeSpan` arithmetic inside `Select` projections (the payments/unresolved 500) — compute in memory after `ToListAsync`.
- New tenant-owned entities (`CustomerExternalRef`, `UnenrolRequest` if an entity) → `TenantOwned` array + tenant integration-test seed needs `Entitlements="[]"`, `ConnectionRef=""`.
- Migrations: `dotnet ef migrations add <Name> --project Plutus/Commons/Plutus.Entities --startup-project Plutus/Data/Database.Migrations.Startup --context MySqlDbContext -o Migrations/MySql` (the DBService startup project fails by design).
- Portal + till are separate npm apps — the shared DataTable is a **copied twin**; keep them identical, header-comment the twin path.
- Deploy: backend `pm2 restart ~/PLUTUS/plutus-ecosystem.config.js --update-env` (restarting by name silently misses new env keys); frontends build ON the Mac (Node 26), portal needs `VITE_OIDC_AUTHORITY`/`VITE_OIDC_CLIENT_ID` inline.
- After ANY Mac change: ETRIE health → 200.
