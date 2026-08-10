# Till design — the single source of truth for every till build

**Every till Plutus ships is designed here.** What the surfaces are, what each can do, and where
every shared rule lives. If a question is about a till, the answer is in this document or it belongs
in this document.

> ### The binding rule
>
> **Any work on any till reads this document first and updates it in the same commit.**
>
> **1. Parity is the default — new functionality ships to EVERY till.** Not to the till that
> happened to prompt it. A till left behind is a *decision*, and a decision has a name and a
> number: a ⬜ in Part B whose Notes say which work package will close it and why it can wait.
> "We'll do the other one later" may be written down exactly once — as a WP reference. If there
> is no WP, the feature is not finished.
>
> **2. A capability isn't done** until its row exists in Part B with every till marked ✅, a
> deliberate ⬜, or ➖.
>
> **3. A rule isn't done** until Part C says where it lives — and, if it exists in more than one
> place, what stops the copies drifting.
>
> This is what keeps till versions in sync. Not memory, not review, not good intentions.
>
> *Matt, 2026-08-08:* "**The tills need to be in parity. This is the point of the MAUI retrofit.**
> In addition when adding new functionality, it needs to be added to all tills going forward."

**Legend:** ✅ built · 🟡 partial · ⬜ absent · ➖ not applicable · ⏸ blocked externally.
Last verified against code **2026-08-08**. Retrofit WPs refer to
[`To do/MAUI-Retrofit-Plan-2026-08-07.md`](To%20do/MAUI-Retrofit-Plan-2026-08-07.md).

*Consolidated 2026-08-08 from `till-parity.md` (features) and `till-anatomy.md` (build + rules) at
Matt's instruction — the two questions were always one question, and splitting them meant a till
could look complete in one document while being wrong in the other.*

---

## Why this document exists

Two failures, a week apart, and neither was carelessness.

**The web till gained nine features in eight days** at the end of July. The MAUI parity analysis
written on 2026-08-01 was already missing several of them by 2026-08-07 — because there was nowhere
for a gap to show up. Prose written once decays; a table every feature has to touch does not.
*That's Part B.*

**The VAT arithmetic existed only in the web till's TypeScript**, described in the retrofit plan as
"the reference implementation" — meaning every other till was expected to re-derive it by reading
someone else's language and getting it right. There were already three partial copies, including a
hardcoded UK VAT band list inside a client library. A feature register could never have shown this:
it read ✅ / ✅, because both tills *do* have VAT. Two tills that disagree by a penny on the same
basket disagree on **every VAT return, forever**, and nothing flags it. *That's Part C.*

---

# Part A — What the tills are

## A1. The surfaces

Everything that can produce a sale, and what it's made of.

| Surface | Technology | Runs on | Local state | Reaches the server via |
|---|---|---|---|---|
| **Web till** — `Plutus.Frontend.WebApp` | React 19 + TypeScript, Vite | Any browser | IndexedDB (outbox + catalogue cache) | `POST /api/v1/sales`, plus some legacy `/api/*` |
| **MAUI till** — `Plutus.Frontend.AppClient` | .NET MAUI (Sean's rework, NatApp lineage) | `net10.0-windows`, `-android`, `-ios` | SQLite — legacy schema today, local store v2 at cutover | Not yet — WP5+ wires it through `Plutus.Client.Core` |
| **Webstore connector** | Backend module `Plutus.Webstore` | Server | — | Its sink builds an `IngestSaleRequest` and calls `SalesIngestService` **directly** |
| **Hardware agent** — `tools/Plutus.TillAgent` | WinForms tray app + Kestrel on `127.0.0.1:9123` | `net10.0-windows` | Token in local config | Not a sales path — it prints and kicks the drawer for the *browser* till |
| **Portal** — `Plutus.Frontend.Portal` | React 19 + TypeScript | Any browser | — | The **source of truth**, not a till: it publishes what tills obey |
| ~~`Plutus.Frontend.ClientUI`~~ | MAUI | — | — | **Abandoned port, being retired.** Kept only to harvest its colour palette. Do not build on it. |

⚠ **The webstore is a sales channel, and it is easy to forget.** It doesn't look like a till, but it
writes sale lines, so every rule in Part C applies to it. It was the channel found sending no VAT
band at all.

## A2. The shared spine

The libraries any .NET till uses. All are plain `net10.0` — no MAUI, no UI, no OS-specific target.

| Project | What it holds | Rule |
|---|---|---|
| `src/Plutus.SharedKernel` | **The rules.** Money, VAT arithmetic, ids, permissions, band resolution. | No project references at all. |
| `src/Plutus.Contracts.Client` | The wire contract (DTOs + the `LineMeta` envelope). | **No references, no packages** — it ships onto tills. |
| `src/Plutus.Client.Core` | Outbox engine, pusher, API client, device-token provider. | May reference only SharedKernel + Contracts.Client. |
| `src/Plutus.Client.Storage` | Local store v2 (SQLite) + cutover. The only place that knows SQLite. | As above. |

**Three architecture tests hold this, so it can't rot by accident:**

| Test | What it refuses |
|---|---|
| `Till_client_libraries_stay_free_of_MAUI_and_backend_modules` | A UI/MAUI package, or a reference to a backend module, in a till library. |
| `Till_libraries_stay_platform_neutral_and_hold_no_VAT_rates_of_their_own` | An OS-specific target framework in a till library, or a literal VAT rate in one. |
| `No_module_references_another_module` | Backend modules reaching into each other. |

---

# Part B — What each till can do

**The rule:** when you add a capability to any till, add its row here in the same commit. If another
till isn't getting it, say so in Notes and why. A deliberate ⬜ is a decision; a missing row is a
surprise six months later.

## B1. Selling (the Till screen)

| Capability | Web | MAUI | Backend | Notes |
|---|:--:|:--:|---|---|
| Scan / search / manual add | ✅ | ✅ | `GET /api/Item` | |
| Basket qty, price adjust, reorder | ✅ | ✅ | — | |
| Discounts | ✅ | ✅ | — | |
| Returns / refund lines | ✅ | ✅ | `GET /api/v1/sales/{saleId}` | **Cutover steps 15+16, 2026-08-09.** `Services.Sales.ReturnLookup` prefers the SERVER record whenever it answers — goods bought on till B and returned at till A is ordinary retail, and only the platform knows what has already been given back elsewhere — and falls back to this till's own record ONLY inside the 14-day rolling window. Outside it the answer is "reconnect", never a guess. `RefundRules.Authorise` (shared) decides; `WasCapped` is surfaced to the operator because silently handing over less than was asked for is how a refund becomes an argument. ⚠ It replaced a block that **crashed the app**: `trans.First()` on the legacy `Trans` table threw from an `async void` with no catch the moment the item wasn't on that sale, so scanning the wrong receipt closed the till — and that table is empty and permanently so on a portal till, while its "refunds left" test counted quantities on one machine. ⚠ The refunded price is the one from the ORIGINAL sale, not today's catalogue price. ⚠ **The local cap is per SALE, not per line** (`LocalRefunds` records money per origin sale): it enforces "never more than was paid" in aggregate, and per-line accounting is step 17's, server-side. |
| **Refund-only baskets** (negative sale) | ✅ | ⬜ | `POST /api/v1/sales` | Shipped 2026-08-07. Invariants are sign-agnostic, so it's client work only. |
| Park / retrieve basket | ✅ | ✅ | — | MAUI must reserialise as contract JSON, no `$type` — WP2. |
| Split payment / change | ✅ | ✅ | — | |
| **Gift cards — sell & redeem** | ✅ | ⬜ | `/api/v1/giftcards/*` | **Whole feature missing.** Zero references in MAUI. ⚠ Gated on the per-tenant VAT-treatment decision — `GiftCardSettings`' absence **409s** generate/activate/redeem, so a MAUI till that doesn't understand this gets errors it can't explain. **WP13** (body + DoD). |
| Customer attach at sale + auto-discount | ✅ | ⬜ | `/api/v1/customers` | WP12 |
| Member-number scan-to-attach | ✅ | ⬜ | `/api/v1/customers` | FE2 `NNNNNNC` barcode. **WP12** — body + DoD added 2026-08-08; the check-digit rule is `SharedKernel.MemberNumbers`, never re-derived. |
| Store credit as tender | ✅ | ⬜ | `.../credit/redeem` | Online-only by design. WP12 |
| **Add unknown scan as a new item** | ✅ | ⬜ | — | Shipped 2026-08-07. **WP10** — body + DoD added 2026-08-08 (it was ruled into WP10 but never specified). |
| Effective pricing at basket-add | ✅ | ✅ | `GET /api/v1/prices/effective` + the timeline on `catalogue/changes` | **WP5, built 2026-08-08.** The feed carries the item's **whole price timeline** (future points included), its policy, and this till's store overrides; `TillStore.EffectivePricePenceAsync` resolves with the shared `PriceResolution` at the SALE's instant. ⚠ A price write must call `PricingService.TouchItemForSyncAsync` or it never reaches a till — the feed pages by `Item.ModifiedAt` and prices live in their own tables. Pinned by `A_PRICE_change_reaches_the_till_even_though_prices_do_not_live_on_the_item`. |
| Card capture events | ⏸ | ⏸ | `POST /api/v1/payments/events` | Blocked on a provider for **both**. Terminal integration is greenfield — but note the till already *displays* the configured gateway (next row). |
| Payment-gateway awareness at checkout | ✅ | 🟡 | `GET /api/v1/payments/gateway/active` | **WP14** — shared half landed 2026-08-09 (`Client.Core.PaymentGateway.Resolve`, 13 tests); MAUI is 🟡 pending the checkout XAML. ⚠ **The rule is NOT "read `provider`".** Every provider reports `integrated: false` today, so a till switching on the provider name would wait for a terminal that never answers with a customer in front of it. **`integrated` decides the flow; `provider` only decides the wording** — a chosen-but-unwired provider is still the manual flow, named, with "integration pending" shown. ⚠ No answer at all = standalone and **not** "pending": a cosmetic lookup must never block a card sale, and a tenant that never chose a provider must not be told an integration is coming. Web till decides the same three cases inline at `CheckoutDialog.tsx:226-233`. |

## B2. Receipts & hardware

| Capability | Web | MAUI | Backend | Notes |
|---|:--:|:--:|---|---|
| Silent receipt print | ✅ | ✅ | — | Different transports, same outcome: web goes through `Plutus.TillAgent`, MAUI drives OPOS directly. MAUI does **not** need the agent. |
| Cash drawer kick | ✅ | ✅ | — | As above. |
| **Portal-controlled receipt template** | ✅ | ⬜ | `GET /api/v1/stores/{id}/receipt-template` | Shipped 2026-08-06. MAUI's layout is **hardcoded C#** (`PosPrinterManager.cs:138-167`) — it *does* print the store's name/logo/address/VAT from the locally-synced StoreModel, but it ignores the portal template entirely (header/footer lines, toggles), contradicting the "all receipts controlled from the portal" requirement. **WP3** — the receipt renders from the portal template, DoD-gated. |
| Reprint from a past sale | ✅ | 🟡 | `GET /api/v1/sales/{saleId}` | MAUI reprints locally only; cross-till needs WP11. |
| ⚠ **Finding a past sale AT ALL** (list / search recent sales) | ✅ | 🟡 | `GET /api/v1/sales` — already answers | ⚠ **THIS, not the refund rule, is what made refunds unusable.** Returns, the refund cap and the read path all shipped at steps 15–17 — but the "Returns" dialog asked for the **original sale ID**, and `TillStore.FindLocalSaleAsync` takes a `Guid` with **no browse, no search, no "today's sales"** anywhere in the app. The only source of that id was the **barcode on a printed receipt**, so a till with no printer could not refund anything. A complete feature with no door; reported by Matt 2026-08-10 as *"In MAUI I cannot do a refund?"*. ✅ **Closed 2026-08-10**: `TillStore.ListRecentSalesAsync` + a picker in front of the Returns dialog — pick the sale, then give only the reason. ⚠ Reads THIS TILL's own record, so it works with the line down, which is when a shop most needs to hand money back; typing an id remains for goods bought on another till. ⚠ **A QUEUED sale is offered** — the money left the drawer when the goods did, whatever the outbox has managed to deliver. Pinned by `TillStoreReadPathTests` (18). 🟡 until it also covers **cross-till** sales and **reprint** (step 26 — one screen serves both). |
| Barcode on receipt (sale id) | ✅ | ✅ | — | |

## B3. Tabs the MAUI app has no equivalent for

| Area | Web | MAUI | Retrofit |
|---|:--:|:--:|---|
| **Cash** — float, paid in/out, X, Z | ✅ | ✅ | **WP9 / cutover step 23 — MAUI closed 2026-08-10.** Its own tab beside the Till, because a shop cannot OPEN or CLOSE without it. ⚠ **The server was already finished and nothing client-side existed** — no screen, no client method, and the five type names appeared nowhere in the app or the shared libraries. ⚠ **QUEUED, NOT POSTED** (`LocalCashEvents`, schema v4, drained by `CashPushService` on the 60s tick): a shop opens before its broadband does and the money moves whether or not the platform hears, so a float that failed to post is a day whose banking cannot be reconciled at all. ⚠ **ONE Z PER DAY is enforced LOCALLY as well as server-side**, refusing every type after it rather than merely a second Z — the server's guard cannot be consulted with the line down, which is exactly when it matters. ⚠ **The EXPECTED figure is the SERVER's**: only the platform sees the sales half (including sales another device on the same till posted), and a till caching its own would disagree with the banking report with no way to say which was right. ⚠ On the drain, **409 and 400 are TERMINAL** and kept with the server's words — a till that retries them for ever looks healthy while quietly never banking. 10 tests, mutation-checked twice. ⚠ **USER-VERIFY outstanding: does the drawer physically kick?** `POSCashDrawer.InitPOSObject` was missing the `return` on its success path until 2026-08-10, so this is the first build where it can be exercised. |
| **Loyalty** — members, tiers, credit | ✅ | ⬜ | WP12. Zero references in MAUI. |
| **Users** — employee list/create + set password (legacy `/api/Employee`, `/api/Auth/SetPassword`) | ✅ | ⬜ | **WP8** (body + DoD; the list and set-password halves gained DoD lines 2026-08-08). MAUI's add-user command is a stopgap dialog reading *"not available in this version yet"*. Roles and effective-permissions management are **portal-side**, not till capabilities — the MAUI parity target is this smaller surface. Overlaps WP8 (RBAC sync) but is a distinct screen. |

## B4. Tabs both have, in different shapes

| Area | Web | MAUI | Retrofit |
|---|:--:|:--:|---|
| Inventory CRUD | ✅ | 🟡 | **WP10 / step 25.** ✅ **EDIT (name + price) landed 2026-08-10**, against `PUT /api/Item/{id1}` — the same endpoint the web till uses in production. ⚠ **READ-MODIFY-WRITE, always**: that PUT binds the WHOLE entity, so a bare price change would clear `StockUntracked` or blank `BinnedAtUtc` — **restoring a withdrawn item to sale on every till in the estate**. `PlutusApiClient.UpdateItemFieldsAsync` fetches first and echoes everything back, and the client deliberately exposes no way to construct the payload from scratch. ⚠ **The ex price is DERIVED from the item's existing pair, never typed**: the server guards `|price − exPrice × rate| ≤ 2p` because free-typed ex-prices corrupted 47 live items (a £7.99 item with a £799.00 ex-price) and with them every downstream VAT figure. ⚠ **Needs an OPERATOR token** — the legacy base controller reads an `objectidentifier` claim in its CONSTRUCTOR, so a device token does not merely fail the policy, it 500s before the action runs. 🟡 not ✅ until CREATE, the stock ledger, categories and the Bin land — see below. **Original correction, 2026-08-10:** this row read ✅ / ✅ and was WRONG — MAUI's add/edit/stock screens wrote into the LEGACY local database, and the till client has no item-write endpoint at all (`PlutusApiClient` reads the catalogue and nothing more), so a till-created item reached no report, no other till and no VAT return. Since cutover step 11 the basket resolves items from the v2 catalogue, so it could not even be SOLD on the machine that made it. Both were also gated on the legacy `IsAuthorised`, which **crashed the app** rather than refusing. Hidden 2026-08-10 — see [`legacy-removal.md`](legacy-removal.md) L2. A ✅ here is what "reads as built and is not" looks like; the ⬜ is the honest state. |
| **Item search by NAME in the scan box** | ✅ | ✅ | **Closed 2026-08-10.** ⚠ **MAUI had none, and its absence read as an empty catalogue**: `TillViewModel.FindItem` called `FindByBarcodeAsync` and nothing else, so anything TYPED — "BAT" — was tried as an exact barcode, missed, and produced *"We can't find an item with that ID"* against 20,344 synced, sellable items. ⚠ `TillStore.SearchAsync` had been built, correct and tested since 2026-08-09 and was called from **nowhere** — the third finished component found sitting unwired behind a screen that looked broken (after `OutboxPusher.DrainAsync` and the catalogue browse). ⚠ Barcode is tried FIRST and stays exact: a scan is the hot path and must never open a picker in front of a queue. Matching is `SharedKernel.ItemSearch` (C1), never a `LIKE` written at the call site. `matchAllWords` is a per-device preference defaulting to **true on both tills** — two tills that merely *default* differently would disagree about the same query out of the box. |
| Stock as a movement ledger | ✅ | ⬜ | WP10. MAUI writes a flat quantity column; concurrent edits are last-write-wins. |
| VAT-band consistency guard | ✅ | ⬜ | **WP10** — the guard was in WP10's body but gated by no DoD until 2026-08-08. |
| **Portal-published VAT bands** (`GET /api/v1/vat/bands`) | ✅ | ⬜ | **WP2c shipped 2026-08-08 (backend + web till).** The web till caches the bands and their whole effective-dated timeline at boot + every 60 s, and its last hard-coded VAT rate (the single-purpose gift-card `/1.2`) is gone. ⚠ **MAUI had NO WP home for this until 2026-08-08** — WP2c asserted "both tills cache it" and only the web till did, while `PlutusApiClient.GetVatBandsAsync` + `VatBandsResult.RateBpAt` sat built and uncalled. Now **WP5**, with a DoD proving a future-dated change applies offline on the day. ⚠ **Caching only "today's rate" is a bug**: the timeline is what lets an offline till apply a future-dated change on the day. |
| **VAT band on the sale line** (`LineMeta.vatBand`) | ✅ | 🟡 | **WP2c-exempt, 2026-08-08.** Zero-rated and exempt both declare `vatRateBp = 0`; the band is the only thing separating them, and it decides whether input tax is recoverable (Notice 706). **MAUI SHOULD send it** — resolve from the item's legacy `TaxId` against each band's published `legacyTaxIds`; `LineMeta.VatBand` exists in `Plutus.Contracts.Client`. **Leave it null rather than guessing** when the portal hasn't mapped the tax row. 🟡 not ⬜ because **the server backfills any line that arrives without one** (`VatBandStamp`, applied in `SalesIngestService` — the single choke point every channel passes through), so MAUI is correct-by-default and only needs to send it for cases the catalogue can't know. A single-purpose gift-card activation/redemption is `"standard"` by the voucher treatment, not its catalogue row — that IS such a case. |
| Category create / rename / reassign / delete | ✅ | 🟡 | WP10. MAUI creates locally; no reassign UI, so the server's 409 has nowhere to land. |
| The Bin (soft delete) + untracked stock | ✅ | ⬜ | FE5 concepts MAUI has no model for. **WP10** — body + DoD added 2026-08-08. ⚠ A binned item must stop selling on an OFFLINE till, which is what WP5's tombstones (`CatalogueItem.Removed`, built and unread) are for. |
| Reporting — cross-till, server-aggregated | ✅ | 🟡 | **WP11 / step 26 — first slice landed 2026-08-10.** The Statistics tab now shows **this till's real takings for today from the platform** (`GET /api/v1/reports/summary?level=till`), where a red "these reports read the OLD local database" warning used to be. ⚠ **The figure MUST come from the server**: a till cannot know its own takings — sales posted by the other device on the same till, sales pruned out of local history, and refunds taken at another counter against sales rung up here all belong in the number an operator counts a drawer against. ⚠ **The endpoint was gated on `portal.financials.view` ALONE**, so a Supervisor — who holds `pos.reports.view` and no portal permission — could close a day with a Z-read and be refused the takings they had just counted against. Now carries the `pos.*` alternative, like `/api/v1/sales` since step 19. ⚠ **The test that should have pinned that alternative had been passing for the wrong reason since step 19** — it assigned Store Manager, which holds BOTH portal reporting permissions, under a comment claiming it held neither. Found by mutation, corrected to Supervisor. 🟡 until the fuller reports (items sold, VAT, best sellers) and the cross-till lookup + reprint screen land. ⚠ The two LEGACY reports remain reachable and still warned about — a till migrated from NatApp holds real pre-cutover history in that file and this is the only way to see it. Original note: WP11. MAUI queries its **own** SQLite, so it can only ever show one till. |
| Store Information | read-only ✅ | local editor ⬜ | WP6 — the two are *inverted*; this is a deletion, not a build. |
| Settings — local prefs | ✅ | ✅ | — |
| Settings — device enrolment & identity | ✅ | 🟡 | **WP4 + WP16a.** MAUI gained a **Plutus tab** 2026-08-08: server address, enrolment code → `POST /api/v1/tills/enrol`, connection state, device status, and buttons that exercise the heartbeat and catalogue feed. Secret goes to platform `SecureStorage`, device id to `Preferences` — ⚠ never the SQLite file, which gets copied off machines during support. 🟡 until the un-enrol *request* flow and the local store are wired. |
| First-time startup | ➖ | 🟡 | **Portal-first since 2026-08-08** (Matt: *"The till is moving to the portal being the 1st place you start, not the local app"*). First run now opens **Connect to Plutus** — server address + enrolment code — because a till is *born in the portal*: company, store, till and code are created there, and the app's only job is to claim that identity. ⚠ **Legacy "Setup" and "Third-party transfer" are retitled `(Legacy)` and pending removal** — see the warning below. 🟡 until they go and WP8 lets a synced operator sign in. |

## B5. Platform citizenship

Most of this exists in the web till and is **absent from MAUI**. None of it was in the original
parity analysis. ⚠ **Two rows now run the other way** — connection status and offline sign-in are
places the *web till* is behind. Parity is not a synonym for "catch MAUI up".

| Capability | Web | MAUI | Backend | Notes |
|---|:--:|:--:|---|---|
| Offline trading + outbox | ✅ | 🟡 | `POST /api/v1/sales` | **Cutover step 11, 2026-08-09: checkout now COMMITS a platform sale.** `CheckoutCommit` assembles an `IngestSaleRequest` and `TillStore.CommitSaleAsync` writes the sale and its outbox entry as ONE row in ONE transaction, allocating `DeviceSeq` inside it. Until then the till wrote a legacy EF graph and called `db.Save()` — the sale existed on that machine only and appeared in no report, no VAT return and no other till's history. ⚠ **COMMIT BEFORE PRINT** is now structural, not incidental: a printer failure is a reprint problem, never a money problem. ⚠ A failed commit does NOT clear the basket — clearing would lose the sale and the evidence together. ✅ **Cutover step 13, 2026-08-09: the outbox now DRAINS.** `OutboxPushService` + `TillCadence` — the till's ONE background clock, ticking every 60s exactly like the web till's `App.tsx`: heartbeat → outbox drain → catalogue (only when the beat says it moved) → notices. Before it, `OutboxPusher.DrainAsync` was referenced **nowhere in the app**, so every sale the till committed sat in the queue for ever while the till looked healthy. ⚠ **One timer, not four** — four drift into each other and fire together behind a rate limit sized for one. ⚠ **Drain runs BEFORE the catalogue pull**: a sale already rung up is worth more than a price nobody has asked for, and on a slow link the catalogue can take the whole interval. ⚠ **The basket owns the catalogue sync** (`TillCadence.BasketIsOpen`) — a sync mid-basket would price one line from before the tick and one from after, in a single sale. The drain and heartbeat are NOT held. ⚠ **Started at app start, not sign-in, and never stopped on sign-out** — a till on its login screen after close still holds the day's takings. ⚠ Quarantine is reported separately and never softened: 202 is terminal, so it is the one outcome that will not fix itself. The Plutus tab's **"Send queued sales now"** is the only place a stuck queue is visible. WP3. |
| **Taking payment at all** (the tender buttons) | ✅ | ✅ | — | ⚠ **Cutover step 13b, 2026-08-09 — a portal-provisioned till could not complete a sale AT ALL.** The checkout read the legacy local `PaymentMethodModel` table, seeded only by `Database.Init()` (the legacy first-run path). A portal till never runs it and has no local database, so the payment sheet rendered **zero buttons** and the `paid != sale.Total` loop could never terminate — the operator was trapped mid-checkout with nothing but Cancel, and nothing threw or logged. Every dev machine hid it: they were migrated from legacy installs that had already seeded Card and Cash. Now `Services.Sales.TillTenders` — the **fixed** shared tender set (binding default 13), not a roster. ⚠ Each name must map back through `Tenders.FromMethodName` to its own byte, pinned by test, because the wire byte is derived from that STRING at commit. ⚠ Change is cash-only, cashback is card-only (the pairing is easy to invert — mutation-checked), and a refund gives neither. ⚠ `Online`/`Credit`/`GiftCard` are deliberately absent: the first is webstore ingest, the other two are online-only and unbuilt, and a button that must fail is worse than no button. |
| **Connection status — network vs server vs revoked** | 🟡 | ⬜ | `GET /api/v1/ping` + `GET /api/v1/tills/devices/{id}/status` | **WP16a.** Shared rule in `Plutus.Client.Core.ConnectivityProbe` (C1). ⚠ **The web till is 🟡, not ✅**: it reads `navigator.onLine` only (`App.tsx:60`), which reports the network interface and never asks whether the server is there — so it shows "online" in a shop whose broadband is down, and shows "offline" for a revoked till. Both tills move onto the shared probe. |
| **Offline sign-in with an expiry** | ⬜ | ✅ | `GET /api/v1/tills/{id}/operators` | **WP8 + WP16b, built 2026-08-08.** Horizons tiered and shared (`SharedKernel.OfflineCredentials`, C1). ⚠ **The web till still cannot sign in offline at all** — a row where MAUI is now AHEAD, so this parity gap runs the other way and needs a WP of its own. |
| Device enrolment | ✅ | ✅ | `/api/v1/tills/enrol` | **WP4 · MAUI closed 2026-08-09** (cutover step 4). Enrolment goes through `Client.Storage.EnrolmentFlow`, so the till records **where it is**, not just who it is: `TillId`, `StoreId`, `BusinessId`, `TenantId`, `ServerUrl` into the v2 store's Meta. ⚠ It called `api.EnrolAsync` directly until then — the credential was saved and the store learned NOTHING, so every screen needing a storeId had nothing to read and the failure was silent because enrolment itself succeeded. ⚠ `BusinessId` is the LEGACY business id, not the tenant id: `DeterministicGuid.ForItem` seeds from it, so the wrong one diverges every item id from the web till's, permanently and without a symptom. Placement is re-read on **every start** (`TillPlacement.RefreshInBackground`) because a till can be MOVED between stores in the portal — and that is also what self-heals tills enrolled before this landed. ⚠ The archive gate (D-defaults §9.3) stays OFF until cutover step 21 gives the app a way to archive; turning it on first would refuse enrolment with no way through. |
| Un-enrol request + manager approval | ✅ | ⬜ | `/api/v1/tills/unenrol-request` | WP4 |
| Heartbeat / fleet status | ✅ | ✅ | `POST /api/v1/heartbeat` (**built 2026-08-08**) | **WP5.** Presence is in-process (`TillPresence`, 2min ONLINE / 5min STALE) — a fleet beating every 60s must not be a MySQL write per till per minute. ⚠ A failing heartbeat is **silent**: it must never stop a till selling. ✅ **MAUI closed 2026-08-09** — `TillCadence` calls `BeatAsync` on its 60s tick. ✅ **The WEB till closed 2026-08-10, and had never beaten at all**: the endpoint shipped 2026-08-08 with only a MAUI caller, so the surface *most* of the estate runs was permanently absent from the fleet list, and it read as "those tills are switched off" rather than "nobody wrote the caller". `api.ts sendHeartbeat()` + a 60s interval in `App.tsx`. ⚠ **Silent when the browser has no device credential** — an un-enrolled tab has no identity to report, and inventing one would put a phantom till in the list. ⚠ **The reported version is PERSISTED on `Device`** (`AppVersion`, `AppVersionReportedAtUtc`), written only when it CHANGES so the common beat stays a pure read. Presence alone forgot every version on a backend restart — which is exactly when somebody is looking at the fleet list. |
| Hardware-agent telemetry (agent version/health → Locations) | ✅ | ➖ | `POST /api/v1/tills/agent-status` (`TillsController.cs:283`) | FE3.0. ➖ for MAUI: it drives OPOS directly and has no agent to report on. |
| **Pick-from-floor notifications** (web sale sold shop-floor stock) | ✅ | ⬜ | `GET /api/v1/notifications?unackedOnly=true` + `POST /api/v1/notifications/{id}/ack` | Phase 6. Banner + acknowledge on the till (`App.tsx:264-272`), polled on the 60s cadence. **WP5b** — the shared half landed 2026-08-09 (`Client.Core.NoticesClient`, 23 tests), so MAUI is ⬜ pending BOTH its banner XAML **and a caller** — `NoticesClient` is referenced once in the whole AppClient, in a comment (see the Announcements row). ✅ **FIXED SERVER-SIDE 2026-08-09 (WP17.4)** — `GET /api/v1/notifications` now filters by the caller's store, derived from the DEVICE TOKEN and never from the query string. That **deletes the C2 twin** rather than pinning it: a per-client filter is a rule every future till has to re-implement correctly. MAUI filtered (`NoticesClient.IsForStore`), the web till did not (`App.tsx:130` set whatever arrived), so in a multi-store tenant staff at one shop hunted stock that was never on their shelves while the shop that had it assumed the other branch had dealt with it. ⚠ Notes with NO store stay visible to everyone — they are tenant-wide by construction. Pinned by two `PickNotesE2eTests`, mutation-checked. ✅ The two mis-gated endpoints were fixed 2026-08-07 (they were gated on `perm:sales.ingest` — a *scope*-policy name used as a *permission* code, which no RBAC role can hold, so every till poll 403'd silently into its `.catch`). ⚠ Their tenant isolation rests entirely on the **global query filter** (`WebstoreNotification` is in `MySqlDbContext.TenantOwned`), not on a predicate in the controller — pinned since 2026-08-09 by `A_till_cannot_see_or_ack_another_tenants_pick_note`, which was mutation-checked against `IgnoreQueryFilters()`. |
| **Announcements banner** (maintenance/incident) | ✅ | ⬜ | `GET /api/v1/announcements/active` | **WP5b.** Shared half done 2026-08-09 — `NoticesClient.ShowsOnATill` is the one home for *which severities reach a till*: **`Info` is portal-only**, and an **unrecognised severity SHOWS** rather than being swallowed, so a severity added by a later backend can't silently blank itself on every till. ⚠ **MAUI is ⬜, not 🟡 — corrected 2026-08-10.** The row said "pending the banner XAML", which
implied the rest was wired. It is not: `NoticesClient` appears in the AppClient **exactly once, in a
comment**, so nothing calls it and nothing puts it on `TillCadence`'s 60s tick. **This is the FOURTH
component found fully built, tested and referenced from nowhere** — after `OutboxPusher.DrainAsync`,
the catalogue browse and `TillStore.SearchAsync`. Remaining work is a cadence step **and** the XAML,
not the XAML alone. |
| **Help / support tickets** | ✅ | ⬜ | `/api/v1/support/tickets` | **WP5b.** Closes the `support-heavy` churn signal. |
| **Component version — each till its own** | ✅ | ✅ | `GET /api/v1/ping` returns the **backend's** | Matt, 2026-08-08: *"each till needs a specific version as they will end up diverging when you have specific Windows or Linux challenges."* One file per deployable in **`versions/`**: `till-web.txt` · `till-maui.txt` · `backend.txt` · `portal.txt` · `agent.txt` · `platform.txt` (the shared libraries). **`MAJOR.FEATURE.FIX`** — see [`versions/README.md`](../versions/README.md). ⚠ **A shared number was the first attempt and was wrong**: a Windows-only printer fix would have forced the web till to claim a release it had no changes in, and left "broken on 1.4.2" unanswerable. ⚠ **No surface may hardcode its own** — one nobody bumps names the wrong build in every report against it. Pinned by `ComponentVersionTests` (17). |
| **Card surcharge** (tenant-set fee on card tenders) | ⬜ | ✅ | `GET /api/v1/payments/gateway/active` carries `surchargeBp` + `surchargeFlatPence`; portal Company → Card payments sets them (typo-capped 10% / £5, audited) | **Built 2026-08-09** on the gateway settings row — the legacy `PaymentMethod.Charge` could never carry it because that table is GLOBAL, one tenant's fee would have been every tenant's. MAUI adds a REAL line against the provisioned `CARD-SURCHARGE` item (`CheckoutCommit.SurchargeItem`): fee = `CardSurchargeVat.FeePence` (percent + flat, the shape of an acquirer's own pricing), VAT = `CardSurchargeVat.PairFor` — **the fee follows the basket** (Bookit C-607/14 / NEC C-130/15: further consideration for the main supply), zero on zero-rated goods, blended on mixed, NEVER a hardcoded rate. Applied once per sale, card tenders only, never on refunds; offline uses the last-known setting from Meta. ⚠ Replaced a legacy path broken twice over: misspelt resx key (`CardChangeNote`) and a money-carrying `BasketNote` the commit guard refuses. ⚠ **Web till ⬜, deliberately**: it reads `charge`/`minimumCharge` off the legacy wire and ignores them (`CheckoutDialog.tsx:54,112`) — no tenant charges a fee today (UK consumer surcharges are BANNED since 2018-01-13; the portal helper text carries the warning), so the web half rides WP15's checkout reshape rather than a one-off TS edit nothing can typecheck on this box. |
| **Receipt off the COMMITTED sale** | ✅ | ✅ | — | **Cutover step 14, 2026-08-09.** `PosPrinterManager.SetUpSalePrint` now takes `Printing.ReceiptSale`, built from the payload the platform accepted (`CommitOutcome.Request`), not the legacy `SaleModel`. ⚠ **The barcode had been EMPTY since step 11**: the footer printed `SaleModel.Id`, and step 11 deleted the legacy `db.Save()` that assigned it — nothing threw, and the barcode is the ONLY way a customer's receipt finds its sale again for a reprint or a receipt-led refund. It now carries the platform `saleId`, which is what step 15's read path keys on. ⚠ Totals are the committed ones, never a re-sum of the basket — two opinions about one sale means the paper and the record can differ by a penny with no way to tell which was charged. ⚠ Timestamp is LOCAL (the payload is UTC); a receipt an hour out gets queried at a chargeback. |
| ⚠ **How a receipt reaches paper — the HARDWARE ROUTE** | ✅ | ✅ | `POST http://127.0.0.1:9123/print` — the **Plutus Till Agent**, a tray app on the till PC | ✅ **Closed 2026-08-10 (till 1.33.0).** Matt: *"I still cannot see a printer, it says wifi is turned off, but I do not understand what this means? The webtill can see the receipt printer fine."* Both halves were true and had ONE cause — **the two tills were on different hardware routes**. The web till has always POSTed a rendered document to the agent, which prints through the **ordinary Windows print queue**, so every driver-installed printer is available to it. MAUI asked Windows for a `PointOfService` device: a specialist driver profile almost no receipt printer ships, so the list was empty — and the picker Windows shows for that selector is the **generic device chrome**, which fills an empty list with its stock advice about Bluetooth and Wi-Fi Direct radios. ⚠ **"Wireless is turned off" was never about the printer.** It is the picker answering a question nobody asked, which is exactly why the screen was impossible to act on. MAUI now speaks the same route: `Client.Core/TillAgentClient.cs` + `ReceiptDocumentBuilder`, paired from Settings → Receipt printer with the agent's own code. ⚠ **The drawer rides WITH the print job** — one round trip, and it opens as the paper starts moving, as it did on OPOS; the checkout resolves which route it is on BEFORE dispatching anything, or agent and OPOS would both kick it. ⚠ **Graceful degradation is structural**: every method returns false rather than throwing, so no agent / wrong code / printer off all leave a committed sale alone. OPOS survives one level down for a till with a genuine POS device. Pinned by `TillAgentClientTests` (9) + `ReceiptDocumentTests` (11), mutation-checked four ways. |
| **Reprint a receipt for a past sale** | ⬜ | ✅ | — (reads this till's own record, so it works with the line down) | ✅ **Cutover step 26, 2026-08-10 (till 1.34.0).** Statistics → **Reprint a receipt** → pick from this till's last 20 sales. A customer who has lost their receipt could not be refunded: the barcode on the paper is what the refund flow keys on. ⚠ **The copy is MARKED "REPRINT — not a new sale", above the first rule.** That is a money rule, not a courtesy — the refund flow accepts a sale found by a receipt barcode, so two indistinguishable papers for one purchase is the shape of a double refund, and this till has already let £13.99 out twice from a different cause. ⚠ **The barcode is the SAME** as the original's: marking distinguishes the paper, never the sale, or the copy could not find the sale at all, which is the whole point of asking for one. ⚠ **The drawer does NOT open** — no money is moving, and a drawer that opens when nothing is happening teaches operators to ignore it. ⚠ **Refunds ARE reprintable** (`purchasesOnly: false`), unlike the refund picker: refunding a refund is invalid, reprinting one is not. ⚠ Every figure is the STORED payload's; re-deriving from today's catalogue would hand back a different total after a price change. Item NAMES are looked up locally because the wire carries only ids — a withdrawn item falls back to its barcode rather than a blank. Pinned by `ReceiptReprintMarkingTests` (5), mutation-checked on the marker position and the barcode. **Web till ⬜ — WP11**, where it joins the reporting screen that already lists sales. |
| **Item edit — the FULL field set** | ✅ | ✅ | `GET`/`PUT /api/Item/{id1}`, `/api/Tax/Index`, `/api/Category/Index` | ⚠ **Reworked 2026-08-10 (till 1.34.0) after a second report** — Matt: *"Maui edit items is missing category and tax e.g. 20%."* Both readings were true. The sheets came AFTER the form, so opening "Edit item" showed name/brand/price and no tax or category anywhere; and **an empty list SKIPPED the sheet in silence**, so a failed `/api/Tax/Index` call made the capability vanish without a word — indistinguishable from one never built. Now: the three choices come FIRST, the form then shows the chosen **tax band, category and stock setting as read-only rows** beside the price, and an empty list SAYS SO and keeps the item's existing value. ⚠ **The band shows its PERCENTAGE** (`Standard — 20%`) via `Client.Core/TaxBandLabel`, because a band called "Standard" or "T1" cannot be checked against a price — and checking is the only reason to show it. See C1. ✅ **Extended 2026-08-10 (till 1.33.0).** Matt: *"I can now edit, but it seems to be missing a lot of options compared to the webtill."* Correct — the web till offers barcode, name, brand, description, cost, price, tax band, category and stock tracking; MAUI offered **name and price**. It now offers all of them: text fields in the input alert, and action sheets for the three a text prompt cannot ask for (tax band, category, stock tracking). ⚠ **The barcode stays read-only, as it is on the web till** — it is half the composite primary key, so changing it is a DELETE plus an INSERT that orphans every stock movement, sale line and webstore listing pointing at the old one. ⚠ **The ex price is DERIVED from the chosen band and `rate` is a MULTIPLIER** (1.2 = 20%), so it divides; multiplying makes a £10 item's ex price £12 and the server's `|price − exPrice × rate| ≤ 2p` guard rejects the write with a message that never mentions units. ⚠ **Zero-rated and Exempt both have rate 1.0 and are different bands** — the sheet shows NAMES and writes the id, never a rate comparison. ⚠ Still read-modify-write through `UpdateItemFieldsAsync`: the PUT binds the whole entity. |
| **Parked baskets** (park / recall) | ✅ | ✅ | — (local only, binding default 15 — the web till parks locally too) | **Cutover step 18, 2026-08-09.** Moved onto the v2 `SavedBasket` table, which had existed, been mapped and been indexed since the store was built while **nothing ever touched it** — parking wrote to the LEGACY database, which on a portal-provisioned till is an empty file the app creates on first use. ⚠ Serialised as **contract JSON with a plain `kind` discriminator**, never Newtonsoft `TypeNameHandling.Auto`: that wrote .NET type names (`NatApp.Plutus.Models.BasketItem, NatApp.Plutus`) into the blob, which stop resolving the moment a namespace, assembly or class is renamed — and this codebase has renamed all three, which is why a discounted parked basket crashed the app on recall. ⚠ **Read before delete** on recall: the old code deleted the row and *then* parsed its copy, so an unreadable blob destroyed the basket as well as losing it. ⚠ **The parked price is kept, not re-resolved** — a basket is a quote made to a customer who walked away. ⚠ Loading moved OFF the UI thread (the constructor read SQLite synchronously inside `BeginInvokeOnMainThread`). Pinned by `ParkedBasketTests` (15), mutation-checked on the return discriminator. |
| **Operator platform token** (for `perm:*` routes) | ✅ | ✅ | `POST /api/Auth/Login` — the SAME endpoint the web till uses (binding default 11) | **Cutover step 19, 2026-08-09.** The roster still AUTHENTICATES (offline, against synced PBKDF2 hashes); this is a different need — the till's `perm:*` routes are gated on what the OPERATOR may see, and a device token identifies the machine, not the person. Fetched in the background after a successful roster sign-in; failure is silent, because a till with no network must still open. ⚠ **Held in memory only**: it is a bearer token with no server-side denylist, so a copy on disk would outlive every revocation the platform can perform. ⚠ Expiry is checked on READ, not by a timer — a token that lapsed overnight must not be handed out because nothing woke up. |
| **A till can request its own removal** | ✅ | ✅ | `POST /api/v1/tills/unenrol-request` | ⚠ **The caller it was written for could not call it.** Its doc comment has always said *"a till asks to be un-enrolled"*, and it was gated on `portal.tills.enrol` — a scope a DEVICE token cannot carry — so only a portal admin could ever raise the request and the till's own button had nothing to talk to. Now `TillsUnenrolRequest` (portal admin OR device). ⚠ A device may only un-enrol **itself**, enforced against the token's `did`: otherwise one enrolled till could start the removal of every till in the estate, and approval is a portal queue where plausible-looking requests get waved through. Mutation-checked. |
| **A till can read its own takings** | ✅ | ✅ | `GET /api/v1/sales`, `GET /api/v1/cash-events` | Both gained `pos.reports.view` alongside their portal permissions (step 19). ⚠ `cash-events` **is** the X/Z drill an operator runs at the end of their own shift, and it was reachable only with `portal.financials.view`, which no till operator holds — the till could write cash events and never read them back. |
| **Store Information** (name, address, phone, VAT number) | ✅ | ✅ | `GET /api/v1/stores/{id}/info` — READ-ONLY; the portal is the source of truth (WP6.1) | **Cutover step 20, 2026-08-09.** MAUI's five local-write commands are DELETED. They edited the shop's name, address, logo, phone and VAT number into the LEGACY local database — so the company's own VAT number could differ on every till in the estate, and the one printed on a receipt was whichever machine printed it. Nobody finds that until an inspection. They were also unusable on a portal till three times over: gated on `IsAuthorised` (a legacy `AuthActions` table that does not exist there), through `EmployeeId` (null for every roster operator), falling back to `RequestAuthorisedUserInput` (which never terminates). ⚠ **Last-good when offline, "unavailable" when never fetched** — never the legacy local record, which is the thing this step exists to stop being authoritative. ⚠ A null answer does NOT clear the cache: a blip would otherwise leave the next receipt with no shop on it. ⚠ **The logo is dropped** (binding default 18) — no logo field on the contract, so a local one was only ever this machine's opinion. |
| App-update prompt | ✅ | ⬜ | — | Web polls a build stamp; MAUI needs the `426 Upgrade Required` path in WP5. |
| ⚠ **Remote lock of a lost or stolen till** | ⬜ | ⬜ | ⬜ **`Device.Locked` exists but is inert** | ⚠ **Reads as built and is not — traced 2026-08-09.** The column ships (WP5's `AddDeviceSyncSignals`), `HeartbeatResult` carries `Locked`/`LockReason`, and `SyncClient` surfaces `HeartbeatOutcome.Locked`. But **nothing sets the flag** (no endpoint, no portal control — `Locked =` appears nowhere outside the migration) and **nothing enforces it**: `IssueDeviceTokenAsync` refuses a device only when `Status == Revoked`, so even once set the lock is *advice to the till* and older or modified software ignores it. ⚠ **`Revoked` is the only thing that actually stops a device today** — reach for that in a real incident. Enforcement must land **before** any control that sets it, or the switch stays fake. Retrofit plan §7 risk 4. |
| **Portal-controlled theming** (colour schemes pushed to stores / tills / groups) | ✅ | ⬜ | `GET /api/v1/themes/effective` + `/api/v1/themes` CRUD | Shipped 2026-08-07. Built-in light/dark + custom schemes, assigned per tenant/store/group/till from Locations. MAUI: consume the same effective endpoint and map the slots onto XAML resources — **WP7b**, added 2026-08-08. ⚠ WP7 previously said "port the palette" only and would have closed with a hardcoded scheme. |
| Operator RBAC + offline login | ✅ | ✅ | `GET /api/v1/tills/{id}/operators` (**built 2026-08-08**) | **WP8.** A till syncs its roster — credential hashes + **raw** permission windows — and verifies offline with `SharedKernel.Pbkdf2`, byte-identical to the server. ⚠ **Windows ship RAW, never pre-evaluated**: resolving "allowed right now" at sync time would leave a Saturday-only supervisor synced on a Wednesday with **no permissions until the next sync**, silently. The staleness tier applies at the gate, so past 7 days the same person keeps selling and loses refunds. |
| ⚠ **Enforcing those permissions at the action** (sell, refund ceiling, price override) | ✅ | ✅ | — | **Cutover step 12, 2026-08-09** — `AppClient/Services/Security/TillGate.cs`, 16 tests. Delegates every decision to `SignedInOperator.Can`, so the ceiling, the window and the staleness tier are the shared rule. ⚠ It replaced a gate that **could not work**: the legacy `Authorisation.IsAuthorised` looked up action names in a local `AuthActions` table a portal-provisioned till does not have, and `RequestAuthorisedUserInput` **never assigned the id it returned** — so a supervisor typing correct credentials was re-prompted for ever and only Cancel escaped, while the caller re-tested the ORIGINAL operator anyway. ⚠ **A null operator BLOCKS** (`SignedInOperator` is null after a legacy local login) and is NOT offered an override — another person's password does not answer "nobody is signed in". ⚠ The refund ceiling now counts **quantity**: the legacy `Sum(bRI => bRI.Price)` summed UNIT prices, so five £30 returns tested as £30 and sailed through a £100 band. Both pinned by mutation. |

---

# Part C — Where the rules live

Part B answers *can this till do X*. Part C answers **would every till get X right** — which is a
different question, and the one that produces silent money bugs.

## C1. The rules register

For each cross-cutting rule: where the one implementation lives, who else implements it and why,
and what stops them drifting.

### Money and VAT

| Rule | The implementation | Second implementation | Pinned by |
|---|---|---|---|
| **Money is integer pence** | Everywhere; `SharedKernel/Money.cs` | — | `No_module_declares_decimal_or_double_money_members` |
| **A line's VAT figures** (rate from the price pair, VAT = gross − ex, discount scaled by ex/inc, returns negated with the discount dropped) | `SharedKernel/VatLineMath.cs` | Web till `api.ts` checkout — **deliberate**, it's TypeScript | `VatLineMathTests` (.NET side only — see C2) |
| **Which VAT rate applies** | The portal. `GET /api/v1/vat/bands` publishes the whole effective-dated timeline | None — **no till may hold a rate** | `Till_libraries_stay_platform_neutral_and_hold_no_VAT_rates_of_their_own` |
| **Which VAT band a line was sold under** | `LineMeta.vatBand` on the wire; backfilled server-side by `Plutus.Entities/VatBandStamp.cs` when a channel sends none | Client may state it (and then wins) | `VatBandsE2eTests` |
| **How much of a sale is still refundable** | `SharedKernel/RefundRules.cs` (2026-08-09) | None yet — ⚠ the server does **not** enforce the remainder when a refund is posted, so today this is a *client* gate only | `RefundRulesTests` (23, mutation-checked) |
| **Legacy tax row → band** | `SharedKernel` `VatBandResolution` + the portal's `VatBandTaxMap` | — | `VatExemptBandTests` |
| **Band snap tolerance** (25bp) | `VatAccounting.BandSnapToleranceBp` — one constant | — | `VatExemptBandTests` |
| **Output tax on a return** (VAT fraction × takings) | `SharedKernel/VatAccounting.cs` — **server only**, a till never computes a return | — | `VatAccountingTests` |
| **Was the rate legal at the time of sale** | `SharedKernel/VatRates.cs` `VatRateHistory.Assess` — server-side at ingest | — | `VatRateChangeE2eTests` |
| **Which HMRC rules the platform applies** | `SharedKernel/VatGuidance.cs` — served to the portal's VAT → Rules tab | — | Rendered from code, so it cannot drift from behaviour |
| **Caching the published bands on a till** | `Client.Core/VatBandCache.cs` — ⚠ stores the WHOLE effective-dated timeline, and prices at the SALE's instant | Web till `api.ts` band cache | `VatBandCacheTests` (10) — including a future-dated change applying on the day to a till that never reconnects |
| **Legacy tax row → band, on a till** | `VatBandCache.BandKeyForTaxIdAsync` — ⚠ **null on a TIE**, never first-wins | ⚠ Web till `api.ts:786` DOES take the first match — see C2 | `A_tax_row_claimed_by_TWO_bands_is_ambiguous_not_first_wins` |

### Identity and sale shape

| Rule | The implementation | Second implementation | Pinned by |
|---|---|---|---|
| **Item id from a barcode** | `SharedKernel/DeterministicGuid.cs` `ForItem(businessId, itemIdOne)` | Web till `pipeline.ts` `itemGuid()` — **deliberate** | Frozen golden vector in `LegacySaleBridgeTests` — see C2 |
| ⚠ **The barcode is the real invariant**, `ItemId` rides along | `SaleLine.ItemIdOne`, carried in `LineMeta.itemIdOne` | — | Retrofit plan §10 |
| **Entity ids are UUIDv7** | `SharedKernel/Uuid7.cs` | Web till `pipeline.ts` `uuidv7()` — **deliberate** | `No_module_mints_entity_ids_with_Guid_NewGuid` (.NET side) |
| **The four sale invariants** | `SaleV2.Validate()` — enforced at ingest, so a client cannot diverge undetected | — | `SalesV2Tests` |
| **Ingest status policy** — 201 recorded · 200 duplicate · 202 quarantined (never retry) · 400 skip | `Plutus.Client.Core` `OutboxPusher` | Web till `pipeline.ts` | `TillOutboxSoakE2eTests`, `ClientCoreE2eTests` |

### Connection and offline trust

| Rule | The implementation | Second implementation | Pinned by |
|---|---|---|---|
| **What "connected" means** — no network / no server / revoked / not enrolled / online, and the two-step order that tells them apart | `Plutus.Client.Core/Connectivity.cs` `ConnectivityProbe` | ⚠ Web till still on `navigator.onLine` — **WP16a moves it here** | `ConnectivityProbeTests` (14), `ConnectivityE2eTests` (5) |
| **Reachability is asked without a database** | `GET /api/v1/ping` — anonymous, no DB, so it answers during a MySQL blip and for an un-enrolled or revoked till | — | `ConnectivityE2eTests` |
| **Revocation reaches a till by POLLING, never by token expiry** | `GET /api/v1/tills/devices/{id}/status`. ⚠ Tokens are bearer tokens with **no server-side denylist** — a revoked till otherwise trades for up to 12h | — | `ConnectivityE2eTests` |
| ⚠ **Never probe by minting a device token** | `POST /api/v1/tokens/device` is rate-limited 5/min/IP; polling it makes a healthy till report itself revoked, and tills sharing one public IP do it to each other | — | `ConnectivityProbeTests` |
| **How long a cached login is trusted** — tiered: money-out 7d, selling 30d, warn from 3d, idle 15min, session ≤ min(12h, business-day rollover) | `SharedKernel/OfflineCredentials.cs` | — | `OfflineCredentialsTests` (23) |
| **Whether a permission is live right now** (validity dates, day mask, time window) | `SharedKernel/PermissionGrant.IsActiveAt` | The server's `EffectivePermissionsService.InWindow` now **delegates** to it — WP8 gave the same question to every till, and two implementations would be a permission meaning one thing centrally and another on a counter | `OperatorLoginTests`, `TillOperatorsE2eTests` |
| ⚠ **Permission windows travel RAW to a till, never pre-evaluated** | `GET /api/v1/tills/{id}/operators` ships the dates/mask/window; the till judges them against its own clock at the moment of the action | — | `A_Saturday_only_grant_is_refused_on_a_Sunday_and_allowed_on_the_Saturday` — resolving at sync time would strand a Saturday-only supervisor with no permissions all week |
| **Offline password verification** | `SharedKernel/Crypto.cs Pbkdf2` on both sides — the till verifies the same hash the server stores | — | `The_payload_carries_a_VERIFIABLE_hash_and_the_RAW_window` asserts the hash actually verifies with the till's KDF |
| **Splitting one discount across the lines it applies to** | `SharedKernel/DiscountApportionment.cs` — proportional, largest-remainder, ties to the earlier line | — (web till reshapes in WP15) | `DiscountApportionmentTests` (13) |
| **How much of a sale has ALREADY been given back** | `TillStore.AlreadyRefundedPenceAsync` over the `LocalRefunds` table — written in the SAME transaction as the refund, from that refund's own payload | The SERVER's `SaleAdjustments` rows, written by `SalesIngestService` (step 17) and summed by `SaleDto.AlreadyRefundedPence` — the till knows only what IT has seen, so the two are halves of one cap, not copies | `TillStoreReadPathTests` (13), mutation-checked twice: overwrite-instead-of-accumulate, and keeping the sign. ⚠ A **table**, not a column: one basket can refund lines from two different original sales, and a single origin column would drop one — leaving it refundable again in full. ⚠ Counts sales still QUEUED (the cash left the drawer when it was handed over) and SURVIVES pruning (deleting it would reset the running total to zero). |
| ⚠ **A REFUND CANNOT ITSELF BE REFUNDED** | The till never offers one: `TillStore.ListRecentSalesAsync(purchasesOnly: true)` excludes negative-gross sales | The SERVER refuses independently: `ValidateRefundCapAsync` quarantines any refund whose origin has `GrossPence < 0` | `Refunds_are_NOT_offered_as_sales_to_refund_against` + `A_refund_cannot_itself_be_refunded` (E2E), both mutation-checked. ⚠ **This cost £13.99 twice on a £13.99 sale, 2026-08-10.** A refund is stored as its own sale with a NEGATIVE gross, so it appeared in a recent-sales picker — newest first, exactly where an operator taps — and the second refund was recorded against the FIRST REFUND's id. The cap then compared it against nothing. ⚠ The server's half took `Math.Abs(origin.GrossPence)`, so a −£13.99 refund read as a £13.99 sale with nothing returned: **the cap was working perfectly on a premise nobody had checked.** |
| ⚠ **A refund still IN THE OUTBOX counts against the next one** | `TillStore.UndeliveredRefundedPenceAsync` sums refunds whose sale is not yet `Pushed`; `ReturnLookup` ADDS it to the server's figure | The two do not overlap — the server counts DELIVERED refunds from any till, this counts UNDELIVERED ones from this till | `A_refund_still_QUEUED_counts_against_the_next_one`, `A_refund_the_platform_has_TAKEN_is_no_longer_counted_locally`, `A_refund_the_platform_REFUSED_still_counts_against_the_next_one`, mutation-checked. ⚠ **The drain window is a real hole**: the two real double-refunds were **97ms apart** and drained together, so the platform answered "nothing refunded" both times — correctly. ⚠ **Added, never `max()`'d**: taking the larger would miss another till's refund landing while this one holds something queued. ⚠ A **REFUSED** refund still counts — the money left the drawer whatever the platform later decided. |
| ⚠ **The refund cap itself** — never give back more than was paid (binding default 12) | `SharedKernel.RefundRules.Authorise`, run at BOTH gates | **The till** caps per ORIGIN SALE (step 16); **the server** re-runs the same rule per sale AND **per item** (step 17) and quarantines 202 past it | `ReturnLookupTests` + `SaleAssemblerE2eTests` (5 E2E), mutation-checked. ⚠ **Per-item is the half only the server can do**: a till's local history is per origin sale, so refunding a £10 line twice inside a £210 sale passes a sale-level test comfortably — and that is the shape refund fraud actually takes. ⚠ **Recording the refund was missing entirely** until step 17: only the WEBSTORE wrote `SaleAdjustments`, so every "how much has been refunded" question read a table till refunds never populated and always answered "none". ⚠ An origin the platform has NOT seen is **accepted, not quarantined** — 202 is terminal and a refund can legitimately outrun the sale it refunds when another till's outbox drains later. |
| **Card-surcharge money** — the fee (percent bp + flat) AND its VAT (follows the basket, one blended ratio, multiply-before-divide, round once away from zero) | `SharedKernel/CardSurchargeVat.cs` — `FeePence` + `PairFor`; the `CARD-SURCHARGE` natural key is the shared constant both the provisioner and the tills use | — (web till takes it with WP15) | `CardSurchargeVatTests` (17), mutation-checked against a hardcoded 20% — the plausible shortcut that reconciles perfectly and is wrong on every non-standard-rated basket. ⚠ `PairFor` found its own trap: `fee × (ex ÷ gross)` turns the exact midpoint 3 × 10000 ÷ 12000 = 2.5 into 2.4999… — products first, always. |
| ⚠ **A basket whose lines don't add up to its total is REFUSED at the till, not sent** | `CheckoutCommit.CommitAsync` compares `request.GrossPence` against `BasketMoneyPence` — the same sum the till's own `sale.Total` uses — before anything is queued | — | `CheckoutCommitTests`. ⚠ The alternative is not "a slightly wrong sale": the server answers **202 Quarantined** and `OutboxPusher` treats 202 as **terminal**, so the sale looks successful at the counter and is destroyed hours later with the customer long gone. This guard is what makes any future money-carrying basket record fail **loudly and locally** instead. |
| ⚠ **A till receives only its OWN tenant's operators** | `TillOperatorsController` joins through `Employee` (tenant-filtered). `WebCredentials` is a GLOBAL table with no query filter — login is by email before a tenant is known | — | `A_till_NEVER_receives_another_tenants_operators_or_their_hashes` |
| **Catalogue freshness — what a till has and when** | `GET /api/v1/catalogue/changes`, keyset cursor over `(ModifiedAt, IdOne)`; client loop in `Client.Core/SyncClient.cs` | — | `CatalogueSyncE2eTests` (9), `SyncClientTests` (13), `CatalogueCursorTests` |
| ⚠ **A withdrawn item reaches a till as a TOMBSTONE, never as an absence** | `CatalogueItemDto.Removed`, set from `Item.BinnedAtUtc` | — | `A_binned_item_arrives_as_a_TOMBSTONE_not_as_an_absence` — without it a binned item stays sellable on every offline till indefinitely |
| **The change cursor is stamped where it cannot be forgotten** | `Item.ModifiedAt`, stamped for every `IAuditable` in `RepositoryContext.SaveMethods()` — a single choke point every write already passes. ⚠ The plan originally specified a counter each write path bumps; a path added later that forgot would leave every till silently stale | — | `Paging_a_bulk_edit_loses_nothing_even_when_every_row_shares_a_timestamp` |
| **Till presence** — ONLINE <2min · STALE 2–5 · OFFLINE >5 | `Plutus.Tenancy/TillPresence.cs`, in-process. ⚠ NOT MySQL: a fleet beating every 60s would be the busiest write path in the system, storing data that expires in five minutes | — | `TillPresenceTests` (6) |
| ⚠ **A failing heartbeat is SILENT** | `SyncClient.BeatAsync` swallows everything. If a 500 ever read as "locked", one backend hiccup would stop every till in the estate selling | — | `A_failing_heartbeat_is_SILENT_and_never_locks_the_till` |
| **Which TRADING DAY a till is on** | `SharedKernel/BusinessDay.cs` — local wall-clock, not UTC. `CheckoutCommit` and the cash screen both call it | The web till's `pipeline.ts businessDay()` — **C2 twin** | ⚠ Not yet pinned by a test. **Why it is a rule and not an expression:** the drawer reconciles against the day's TAKINGS, so if a cash event and a sale disagree by one day about "today", the Z balances against the wrong takings and the variance is fiction — with nothing in the numbers to show the dates were the problem. ⚠ UTC would file a till trading past midnight under tomorrow and split one shift's banking across two days. ⚠ A rollover hour ("the day starts at 04:00") belongs HERE when it comes, not in each caller |
| **The cash vocabulary** — the five event names | `Contracts.Client/CashContracts.cs` `CashEventTypes`, with `NeedsCount` / `NeedsReason` | The server's `CashEventType` enum in `Plutus.Entities` — **C2 twin by construction**, since a till may not reference a backend project | `LocalCashEventTests.The_wire_vocabulary_is_exactly_the_five_the_server_parses` |
| **The print-op wire contract** — what `POST /print` binds | `tools/Plutus.TillAgent.Core/PrintOps.cs`, referenced (not copied) by `Plutus.Client.Core` | The web till hard-codes the NUMBERS in `receiptDoc.ts` (`OP_TEXT = 0`) — **C2 twin**, since a browser cannot reference a C# project | `TillAgentClientTests.Op_kinds_go_on_the_wire_as_NUMBERS`, and `ConventionTests.The_print_wire_contract_stays_a_pure_contract` — which is what pays for Client.Core being allowed to reference a project outside `src/` |
| **What a receipt SAYS** — the document layout | `Client.Core/ReceiptDocument.cs` `ReceiptDocumentBuilder` | The web till's `till/receiptDoc.ts` — **C2 twin, unavoidable**: the browser cannot run the C# and MAUI cannot run the TypeScript | `ReceiptDocumentTests` (11). ⚠ Two shops printing two different receipts for the same basket is the failure this describes, and it is invisible until someone compares paper |
| **A reprinted receipt is marked as a copy** | `ReceiptDocument.cs` `ReceiptDocInput.IsReprint` → "** REPRINT — not a new sale **", above the first rule | ⬜ — the web till has no reprint yet (WP11); ⚠ **when it gains one, this rule comes with it** | `ReceiptReprintMarkingTests` (5), mutation-checked by moving the marker into the footer and by changing the copy's barcode |
| **What a VAT band says to an operator** | `Client.Core/TaxBandLabel.cs` — `(rate − 1) × 100`, because `Rate` is a MULTIPLIER | The web till shows the band NAME only (`InventoryPage.tsx`), so no twin yet — ⚠ it should adopt this, since a name alone cannot be checked against a price | `TaxBandLabelTests` (7), mutation-checked four ways. ⚠ Zero-rated and Exempt both read 0% and stay distinguishable by NAME — pinned, because merging them on screen is the first step to merging them on a VAT return |
| ⚠ **A failed list is NOT an empty list** | `Client.Core/PlutusApiClient.GetLegacyAsync` returns `(Value, Problem)` — the status is named, never flattened to null | — | `LegacyListFailureTests` (12), mutation-checked four ways. ⚠ **This is the rule Matt's *"missing category and tax"* report was really about**: the client returned an empty list for a 403, a 500, an unreachable server and an unparseable body alike, so the item editor skipped its prompts and a NETWORK fault presented as a fact about the shop. "There are none" is about the tenant; "I could not ask" is about the till, and only one of them is somebody's job to fix. ⚠ A 500 here means the legacy base controller's CONSTRUCTOR `.First()`-ing the `objectidentifier` claim — so it is reported as *"nobody is signed in"*, which is the actionable reading |
| ⚠ **The item editor's ex-price comes from the LEGACY TAX ROW, never the cached VAT band** | `ViewAllViewModel.ExecuteOpenEditItem` divides by `TaxBandDto.Rate` from `/api/Tax/Index` | The till DOES hold a full band cache locally (`VatBandCache`, refreshed every 60s) and it is **deliberately not used here** | ⚠ **Not pinned — held by this row and the code comment.** The server validates the saved pair against the specific legacy `Tax` row's rate (`ItemController`: `|price − exPrice × rate| ≤ 2p`), while band↔tax-row matching runs on a **25bp tolerance** (`VatAccounting.BandFor`). So a band and its tax row may legitimately differ, and an ex-price derived from the cached band would be rejected with a message about ex-prices that never mentions where the rate came from. ⚠ The consequence is that item editing needs the network — recorded here rather than "fixed" by an offline path that would write wrong money |
| ⚠ **A Z-close ENDS the business day** — nothing may be recorded against it afterwards | `TillStore.RecordCashEventAsync` refuses once `IsDayClosedAsync`; the server 409s independently | Two gates, one rule — the local one is the only one available offline | `Nothing_can_be_recorded_against_a_day_that_has_been_Z_closed`, mutation-checked |
| **Which build this is** | `versions/<component>.txt` → `Directory.Build.targets` → `SharedKernel/PlutusVersion.cs`. ⚠ **`.targets`, not `.props`** — props is imported before the project body, so a per-project `<PlutusVersionFile>` would be ignored and every component would silently report the platform version | Each `vite.config.ts` reads **its own** file into `__APP_VERSION__` | `ComponentVersionTests` — each file exists and parses X.Y.Z, each project points at its own, no surface holds a literal, and the wiring has not moved back to `.props` |
| **Every HTTP endpoint is gated unless deliberately listed** | `[Authorize]` per action or per controller; ⚠ there is **no global fallback policy**, so a missing attribute is an open door | — | `AnonymousEndpointTests` — a reviewed allow-list of anonymous entry points, mutation-checked |
| **Which permissions survive staleness** | `OfflineCredentials.SellFloor` — an **allow-list**, so a permission added later is withdrawn until someone says otherwise | — | `OfflineCredentialsTests` |
| **Supervisor override** — a second operator authorises ONE action | `Client.Core/OperatorLogin.AuthoriseOverrideAsync` | — | `OperatorLoginTests`. ⚠ It may never become an escalation path: **self-authorisation refused**, the supervisor's **own ceiling applies**, and it does **not bypass staleness** — a roster too old to be trusted with refunds is too old to authorise one, or every staleness restriction has a trivial workaround. The record names **both** people, because "a refund was authorised" is worthless and naming only the supervisor re-attributes the sale. |
| **Offline password verification is byte-identical to the server's** | `SharedKernel/Crypto.cs` `Pbkdf2` — PBKDF2-HMAC-**SHA1**, 101,010 iterations, 64-byte output | MAUI's legacy `Helpers/Security/Password.cs` — **retired at WP8** | ⚠ Nothing pins the two against each other yet — see C2 |

### Screens that must never trap the operator

A till has no back button and no task switcher — it is a full-screen app on a counter with a queue
in front of it. A screen that stops responding is not a bug report, it is a shop that cannot trade,
and the two entries below are the two ways it has actually happened here. Both were reported as
*"it's just hung"*, on screens that were not at fault.

| Rule | The implementation | Second implementation | Pinned by |
|---|---|---|---|
| ⚠ **The loading overlay is driven by the DIFFERENCE between wanted and actual, one awaited operation at a time** | `AppClient/Services/Loading/OverlayGate.cs`; `LoadingViewService` is now only the MAUI half (push, pop, marshal to the UI thread) | — (the web till has no modal overlay) | `OverlayGateTests` (5), mutation-checked by collapsing the reconcile loop to a single `if` — caught by `Hide_requested_while_the_show_is_still_in_flight_still_ends_hidden` and `The_overlay_still_works_on_the_next_screen_after_that_race` |
| ⚠ **Every modal dialog has a way out — a Cancel button, the background, or Escape** | `CustomViews/AlertDialogBase.cs` — `OnBackButtonPressed` now CANCELS rather than swallowing; `OnBackgroundClicked` completes the task when interruptible. The cash-payment dialog passes a real `cancelText` | — | ⚠ **Nothing. Held by review** — a caller that omits `cancelText` on a non-interruptible dialog rebuilds the trap |
| ⚠ **A cancelled prompt UNWINDS the operation — it never falls through** | `Client.Core/TenderLoop.cs` — `TenderOutcome.Abandoned` carries an EMPTY payment list by construction, so a caller cannot mistake it for a partial success | — | ✅ **Pinned 2026-08-10 (step 11b)** — `TenderLoopTests` (19), mutation-checked three ways. `Backing_out_of_the_AMOUNT_takes_nothing_and_abandons`, `Backing_out_of_the_METHOD_…`, `Abandoning_midway_through_a_split_payment_takes_nothing` |
| **Taking payment — how a basket is settled** | `Client.Core/TenderLoop.cs`. Written in terms of what is still OUTSTANDING, so a refund is the same algorithm with the sign flipped rather than a second code path | — (the web till has its own in TypeScript — **new C2 twin**) | `TenderLoopTests` (19). Mutation-checked: accepting a zero/wrong-way tender, charging the surcharge per-tender, and letting a card give change each fail named tests |
| ⚠ **A tender that cannot settle anything is REFUSED, not accepted** | `TenderLoop` — zero, and any amount pointing away from the outstanding balance | — | `A_zero_tender_is_refused_and_the_operator_is_asked_again`, `A_tender_pointing_the_wrong_way_is_refused`. ⚠ Both were **infinite loops**: `paid += 0` never moves `paid` toward the total, and `paid > total` cannot express "wrong way" for a refund, where both numbers are negative |
| ⚠ **A card surcharge is charged ONCE PER SALE, not once per tender** | `TenderLoop` applies `TenderChoice.SurchargePence` only while none has been applied | — | `The_surcharge_is_applied_ONCE_across_a_split_card_payment`. ⚠ It used to be the CALLER's job (re-checking the basket each pass); a split card payment charged the flat fee twice if the caller forgot |
| ⚠ **A popup is pushed once, awaited once, and popped in a `finally`** | `InputAlertHelper.ShowAsync`, `SliderAlertHelper`, `InputMultiSelectAlertHelper` | — | ⚠ Not pinned — needs `MopupService`, which needs a UI host |
| ⚠ **A screen may not hold the shared store gate without a deadline** | `TillStoreAccess.UseAsync` serialises every caller behind ONE semaphore. Callers pass a `CancellationTokenSource` deadline — `TillCadence`'s beat uses 30s, `ViewAllViewModel`'s browse 20s | — | ⚠ **Nothing. This is a convention held by review, and the next caller written without a deadline restores the fault in full.** Pinning it means an architecture test over `UseAsync` call sites requiring a token that is not `default` — worth doing when a third caller appears |

⚠ **Why the overlay rule is worth a class of its own.** The straight-line version — flip a `bool`,
then `await PushModalAsync` — strands the overlay **permanently** the first time a screen finishes
loading before the push completes, which a local SQLite read does routinely. The hide sees the flag
already set, clears it and pops a modal stack the overlay has not landed on yet; the push then lands
with the flag reading "hidden", so every later hide returns at its first line. The overlay covers
the app **for the rest of the process**, on every screen after it, and nothing is logged because
nothing threw. Fail open, always: if the gate cannot tell what is on screen it assumes the overlay
is DOWN, because a missing spinner is recoverable and a locked screen is not.

⚠ **The cash-payment dialog had no exit at all, and that is the shape to watch for.** It was raised
with no `cancelText` (so no Cancel button was built), `interuptable: false` (so clicking the scrim
did nothing) and an `OnBackButtonPressed` that returned `true` (so Escape was swallowed). Three
independent decisions, each defensible alone, combining into a screen an operator could only leave
by killing the process — mid-sale, with a customer at the counter. Worse, the two helpers behind it
looped `while (result.Count == 0) { push; await; pop; }` over a `TaskCompletionSource` created ONCE,
so the moment cancelling became possible it would have span the UI thread for ever. **Check the
combination, not the pieces.**

⚠ **Never raise the global overlay across a navigation.** It is a MODAL page, so
`App.SetLoading(true)` immediately before or during a `PushAsync` issues a modal push and a
navigation push against one window at once. MAUI does not serialise the two stacks and the WinUI
handler resolves the collision into a corrupted layout — the Inventory list drew over the tab bar,
at the wrong size, with no way back. ⚠ It was also a CROSS-SCREEN contract: the opening screen
raised the overlay and expected the opened screen to lower it, so a screen that failed to load left
the overlay over the whole app. **A screen owns its own spinner, or has none.**

⚠ **Why the store-gate rule is here rather than in "Connection".** One semaphore for the whole store
is the right call (EF contexts are not thread-safe, and two overlapping outbox writes is a lost or
duplicated sale) — but it means a wedged *screen* silences the *heartbeat*, and the till vanishes
from the fleet list while still selling perfectly well. The symptom appears on the platform, not on
the till, so it gets diagnosed as a network or enrolment problem. Anything on that gate needs a
deadline, and the heartbeat needs one most.

### Portal decides, till obeys

Each follows the same shape: the portal owns it, the till caches it on the sync cadence and renders
it. **A till that computes one of these locally is a bug.**

| Rule | Contract | Notes |
|---|---|---|
| Receipt layout | `GET /api/v1/stores/{id}/receipt-template` | The house exemplar for this pattern. ⚠ Receipts are deliberately immune to theming. |
| Colour scheme | `GET /api/v1/themes/effective` | Resolves till > group > store > tenant > default server-side. |
| VAT bands | `GET /api/v1/vat/bands` | See above. |
| Store details | `GET /api/v1/stores/{id}/info` | ⚠ Carries the **legacy `businessId`**, which is not the tenant id. |
| Prices | `GET /api/v1/prices/effective`, and the **timeline** on `GET /api/v1/catalogue/changes` | Resolution is `SharedKernel/PriceResolution.cs` — **store override → central list → legacy baseline** — and the server's `PricingService` delegates to it. ⚠ A price write must call `PricingService.TouchItemForSyncAsync`, or it never reaches a till: the feed pages by `Item.ModifiedAt` and prices live in their own tables. |
| Permissions | Token carries the user's full effective set; `perm:*` resolves from RBAC by userId | ⚠ `"perm:x"` and `PlutusPolicies.X` are different namespaces — a typo between them fails closed and silently. |
| Gift-card VAT treatment | `GiftCardSettings` — single- vs multi-purpose | Locks at the first card sale. Absence 409s. |
| **Which announcements reach a till** | `GET /api/v1/announcements/active` → `Client.Core.NoticesClient.ShowsOnATill` | ⚠ The server ships **all three severities** and applies only the time window and tenant targeting — the *till-vs-portal* split is a CLIENT rule, so it has exactly one home. **`Info` is portal-only**: the banner interrupts someone mid-transaction, and filling it with release notes trains operators to ignore the incident too. ⚠ An **unrecognised severity SHOWS** — a later backend's new severity will be at least as urgent as maintenance, and defaulting to "hide" would blank exactly the messages worth reading, silently, until someone shipped a build. |
| **Which card flow the operator runs** | `GET /api/v1/payments/gateway/active` → `Client.Core.PaymentGateway.Resolve` | ⚠ Decided by **`integrated`**, never by the provider name — every provider is `integrated: false` today, so a till reading the name would hang waiting for a terminal. A chosen-but-unwired provider is the manual flow *with the provider named*; no answer is the manual flow *with nothing pending*. The `"standalone"` key comes from `SharedKernel.PaymentProviderCatalogue`, which the server compares against too, so a rename can't leave the two sides disagreeing. |
| **Which pick notes reach a till** | `GET /api/v1/notifications` → `Client.Core.NoticesClient.IsForStore` | ⚠ The server does **not** filter by store — it returns the tenant's 50 most recent, so the addressing is the client's job. `StoreId` null = every store's tills (fulfilment undecided: everyone, rather than nobody); non-null = only that store's. ⚠ A till that doesn't yet know its own store sees everything — showing a note that isn't its problem costs a walk, hiding one that is costs an oversell. ⚠ **The web till does neither** — WP17.4. |
| **A till's very existence** | Portal → Locations → Tills → enrolment code → `POST /api/v1/tills/enrol` | ⚠ **The portal is where a till is BORN, not just what it obeys** (Matt, 2026-08-08). Company, store, till and code are created centrally; the app claims that identity and holds no opinion about who it is. |

> ### ⚠ A till may not invent itself — and the legacy MAUI "Setup" screen does exactly that
>
> `Views/FirstTimeStartUp/SetupView` builds a **standalone** till: a locally-invented store and
> admin employee in a local SQLite file, with **no tenant, no till record and no device credential**.
> Its "Cloud" option was never implemented (`DatabaseProvider.Cloud` throws), so the only path that
> completes is the local one.
>
> **It still works, and that is precisely the danger.** It produces a till that looks fully
> configured, lets someone sign in, and can never post a sale to the platform — there is nothing to
> post it *as*. Nothing on that screen tells the operator so.
>
> Retitled `(Legacy)` and demoted below Connect-to-Plutus on 2026-08-08; **deletion is the intent**,
> once WP8 lets a portal-synced operator sign in. Third-party transfer goes the same way for the
> same reason — importing someone else's data is a central concern that already belongs to the
> NatApp translation agent, not to one device's local database.

## C2. The drift register — where the same rule exists twice

**This is the section that decays invisibly, and the one to read before writing anything that
computes money on a client.**

The web till is TypeScript and everything else is .NET, so some rules genuinely exist twice. That is
a deliberate cost. What matters is being honest about which twins are tested and which are trusted.

| Twin | Status | The honest position |
|---|---|---|
| **VAT line arithmetic** — `VatLineMath` ↔ `api.ts` | 🟡 **half-pinned** | `VatLineMathTests` fixes the .NET side to the numbers the web till produces, including the JS-vs-.NET midpoint-rounding trap. Nothing executes the **TypeScript** against those numbers, so a change to `api.ts` would not fail a test. |
| **Item id derivation** — `DeterministicGuid.ForItem` ↔ `pipeline.ts itemGuid` | 🟡 **frozen vector** | `LegacySaleBridgeTests` asserts a hardcoded GUID the TS produced in a 2026-07-24 smoke test. It will catch .NET drift. It will **not** catch TS drift. Getting this wrong corrupts item ids silently — stock still moves, because lines key on the barcode. |
| **Basket totals / ex-VAT apportionment** — `VatLineMath` ↔ `till/basket.ts basketTotals` | ⚠ **third copy, unpinned — on the WEB till only** | `basketTotals` re-implements the same discount apportionment for the on-screen total. It must agree with the checkout payload or the screen and the receipt disagree, and nothing enforces it. ✅ **MAUI does not have this problem** (2026-08-09): `SaleAssembler.Total` and `.Assemble` run the SAME `VatLineMath.ForLine` calls, so its display total is a projection of the payload rather than a second calculation. The web till's copy remains — WP15/WP17. |
| **Basket → `IngestSaleRequest`** — `Client.Core.SaleAssembler` ↔ `api.ts` (~1062) | 🟡 **twinned, .NET side pinned END-TO-END** (2026-08-09) | Deliberate twins: TypeScript and .NET. The .NET half is pinned by 19 unit tests **and** `SaleAssemblerE2eTests`, which posts an assembled mixed-rate basket to the real `/api/v1/sales` and requires **201 Recorded, not 202 quarantined** — the first client-assembled payload the endpoint has ever accepted. ⚠ Three rules are load-bearing and each has a named test: the header is the SUM of its lines (the server's reconcile invariant rejects anything else), `LineMeta.itemIdOne` is refused when missing (the server would accept the sale and silently skip its stock movement), and the item id is RE-DERIVED via `DeterministicGuid.ForItem` rather than trusted. ⚠ The TypeScript half is still unexecuted by any test. |
| **Manual line discount** — `SharedKernel.LineDiscounts` ↔ `till/basket.ts lineDiscountPence` | 🟡 **half-pinned** (2026-08-09) | Fixed-per-unit × qty, or a FRACTION of the whole line rounded once. ⚠ `Percentage` **throws above 1.0** rather than obeying: the legacy MAUI till computed `item.Price * decimal.Parse(operatorInput)`, so entering `10` for "10%" multiplied the price BY TEN and the basket charged it. ⚠ Returns take no discount on either till — a refund returns what was actually paid. |
| **Basket-level discount → per-line discounts** — `SharedKernel.DiscountApportionment` ↔ `till/basket.ts` | ⚠ **one copy today — MAUI only** (2026-08-09) | ⚠ **This closed a P0 shipped by cutover step 11.** A MAUI discount is a separate `BasketAlteration` record holding a NEGATIVE price; `ExecuteAlterTransaction` never writes it back onto `BasketItem.Price`. Step 11's `LinesFrom` skipped alterations *under a comment asserting the money was already in the line price* — it was not. The till's `sale.Total` sums every record, so the tenders settled against the discounted figure while `GrossPence` was built from undiscounted lines, and the server's `Σ tender − Σ change == GrossPence` invariant would have answered **202 Quarantined** on every discounted sale — which `OutboxPusher` treats as terminal and never retries. Nothing was lost only because the outbox does not drain until step 13. ⚠ The platform model has nowhere else to put it: no basket-level discount field, and `GrossPence` must equal Σ line gross, so "£5 off these three" MUST be apportioned before it can be sent. ⚠ Largest-remainder, because 3 × 333.33p loses a penny and a lost penny fails the invariant. ⚠ Ties break on the **earlier line** so two tills put the odd penny in the same place. Pinned by 13 tests + `CheckoutCommitTests`, mutation-checked against the step 11 behaviour. The web till reshapes its basket in WP15; when it grows a basket-level discount this becomes a twin. |
| **Tender name → wire byte** — `SharedKernel.Tenders` ↔ `Plutus.Entities.Models.TenderType` ↔ web till `api.ts tenderTypeFor` | 🟡 **three copies, all pinned** (2026-08-09) | The bytes sit on a byte column and every payment-split report groups by them, so a renumber does not break a build — it silently re-labels history. ⚠ The enums could NOT simply move to SharedKernel: 69 backend files import both namespaces (CS0104), and renaming the backend's copy would move the EF model snapshot. So SharedKernel holds **constants** and `TenderTypeParityTests` pins all three — including reading `tenderTypeFor` out of `api.ts` and asserting **gift is tested BEFORE credit**, because "Gift card" matches neither cash nor online and a naive chain files it as store credit: different liability, different reconciliation. ⚠ The TypeScript pin is a TEXT assertion, which is coarse — but the alternative is no pin at all until WP15 lands a JS runner. |
| **Effective price PAIR** — `TillStore.EffectivePricePairAsync` ↔ web till's price resolution | 🟡 **half-pinned** (2026-08-09) | The line's declared rate is derived FROM the pair (C1 rule 2), so both halves must come from the SAME published point — pairing an inc price from the timeline with an ex derived from the snapped baseline rate produces a rate nobody set. Pinned on the .NET side by `TillStoreTests`; the web till's resolution is not executed by any test. ⚠ Recorded here because the wobble is **price-dependent**: 1990bp at £5.00 against the 1993–2004bp the docs quote for £10–£20. Any future range-check on a declared rate must scale with the line. |
| **Item search matching** | ✅ **fixed 2026-08-08 — one implementation now** | Was THREE copies: the server's `ItemParameters.Tokenise`, the web till's `offline.ts searchTokens`, and a fourth about to be written for MAUI. Both existing copies carried a *"keep in sync"* comment, which admits the problem rather than fixing it. Now `SharedKernel/ItemSearch.cs`, and **the server's `Tokenise` delegates to it** — a copy removed, not added. Pinned by `ItemSearchTests` (15) including the `batman one` → *Batman Year One* vector. ⚠ Word-vs-phrase mode remains a per-device **preference** and the two are *supposed* to differ; what must not differ is two tills with the same setting. ⚠ The TypeScript copy is still unexecuted by any test — see the root-cause note below. |
| **Legacy tax row → VAT band** — `VatBandCache` ↔ `api.ts vatBandForTaxId` | ✅ **converged 2026-08-09 — the web till followed** | Both now require **exactly one** claiming band and return **null → unclassified** otherwise. Previously the web till returned the **first match**, so when two bands claimed one tax row the answer was decided by serialisation order. That is the dangerous case, not a benign one: zero and exempt are **both 0%**, so first-match silently moves exempt takings into zero-rated (or the reverse) while every total still balances, and nothing downstream can detect it. ⚠ Its doc comment had described the strict behaviour for some time while the code did the loose thing — worth remembering that a comment is not a pin. ⚠ Still unexecuted by any test on the TS side (see the root cause below); the two are now identical **by review**, not by CI. |
| **Taking payment** — `Client.Core.TenderLoop` ↔ the web till's checkout | ⚠ **NEW TWIN, .NET side pinned** (2026-08-10) | Extracting the tender sequence out of `TillViewModel` created a second home for a rule the web till already implements in TypeScript. Deliberate, and the same trade as every other row here: the .NET half is now 19 tests and three mutation checks; the TypeScript half is **unexecuted by anything**. ⚠ The rules most worth diffing by hand until WP15 lands a JS runner: **zero and wrong-way tenders are refused** (both were non-terminating loops on MAUI — assume the web till has at least one of them), **the surcharge is once per SALE**, and **change is only given by a method that can give it**. ⚠ Refunds are the same algorithm with the sign flipped, NOT a second branch — that is what let over-refunding past MAUI's old `paid > total` guard. |
| **The cash event vocabulary** — `Contracts.Client.CashEventTypes` ↔ the server's `CashEventType` enum | 🟡 **twinned by construction, .NET side pinned** (2026-08-10) | The five names exist twice because they must: the enum lives in `Plutus.Entities`, a backend project a till may not reference, and the contracts project has no references *because it ships onto tills*. ⚠ **Better than the TenderType twin in one specific way**: these travel as NAMES, so a rename fails loudly as a 400 rather than silently re-labelling history the way a renumbered byte would. ⚠ **The failure mode is still nasty** — a till that cannot post a cash event cannot bank the day, and it finds out at closing time. Pinned on the .NET side by `LocalCashEventTests`; the server's parse is `Enum.TryParse`, so adding a sixth name server-FIRST is the only safe order. |
| **The trading day** — `SharedKernel.BusinessDay` ↔ the web till's `pipeline.ts businessDay()` | ⚠ **twinned, NEITHER side pinned against the other** (2026-08-10) | Both return the LOCAL wall-clock Y-M-D, and they must: a sale and a cash event that disagree by one day make the Z-read balance against the wrong takings, and the variance is fiction with nothing in the numbers to explain it. ⚠ The day this grows a rollover hour it must grow one on BOTH tills in the same commit, or two tills in one shop will bank different days. |
| **Money representation** | ⚠ **divergent today** | Web is integer pence throughout; MAUI is `decimal` across 11 model files. WP2. |
| **UUIDv7** — `Uuid7` ↔ `pipeline.ts uuidv7` | ➖ **low stakes** | Only has to be a valid, time-sortable v7. Divergence costs ordering, not money. |
| **Business day** — the wire value ↔ `pipeline.ts businessDay` | ➖ | A till's day is deliberately wall-clock, not UTC. The server takes what it is given. |
| **Wire records** — `Plutus.Contracts.Client` ↔ the server's own copies | 🟡 **twinned by convention** | `EnrolResult`, `DeviceTokenResult`, `StoreInfoResult`, `PingResult` and now `HeartbeatRequest/Result` + `CatalogueItemDto/ChangesResult` are declared on BOTH sides. Deliberate: the contracts project has no references and no packages *because it ships onto tills*, so no backend module references it. The E2E tests deserialise the server's JSON into the CLIENT record, which is what actually pins the two shapes together — a field renamed on one side fails `CatalogueSyncE2eTests` or `ClientCoreE2eTests`. |
| **Password hashing** — `SharedKernel/Crypto.cs Pbkdf2` ↔ MAUI `Helpers/Security/Password.cs` ↔ `Identity/TestTokenAuth.VerifyPassword` | ⚠ **three copies, none pinned against each other** | All three are PBKDF2-HMAC-**SHA1** / 101,010 / 64-byte, and they agree today only because each was written from the same legacy constants. `Rfc2898DeriveBytes(string, byte[])`'s SHA-1 default is *implicit* in two of them — a "modernising" edit to SHA-256 in any one would silently stop every existing password verifying on that surface alone. `AuthController.SetPassword` already writes a **64-byte** salt where `Pbkdf2.SaltBytes` is 32 (harmless, stored alongside — but it shows the drift is real). MAUI's copy retires at WP8; until then, **one shared vector test would close this**. |
| **Connectivity states** — `ConnectivityProbe` ↔ web till `navigator.onLine` | ⚠ **divergent today, and the web till is the wrong one** | `navigator.onLine` reports the network interface. It says "online" in a shop whose broadband is down, and "offline" for a till the portal revoked — the two cases operators most need told apart. WP16a moves the web till onto the shared probe; until it does, the two tills give different answers to "am I connected?". |

### ⚠ The root cause, stated plainly

**The web till has no test suite.** `package.json` has `dev`, `build`, `preview` and `typecheck` —
no test runner, no test files. So every "pinning" test above holds only the .NET half of its twin.
TypeScript changes are held by `tsc --noEmit` (types) and by review (behaviour).

### What the server catches regardless — and what it does not

The real safety net is that ingest refuses malformed sales, so a divergent client is usually caught
at the door rather than in a return three months later.

**`SaleV2.Validate()` rejects a sale unless:**
1. it has at least one line;
2. every line's `LineGrossPence == unit × qty − discount`;
3. `GrossPence == Σ line gross`;
4. `VatPence == Σ line VAT`;
5. net tender (`Σ amount − Σ change`) `== GrossPence`.

**`VatRateHistory.Assess` additionally** judges each line's *price pair* against the bands in force
at the moment of sale, and quarantines a till trading on a superseded rate.

⚠ **The gap those leave.** Validation checks that the header agrees with the lines — it does **not**
check that a line's `vatAmountPence` is the *right* figure for its price pair. A till that computed
VAT by rate arithmetic instead of `gross − ex` would be internally consistent, pass all five
invariants, pass the band check, and be wrong by a penny on about a third of standard-rated lines.
**That is the precise shape of the bug shared code prevents and validation does not.**

---

# Part D — Working with this

## D1. Everything now has a work package — closed 2026-08-08

**This section used to list nine items as "not yet in any work package". All nine now have a WP body
and a Definition of Done**, following Matt's parity instruction (see the binding rule).

The audit that closed it is worth remembering, because the failure was subtler than a missing plan.
Six of the nine had been *ruled in* by the retrofit plan's §3a table months ago — and the register
still said "not in any WP" because **nobody had noticed the ruling and the specification were
different documents**. The other three had a ruling and no body: real gaps hiding behind a tick.

| # | Item | Was | Now |
|---|---|---|---|
| 1 | Gift cards | ruled in, fully specified — **register was stale** | WP13 |
| 2 | Portal-controlled receipt templates | ruled in, fully specified — **register was stale** | WP3 |
| 3 | Users / employee management | ruled in, specified; **DoD missed list + set-password** | WP8 |
| 4 | Announcements · Help/support tickets | ruled in, fully specified — **register was stale** | WP5b |
| 5 | Refund-only baskets | ruled in, fully specified — **register was stale** | WP3 |
| 5 | Add-unknown-item | ⚠ ruled into WP10 and **never written into its body** | WP10 |
| 6 | The Bin + untracked stock | ⚠ ruled into WP10 and **never written into its body** | WP10 |
| 7 | Pick-from-floor notifications | ruled in, fully specified — **register was stale** | WP5b |
| 8 | Theming (pushed, not just the palette) | ⚠ WP7 said "port the palette"; **would have closed without it** | WP7b |
| 9 | Web-till test runner + `basketTotals` pin | ⚠ never ruled either way | **WP15** |

**The same audit found seven more capabilities with no WP body at all** — they were not on the D1
list because D1 was built from B-section Notes, and these had Notes that *named* a WP. Homed now:
MAUI's VAT-bands consumption → **WP5** (the most dangerous: the contract shipped, the client helpers
were built, and no WP told anyone to call them), payment-gateway awareness → **WP14** (its Notes
cited "WP17.2", which is not a work package in this plan), member-number scan-to-attach → **WP12**,
and the shared item-search matcher → **WP5** (it was attributed to WP1, which shipped without it).

**Four more were specified but gated by no DoD**, which is the same gap wearing a better disguise:
the VAT-band consistency guard (WP10), `426` handling (WP5), `LineMeta.vatBand` (WP3) and the
un-enrol request/approval round trip (WP4). All four are gated now.

> ### ⚠ The lesson, since it will recur
>
> **A ruling in a table is not a specification.** The retrofit plan's §3a is a decision log; §5/§6
> are the only half anyone builds from. When this register cites a WP, cite the **body**, and if the
> body does not describe the work, the item is not homed — however many tables say it is.

### Still genuinely open — Matt's call, not an oversight

- **WP15's timing.** The web till has no test suite at all, so every cross-language pinning test in
  C1 holds only its .NET half. It is scheduled; whether it lands before or after the MAUI UI work
  is a judgement about risk appetite.
- **A `TokenEpoch` revocation claim** (retrofit §9.8). There is no server-side session revocation
  for any principal — revoking a device or resetting a password stops the *next* sign-in and does
  not eject a live 12h token. The fix is a column, a claim and a comparison. Recommended, unscheduled.
- ~~**Whether `ATestController` should exist.**~~ **Deleted 2026-08-08.** `GET /api/ATest/testTransaction`
  was anonymous, in production routing, and **wrote a `Business` row** on every call. Nothing
  referenced it. The durable fix is `AnonymousEndpointTests` (C1) — every anonymous endpoint is now
  a reviewed line in a test, so the next one is a red build rather than an audit finding.

## D2. Adding a till

1. Reference `Plutus.Contracts.Client` + `Plutus.Client.Core` (+ `.Storage` if it trades offline).
   Do not re-implement the wire contract, the outbox, or the VAT arithmetic.
2. Read the portal contracts in C1 and cache them on the sync cadence. Hold no rates, no receipt
   layout, no colours of your own.
3. Build sale lines with `VatLineMath`. If you find yourself writing `gross × bp / (10000 + bp)`,
   stop — that disagrees with the receipt the customer is holding.
4. Populate `LineMeta.itemIdOne` on every line. `StockProjectionConsumer` **silently skips** lines
   without it: accepted ≠ stock moved.
5. **Give it its own version file** — `versions/till-<platform>.txt`, and point the project at it
   with `<PlutusVersionFile>`. A new till gets its own release history from day one, because the
   reason it exists is that it will diverge; sharing another till's number guarantees that the
   first platform-specific fix mislabels both. Add it to `ComponentVersionTests.Components`.
6. Add its column to Part B and its rows here.

## D3. Adding a feature, or a rule

**A feature ships to every till.** That is binding rule 1, and this is the procedure that makes it
mechanical rather than a matter of remembering:

1. **Write the Part B row before the code.** All till columns, filled — not just the one you are
   building. Seeing three empty cells next to your ✅ is the point of the exercise.
2. **Put the logic where every till can reach it.** If it computes money, resolves a rule, or
   decides what the operator may do, it belongs in `Plutus.SharedKernel` or `Plutus.Client.Core`,
   not in the UI project you happen to be editing. Then add its C1 row.
3. **Every till that isn't getting it now needs a WP number in Notes.** Not "TODO", not "later" —
   the work package in [`To do/MAUI-Retrofit-Plan-2026-08-07.md`](To%20do/MAUI-Retrofit-Plan-2026-08-07.md)
   that will close it. If no WP covers it, add one; a plan is cheap and an unrecorded gap is not.
4. **A ⬜ needs a reason, not just a number.** "Online-only by design" and "no MAUI model exists
   for this yet" are reasons. An empty Notes cell is how the July drift happened.
5. **➖ is for genuinely inapplicable**, and say why — a browser has no first-run setup; MAUI drives
   OPOS directly so it has no hardware agent to report on.

**A rule:** put it in `Plutus.SharedKernel` and add a row to C1. If it must exist twice, add a row
to C2 saying what pins the copies — or, honestly, that nothing does.

⚠ **The failure this prevents is not hypothetical.** The web till gained nine features in eight
days at the end of July and MAUI silently fell behind on all nine, because each one was "finished"
when it worked in the browser. Parity is not a phase of the retrofit that ends; it is the standing
condition every till commit has to leave true.

## D4. Keeping this true

- **Verify against code, not memory.** Every ⬜ in Part B was checked by grepping both codebases;
  that is the standard, and it took under an hour.
- **C2 is the section that rots dangerously.** Parts A, B and C1 go stale *visibly* — a project
  appears, a contract moves, someone notices. C2 goes stale *invisibly*: a twin quietly added, or a
  pinning test quietly deleted, looks exactly like a document that is still true. So: **when you
  make a rule exist in two places, or stop one existing in two places, say so in C2 in the same
  commit.**
- **The API is the mechanical backstop.** Endpoint usage can be diffed without judgement:

  ```bash
  # every endpoint the web till calls — MUST exclude src/api/types.gen.ts: it is a GENERATED
  # OpenAPI type file whose path string-literals are not calls (including it injects ~30
  # endpoints the till never touches: purchase-orders, tenants, suppliers, roles, periods…)
  grep -rhoE '"/api/[^"`]*|`/api/[^`"$]*' Plutus/Frontend/Plutus.Frontend.WebApp/src \
    --exclude=types.gen.ts | sed 's/^[`"]//; s/[?&].*//' | sort -u
  ```

  Run the equivalent over the MAUI client once WP1 lands and diff the two lists. An endpoint one
  till calls and the other doesn't is either a missing row in Part B or a deliberate ⬜ — never a
  surprise. Two caveats: about a third of the web till's calls are still **legacy `/api/*`
  controllers** (Item, Employee, SavedTransaction, Auth/Login…), so a diff against a
  `/api/v1`-only MAUI client must not read those as parity gaps; and the diff won't catch
  behavioural drift (C2), which is why C2 exists as prose.

- ⚠ **A ✅ MEANS A PERSON HAS USED IT — added 2026-08-10, and it was learned the hard way.** Three
  rows in this register have now been found ✅ against code that could never work: **Inventory CRUD**
  (wrote to a database nothing reads, on a till with no item-write endpoint), the **item search**
  the till never called, and the **outbox drain** that was referenced nowhere in the app. Each was
  fully built and had passing tests; what none of them had was a caller and a human.

  So before writing ✅, ask two questions that a test suite cannot answer for you:

  1. **Who calls this?** `grep` for the entry point. A green suite proves a component works, never
     that anything uses it. This is the single most common way a row here becomes a lie.
  2. **Has anyone driven it?** If the capability has a screen, someone must have used that screen.
     If not, it is 🟡 — and the Notes say "built, not yet driven".

  ⚠ **The failure mode is specific and nasty**: a ✅ that reads as built stops anyone looking, so
  the gap survives every subsequent review of this document. A 🟡 costs nothing and is honest.
