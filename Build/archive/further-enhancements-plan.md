> **📦 ARCHIVED — implemented.** FE1–FE9 are all built & LIVE. FE3 (the hardware helper agent) was
> the last one open and is now **verified on real hardware** (2026-08-07): silent receipt printing
> and the cash drawer work from the browser till via `tools/Plutus.TillAgent`. Two known
> follow-ups, both documented in `tools/Plutus.TillAgent/README.md`: the cash drawer on a Star
> TSP143 needs a one-time Star OPOS registration on that PC, and the agent binary is unsigned.

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
- **Tables**: the shared `DataTable` standard exists ([table-standard.md](../table-standard.md),
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
- **FE3.0 (done) is that visibility path**: the agent only has to serve `/status` with
  `{ agentVersion, printer: { name, online } }` — the till already forwards it and Locations
  already displays it. The FE3.2 agent needs no server-side work to be fleet-visible.

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
| FE3.0 | **Agent telemetry → portal (Matt, 2026-07-31: "see what agents are running on the tills in Locations").** Built AHEAD of the agent so the fleet view lights up the moment one is installed: 4 nullable columns on `Devices` (migration `AddAgentTelemetry`), `POST /api/v1/tills/agent-status` (SalesIngest-gated, tenant-checked, telemetry-not-audit — a poller must never grow an audit table), agent fields on the tills list, till `hardware.ts` (polls `http://127.0.0.1:9123/status` with a 1.5s timeout, reports on change or 6-hourly, NEVER affects till behaviour), and an agent chip on Locations' device chips. Semantics: never-reported = native till / pre-FE3 web till (no chip); reported with null version = **"no agent"** chip; reported with a version = **"agent v1.2.3 · printer ✓/offline"**. ⚠ NRT gotcha: the body record needed `string?` — [ApiController] + non-nullable string turns a null into an automatic 400 before the action runs. E2E: `Agent_telemetry_round_trips_from_device_report_to_the_tills_list`. Deployed 2026-07-31 (rollbacks `backend.pre-agenttel`, portal+till `current.pre-agenttel`); the live web till reports "no agent" on its next page load. | ✅ 2026-07-31 |
| FE3.1 | Spike: skeleton tray app + `/status`; confirm HTTPS-page→localhost fetch on the till browser; test print through `CommonPOSLibrary` on a real deployed printer model. | ◑ **agent built + endpoints verified on the dev box 2026-07-31**; the two on-site checks remain (see below) |
| FE3.2 | Agent v1: endpoints, token pairing, settings window, tray health, ESC/POS receipt formatting from the till's receipt payload. | ✅ 2026-07-31 |
| FE3.3 | Till: `hardware.ts` facade; Settings "Hardware" card (agent URL default + token, test buttons); checkout wiring (silent print + drawer kick, PDF fallback); health indicator. | ✅ 2026-07-31 |
| FE3.4 | Packaging: MSI/winget, auto-start; install doc in HANDOVER.md. | ✅ 2026-07-31 — self-contained single-file exe (61MB compressed) + self-registered auto-start + `tools/Plutus.TillAgent/README.md`. **MSI deliberately deferred** (see below). |
| FE3.5 | Gate: end-to-end on a physical till PC — sale → silent receipt + drawer kick; unplug printer → PDF fallback + red indicator. | ☐ **needs Matt at a till PC** |

### FE3 as built (2026-07-31) — five things worth knowing

**1. The till sends a RENDERED DOCUMENT, not the sale.** `receiptDoc.ts` turns `ReceiptData` into a
list of print ops (text+align/bold/large, rule, barcode, cut, drawer) and the agent only turns ops
into bytes. Receipt layout — the per-store template, header/footer, VAT number, barcode toggle —
already lives in the till and is cached per store; re-implementing it in the agent would mean two
copies to keep in step, and the first template change would print the old receipt on paper and the
new one on PDF. The op vocabulary is deliberately `CommonPOSLibrary.PrinterBaseOperations`, which the
Xamarin/MAUI tills already speak.

**2. ⚠ RAW spooler, not OPOS — and this is the one real on-site risk.** The agent writes ESC/POS
straight to the Windows print queue (`RAW` datatype), which works with the ordinary vendor driver a
shop printer is normally installed with. The MAUI/Xamarin tills instead use WinRT
`Windows.Devices.PointOfService` (OPOS), which needs a vendor UPOS service object. If Kapow's printer
only exposes itself that way the RAW path fails — so the transport sits behind `IReceiptTransport`
and `ClientUI/.../PosPrinter.cs` is the port source. **FE3.1 on-site settles which.**

**3. Security: loopback is NOT enough, so there's a pairing token.** Any web page the cashier opens
can also fetch `localhost`, so `/print` and `/drawer/open` require `X-Agent-Token` (20 Crockford
chars, generated on first run, typed into Settings → Hardware once). `/status` stays open — the till
must be able to ask "is an agent here?" before pairing, and that is what feeds FE3.0's fleet view.

**4. Graceful degradation is enforced, not hoped for.** Every `hardware.ts` call returns a boolean
and swallows its own failure; the agent returns **503** (not 500) for "hardware unavailable" so the
till reads it as a cue, not a bug. No agent / wrong token / printer off / agent wedged → the sale
completes exactly as today and the browser receipt opens. A shop must keep trading with a dead
printer.

**5. What is actually verified, and what isn't.** Verified on the dev box by running the agent:
`/status` unauthenticated; `/print` + `/drawer/open` **401** without the token; **503** with the token
and an absent printer, surfacing the real Windows error (1801 = invalid printer name) — so the
P/Invoke path is genuinely reached. ESC/POS byte generation is unit-tested (15 tests): mode resets
(a missed one bleeds double-height down the receipt), **CP437 `£` = 0x9C** (UTF-8 here prints garbage
on paper while looking perfect on screen), Code 39, cut-feeds, drawer pulse. NOT verified: paper out
of Kapow's printer, the drawer physically opening, and whether the HTTPS till page may fetch
`http://127.0.0.1` in the till's browser (expected to work — browsers treat loopback as potentially
trustworthy — but a five-second check worth doing first).

**MSI deferred, deliberately.** v1 is a copy-and-run exe that registers its own per-user auto-start
(no admin rights). WiX authoring earns its keep when an install is repeated across many machines or
pushed by Group Policy; for a handful of tills it is ceremony. The exe is the same artefact an MSI
would carry, so packaging later changes nothing about the agent.

**Till deploy:** rollback `/srv/apps/PLUTUS/web/current.pre-fe3`. The agent is not deployed anywhere —
it is built on demand (`dotnet publish tools/Plutus.TillAgent`) and copied to a till PC; its build
output is git-ignored.

## FE4 — Table standard everywhere

**Requirement (Matt, 2026-07-30):** every table across the portal (and till) must be
orderable, split into pages, and have a 25/50/100 page-size dropdown. "Looking at the tables
across the portal, they are not all consistent." The standard already exists
([table-standard.md](../table-standard.md): shared `DataTable` twins, client + server modes,
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
| FE5.1 | Category → filtered Items click-through. | ✅ 2026-07-30 |
| FE5.2 | Current-stock column (batched levels) portal + till. | ✅ 2026-07-30 |
| FE5.3 | `inventory.bulk` perm + bulk endpoint + portal bulk UI (both selection modes). | ✅ 2026-07-30 |
| FE5.4 | Bin: migration, exclusions, RBAC-gated Bin view with restore. | ✅ 2026-07-30 |
| FE5.5 | `StockUntracked`: migration, consumer/API skips, UI tick box, report/outbound handling. | ✅ 2026-07-30 |
| FE5.6 | Gate: tests (bulk criteria vs ids, bin exclusions, untracked sale posts no movement), deploy + `SeedMigrator rbac`. | ✅ 2026-07-30 deployed — rollbacks `backend.pre-fe5`, portal/till `current.pre-fe5`, `seedmigrator.pre-fe5`; DB dump `plutus-pre-fe5-20260730.sql.gz` |

### FE5 as built — four things worth knowing

**1. The Bin needed FOUR exclusions, not one.** `ItemParameters` covers the lists, but three other
paths read items directly and would each have leaked a withdrawn item:
- **the till's barcode lookup** (`GET api/Item/{id}`) goes through the generic `FindById`, NOT the
  filter — so a binned item could still be **scanned and sold**. `ItemController` now overrides it:
  a binned barcode 404s exactly like an unknown one (verified live), with `?includeBinned=true` for
  the Bin view;
- **webstore outbound** (`PushNewItemDraftsAsync`) would have pushed binned items as web drafts;
- **the webstore SKU resolver** would still match a binned SKU, silently selling a withdrawn item
  online instead of routing the order to the review queue.

**2. The legacy item PUT binds the WHOLE entity**, so `itemBody` in BOTH frontends had to echo the
new columns back. Without that, an ordinary "edit item" would have cleared `StockUntracked` — and
**silently un-binned a binned item**. Fixed in the portal and till, with the reason commented at
both sites.

**3. `[Required]` rejects empty strings**, which `Category.Description` is. The Uncategorised bucket
had to be created with a real description; a test caught it before the code could fail on live data.

**4. The SeedMigrator binary had to be rebuilt.** `RbacSeeder` compiles INTO it, so the 27-Jul binary
on the Mac would have re-seeded the OLD permission set and silently not granted `inventory.bulk`.
Republished, then run: grants landed on exactly Owner / Company Admin / Store Manager.

**Live DoD run:** bulk count by filter (790 for "batman") · a user with **no roles** is 403'd (the
right gate test — `perm:*` resolves from RBAC, not token scopes, so an Owner token passes whatever
scope string it carries) · mark untracked → stock reads ∞ · bin → **barcode scan 404s** → appears in
the Bin view → hidden from the normal list → restore → scannable again → untracked cleared ·
ids+criteria together 400s. Live state left clean (0 binned, 0 untracked, no stray Uncategorised),
with four `inventory.bulk.*` audit rows as the trail.

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
| FE6.1 | Re-issue code endpoint + one-active-device rule + portal/till UI wording. | ✅ 2026-07-30 (12 enrolment tests) |
| FE6.2 | Tills group in Locations + till→store move endpoint. | ✅ 2026-07-30 — also ported the 3 tables FE4 deferred, and DELETED `sortable.tsx` (last consumer gone) |
| FE6.3 | Test-env till hygiene (user decision). | ✅ moot — Matt authorised the phantom-till cleanup earlier on 2026-07-30; both test tills and all their data are gone |

### FE6 as built

**The gap, closed.** `POST /api/v1/tills/{id}/enrol-code` mints a fresh single-use code for an
EXISTING till. Redeeming it **revokes that till's previous device** (one till = one counter, which
also keeps `DeviceSeq` monotonic per till). The till id — and therefore its whole sales history — is
preserved. Issuing a new code invalidates any outstanding unused one.

**Wording fixed at the source of the confusion.** The till's Settings button read "Generate a code"
under a heading that sounded like re-enrolment; it actually created a NEW till — precisely how
`Till 019f9630` was born. It now reads **"Create a NEW till"** and points explicitly at
Locations → Tills → *New code* for re-enrolment.

**Locations reworked:** Stores (each card's till table now on `DataTable`, with a per-till "New code"
action) · **Tills** (NEW flat fleet view — till, store, device chips, last online, webstore badge,
plus New code and Move) · Warehouses · Webstores (now showing which virtual till carries its orders).

**Auth note:** the tills endpoints are **scope**-gated (`PlutusPolicies.PortalTillsEnrol`, read from
the token claim) — unlike FE5's `inventory.bulk`, which is a `perm:*` RBAC policy. So the correct
gate test is a token WITHOUT the scope (403 confirmed), not a user without the role.

**A bug the live run caught:** a same-store move returned **500**. The service returned early before
setting `_db.CurrentUser`, and the controller then wrote its audit row and saved on that same context
— which the context refuses without an actor. **Third instance of this exact trap** this session
(after both loyalty backfills), so it now has a dedicated regression test reproducing the
controller's hand-over pattern.

**Live DoD:** re-issue keeps the till id and creates no new till · enrolling activates a new device
and shows the previous one **Revoked** · the code is single-use (410 on replay) · unknown till 404 ·
bad store 400 · missing scope 403 on both endpoints · same-store move 204.

⚠ **Side effect of the live DoD, needs one action from Matt:** the run enrolled a throwaway device
against the real **"Kapow Web Till"**, so that till's original browser device is now **Revoked** and
will stop trading at its next token refresh. A fresh code has been issued for it — re-enrol that
browser (Settings → Till device) using the code in the session notes. Lesson recorded: run
device-level DoD steps against a scratch till, not a live one.

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
| FE7.1 | Entities + migration; generate/activate/redeem/void endpoints + ledger; `giftcards.manage` perm; tests (over-redeem, expiry, double-activate, idempotent redeem per sale, **activation posts zero VAT**). | ✅ 2026-07-30 (7 E2E + 9 code-format + 4 VAT tests) |
| FE7.2 | Till: activate at basket + redeem tender in checkout + auto-scan prefix; offline block. | ✅ 2026-07-30 |
| FE7.3 | Portal: gift-cards table + detail/void/link + generate dialog. | ✅ 2026-07-30 (own tab) |
| FE7.4 | Print templates (voucher + A4). | ✅ 2026-07-30 (+ a batch sheet) |
| FE7.5 | Outstanding-liability report (portal Reporting + dashboard pill next to store-credit). | ✅ 2026-07-30 (page stats + dashboard pill) |
| FE7.6 | Gate + deploy (migration + `SeedMigrator rbac`). | ✅ 2026-07-31 deployed |

**DoD:** sell + activate a card at the till (offline attempt blocked) → VAT report for the
day shows ZERO VAT from the activation; redeem partially against a mixed-band basket → goods
VAT normal, card balance reduced, tender split correct; redeem the remainder; over-redeem
409s; liability report reconciles to Σ unredeemed balances; A4 + voucher print render with a
scannable code; scan a printed voucher's barcode into the till and it resolves.

### FE7.7 — the VAT-treatment decision (Matt, 2026-07-31) — ✅ DONE, deployed 2026-07-31

**Requirement:** per the [gov.uk voucher rules (1 Jan 2019)](https://www.gov.uk/government/publications/changes-to-the-vat-treatment-of-vouchers/vat-treatment-of-vouchers-from-1-january-2019),
whether VAT is charged when a card is **sold** (all goods one rate — single-purpose voucher) or when
it is **spent** (mixed rates — multi-purpose) must be spelled out and selected by the store owner
before gift cards are enabled.

**As built:** a per-tenant `GiftCardSettings` row (migration `AddGiftCardSettings`) whose **absence is
the gate** — generate/activate/redeem all 409 with "choose the VAT treatment first" until the owner
decides. The portal Gift cards tab shows ONLY the decision screen until then: both options in plain
English, the gov.uk link, "ask your accountant" copy, and an in-app confirm restating the legal
meaning of the choice. `PUT /api/v1/giftcards/settings` is `giftcards.manage`-gated and audited.

- **Multi-purpose** (what FE7 originally hard-coded): zero-VAT activation, card is a tender at
  redemption, VAT from the goods. Unchanged.
- **Single-purpose** (new mechanics): the till prices the activation line WITH VAT in
  (ex = amount/1.2, band **pinned at 2000bp** — deriving it from rounded pence wobbles to
  1998–2002bp and scatters the VAT report into phantom bands), and redemption becomes a **negative
  standard-rated line instead of a tender** — reducing the sale's VAT-able consideration by exactly
  the VAT embedded in the card. A plain tender would have declared the goods' VAT a second time.
  Pinned to the VatRollups by `GiftCardVatTests`: sell a £30 card → £5 VAT that day; spend it on £24
  of 20% goods → **zero further VAT**.
- **The choice LOCKS at the first card sale** (any ledger entry): its VAT is by then declared under
  the chosen treatment, so flipping it would misstate a return. Re-affirming the same value is
  always a no-op (test seeding relies on that). Until then, changeable in the portal.
- The till's lookup response now carries `vatTreatment`, and every customer-facing line of copy
  (sell prompt, basket note, page banner, liability blurb) states the treatment in force — it is a
  legal statement, so it must match what the sale posts.

**⚠ LIVE STATE: the Kapow tenant is deliberately UNDECIDED** — `GiftCardSettings` is empty, the
endpoints 409, and the portal shows the decision screen. **Matt makes the call in the portal**
(Kapow sells 20% + 5% + Exempt, so multi-purpose looks right — but the declaration is his).
Verified live: settings `{treatment:null}`, generate → 409. Rollbacks `backend.pre-fe7vat`,
portal+till `current.pre-fe7vat` (schema change is one new empty table — no dump needed beyond
the nightly).

Tests: +1 E2E lifecycle (fresh fixture: gate 409s → bad value 400 → cashier 403 → decide → change
while unsold → generate/lookup carries treatment → first sale locks → change 409, re-affirm 200) and
+2 unit (SPV through the rollups, SPV line arithmetic); suites 317 unit / 59 integration green.

### FE7 as built — five things worth knowing

**1. ⚠ An activation needs a REAL catalogue item, so one is provisioned automatically.** The sales
pipeline requires every line to reference an item, and the legacy projection writes a `Transaction`
row whose `(ItemIdOne, ItemIdTwo)` is a FOREIGN KEY to `Items` — a synthetic id would break the
bridge on the first card sold. `GiftCardSaleItem.EnsureAsync` (startup, idempotent, per business)
creates `GIFT-CARD` "Gift card": **zero-VAT band** (picks the band with `Rate <= 1.0`; live = "Exempt"),
`StockUntracked` (a card is not inventory), price 0 (the till sets the line price to the amount
loaded), in its own **"Gift cards" category** so cards never inflate a product category's sales.
Live: provisioned for 1 business on the FE7 boot, `TaxId=3` (Exempt, Rate 1.0). ⚠ It sets
`db.CurrentUser` before any early return — the FE1 lesson.

**2. Money is taken, but it is NOT turnover — and the reports say so.** An activation appears in the
day's takings (the till really did take the cash) and on the **zero VAT band**, which is the correct
treatment: a multi-purpose voucher's VAT falls due on the goods it is later spent on. What the build
does NOT do is strip activations out of gross turnover — that would mean reworking every report. The
Gift cards page states this in plain English and gives the accountant the two numbers they need
(`outstandingPence` = deferred income, `activatedPence` = what to back out). Pinned by
`GiftCardVatTests`: a £12 goods + £25 card sale rolls up as £37 takings / **£2.00 VAT**, with the
card's £25 on the zero band.

**3. `TenderType.GiftCard = 4` — a new enum value, no migration** (same as `DeviceStatus.PendingRemoval`).
Deliberately NOT reusing `Credit`, so the payment-split report doesn't lump gift cards in with store
credit. The legacy bridge maps it onto the legacy *credit* PayMethod (there is no gift-card row, and
adding one would put a free-typed "Gift card" tender on every checkout screen); the v1 `SaleTenders`
row keeps the true type, which is what reporting reads.

**4. Codes are RANDOM, not sequential** (unlike FE2 member numbers): 12 Crockford32 characters + a
weighted (7,3,1) check character = 60 bits. A gift card is a bearer instrument — a guessable code is
a licence to print money. ⚠ The prefix trap: `G` is itself in the alphabet, so a bare code can start
with `G`; the payload prefix is only stripped at full length, or one valid card would canonicalise
into another. Pinned by `GiftCardCodeTests`.

**5. Four ways to lose money, all closed.** Gift-card lines take no discount (member or manual) —
a 10% discount on a £20 card hands over £20 of goods for £18. Quantity is locked at 1 (one line =
one code; "2 ×" would charge twice and load once). A card cannot pay for a card (that would launder
an expiring balance into a fresh one). And a past activation **cannot be refunded at the till** — that
would return the money while the card kept its balance; voiding the card is the audited remedy.

**Live DoD run (2026-07-31):** generate 3 → unsold holds £0 and redeem 409s → activate £25 →
double-activate **409** → redeem £10 (bal £15) → **replay the same entryId: still £15** → over-redeem
£15.01 **409** ("That card only has 15.00 left") → void (redeem 409) → unvoid (balance back) → bogus
code 404 → **one character flipped 404** (the check character earning its keep) → liability
£25 outstanding / 2 live / 1 unsold / £35 activated / £10 redeemed → history `Issue:2500 Redeem:-1000`
→ generate as a no-role user **403**, activate as a `pos.sell`-only cashier **200**. The three DoD
cards were then hard-deleted (their entries carried no SaleId, so nothing referenced them) —
`GiftCards`/`GiftCardEntries` back to 0 rows.

**Rollbacks:** backend `~/PLUTUS/backend.pre-fe7`, portal + till `current.pre-fe7`, DB dump
`~/PLUTUS/backups/plutus-pre-fe7-20260730.sql.gz`. Migration `AddGiftCards` (two new tables only,
nothing altered).

⚠ ~~**Pre-existing, unrelated:** `commercial-sweep … CurrentUser not defined!`~~ — **FIXED
2026-07-31.** All three commercial sweeps (Churn/Renewal/Compliance) saved on a fresh unscoped
context without setting `db.CurrentUser` — the fourth instance of this bug class (after the FE1/FE2
backfills and FE6's till move). It lay dormant until 2026-07-29, when the first tenant actually
crossed a signal threshold (no signal → nothing to save → no throw), then failed every hourly run.
The existing test had masked it by setting CurrentUser itself; the new regression
(`The_sweeps_save_without_a_CurrentUser_set…`) mirrors the sweeper's exact construction and was
verified red-without/green-with the fix. Each sweep now sets its own CurrentUser, like `UsageSweep`
always did. Live: the first-ever successful run (08:48, JobRuns status 1) immediately raised the one
signal it had been trying to write — **`dpa-missing` for Kapow Comics Ltd** (no signed DPA on
record), now visible on the Platform dashboard. Rollback `backend.pre-sweepfix`.

**FE7.7 decision MADE (2026-07-31, Matt's instruction):** the live Kapow tenant is declared
**multi-purpose** (catalogue is 20%/5%/Exempt — mixed rates, which is the MPV case under the rules).
Gift cards are now enabled live; the choice stays changeable in the portal until the first card is
sold. The declaration smoke-test card was deleted (unsold, worthless); GiftCards/GiftCardEntries
back to 0 rows.

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
| FE9.1 | Set-password + reset-token endpoints, IMessageSender email, login-page forgot/complete flow, rate limiting. Tests: token single-use/expiry/hashing, no-email 409, min-length. | ✅ 2026-07-30 (11 tests) |
| FE9.2 | Remove/restore endpoints + self/last-Owner guards; typed-name confirm dialog; pickers exclude removed. Tests: guards, credential revoked, history intact. | ✅ 2026-07-30 (6 tests) |
| FE9.3 | Permission descriptions + enriched roles endpoint; portal Roles section + matrix. Test: role grants in the API match RbacSeeder exactly. | ✅ 2026-07-30 (2 tests) |
| FE9.4 | Per-user collapsed access matrix with role attribution. | ✅ 2026-07-30 |
| FE9.5 | Last-login stamp + column; invite variant; audit slice link. | ✅ **COMPLETE** — stamp + column + invite 2026-07-30; audit-slice link 2026-07-31 (see below) |
| FE9.6 | Gate + deploy (no migration except LastLoginAtUtc + reset-token table; `SeedMigrator rbac` not needed — no new permissions). | ✅ 2026-07-30 deployed — rollbacks `backend.pre-fe9` + `portal/current.pre-fe9`, DB dump `plutus-pre-fe9-20260730.sql.gz`; DoD verified live |

### FE9 as built — three things that differ from the sketch

**1. ⚠ Reset emails do NOT currently reach anyone, and the UI now says so.** No Email provider is
enabled in Platform → Notifications, and `ConfiguredMessageSender` returns early in that case —
it doesn't even journal to MessageEvents (journaling starts only once a provider is *selected*,
which then simulates). The plan assumed "simulated but journaled"; that's only true post-selection.
So the endpoint returns `sent`, and the dialog reports the truth: on `sent:false` it warns
"⚠ Link created but NOT emailed … set a password directly instead and tell them out of band."
The token is still minted and valid, so the flow is ready the moment a provider is turned on.

**2. `LastLoginAtUtc` lives on the `Employees` table, not `People`.** Employee is TPT with `Person`
as the root, and the property is declared on `Employee` — so the raw-SQL stamp in `AuthController`
had to target `Employees`. Caught by reading the generated migration, verified live (a real password
login stamped `2026-07-30T19:08`).

**3. Admin-set password kills outstanding links** (and vice versa) — one live credential path at a
time. Verified live: after `POST /password`, the pending reset row was already marked used.

**4. Per-user audit slice — DONE 2026-07-31 (was deferred).** Each user row gets an **Activity**
button opening the last 200 admin actions they took, on the standard DataTable (searchable across
action / entity / raw detail JSON). The **Remove** dialog links straight to it — "is this dormant
account safe to remove?" is really "what did they touch?", so the answer sits one click from the
decision. Reachable for REMOVED users too: their trail is exactly what you want to read afterwards.

⚠ The plan claimed `/api/v1/audit` "already supports filtering" — it did **not**; it only filtered
`entityType`. An `actorUserId` query parameter was added. Also worth knowing, and said in the dialog's
own copy: only ADMIN actions are audited (roles, prices, bulk edits, tills, settings) — everyday
selling is in the sales reports. So an empty list means "changed no settings", not "did no work".

**Live DoD run:** 17 described permissions · 11 roles (Owner = 17 grants, 2 members) · create user →
set password (204) → short password (400) → send reset (200) → **remove self blocked (400)** →
remove test user (login revoked, roles dropped) → hidden from the list (2 visible / 3 with
`includeRemoved`) → restore (204) → anonymous reset request for an unknown email (204, no
enumeration) → bogus token (410). Test rows removed afterwards.

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

### OUT OF SCOPE — Matt's decision, 2026-07-30
Matt reviewed the deployed tables and confirmed two are **not** to be brought onto the standard
table, despite being inconsistent with the rest:

- **Reporting → Summary → "Top selling items"**
- **Reporting → Summary → "Sales by payment method"** (same screen)

Both are report breakdowns inside a summary view rather than browsable collections. They keep their
current hand-rolled look. Noting it explicitly so a later sweep doesn't "fix" them and so the FE4
audit doesn't read as incomplete. (The till's mirror of the same screen is out of scope for the same
reason.) If they ever grow long enough to need paging, revisit.

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
4. ~~**FE9** (users & roles)~~ — ✅ **COMPLETE 2026-07-30** (FE9.1–9.6; audit-slice link deferred).
5. ~~**FE5** remainder~~ — ✅ **COMPLETE 2026-07-30** (FE5.0–5.6).
6. ~~**FE6** (till identity + Locations IA)~~ — ✅ **COMPLETE 2026-07-30**; `sortable.tsx` deleted.
7. ~~**FE7** (gift cards)~~ — ✅ **COMPLETE 2026-07-31**; own portal tab, till sell + redeem, print formats, liability.
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
   ⚠ **BUILT AS ITS OWN TAB instead (2026-07-31) — a deliberate deviation from this default, for
   Matt to veto.** The surface outgrew a section: four liability stats, a status filter, the card
   table, a generate dialog, a per-card dialog (history + void + adjust + link) and three print
   formats. Nesting that under Loyalty (which is about members and store credit) would have buried
   it and made the Loyalty tab two unrelated pages. The tab bar is now 13 wide. Moving it back is a
   ~15-minute change: render `<GiftCardsPage/>` inside `LoyaltyPage` and drop the TABS entry.
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
