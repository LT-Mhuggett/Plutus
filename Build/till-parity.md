# Till feature parity — web till ↔ MAUI

Two tills, one product. This is the register of what each can do, and it is the **definition of
done for any till feature**: a capability isn't finished until it has a row here, with the other
till marked either ✅ or a deliberate ⬜.

**Web till** = `Plutus.Frontend.WebApp` (React, live). **MAUI** = `Plutus.Frontend.AppClient`
(Sean's rework — the go-forward native app; `ClientUI` is being retired).

Legend: ✅ built · 🟡 partial · ⬜ absent · ➖ not applicable · ⏸ blocked externally.
Last verified against code **2026-08-07**. Retrofit WPs refer to
[`To do/MAUI-Retrofit-Plan-2026-08-07.md`](To%20do/MAUI-Retrofit-Plan-2026-08-07.md).

---

## Why this document exists

The web till gained nine features in eight days at the end of July. The MAUI parity analysis was
written on 2026-08-01 and was already missing several of them by 2026-08-07 — not because anyone
was careless, but because there was nowhere for the gap to show up. Prose written once decays;
a table that every feature has to touch does not.

**The rule:** when you add a capability to either till, add its row here in the same commit. If the
other till isn't getting it, say so in the Notes column and why. A deliberate ⬜ is a decision;
a missing row is a surprise six months later.

---

## 1. Selling (the Till screen)

| Capability | Web | MAUI | Backend | Notes |
|---|:--:|:--:|---|---|
| Scan / search / manual add | ✅ | ✅ | `GET /api/Item` | |
| Basket qty, price adjust, reorder | ✅ | ✅ | — | |
| Discounts | ✅ | ✅ | — | |
| Returns / refund lines | ✅ | 🟡 | — | MAUI validates against a **local** prior sale only — breaks once sales sync centrally. Retrofit risk #3. |
| **Refund-only baskets** (negative sale) | ✅ | ⬜ | `POST /api/v1/sales` | Shipped 2026-08-07. Invariants are sign-agnostic, so it's client work only. |
| Park / retrieve basket | ✅ | ✅ | — | MAUI must reserialise as contract JSON, no `$type` — WP2. |
| Split payment / change | ✅ | ✅ | — | |
| **Gift cards — sell & redeem** | ✅ | ⬜ | `/api/v1/giftcards/*` | **Whole feature missing.** Zero references in MAUI. ⚠ Gated on the per-tenant VAT-treatment decision — `GiftCardSettings`' absence **409s** generate/activate/redeem, so a MAUI till that doesn't understand this gets errors it can't explain. Not in any WP yet. |
| Customer attach at sale + auto-discount | ✅ | ⬜ | `/api/v1/customers` | WP12 |
| Member-number scan-to-attach | ✅ | ⬜ | `/api/v1/customers` | FE2 `NNNNNNC` barcode. WP12 |
| Store credit as tender | ✅ | ⬜ | `.../credit/redeem` | Online-only by design. WP12 |
| **Add unknown scan as a new item** | ✅ | ⬜ | — | Shipped 2026-08-07. Small; fold into WP10. |
| Effective pricing at basket-add | ✅ | ⬜ | `GET /api/v1/prices/effective` | Was till-retrofit row 1. MAUI still reads the legacy `Item.price`. WP5 (offline cache) + WP10. |
| Card capture events | ⏸ | ⏸ | `POST /api/v1/payments/events` | Blocked on a provider for **both**. Terminal integration is greenfield — but note the till already *displays* the configured gateway (next row). |
| Payment-gateway awareness at checkout | ✅ | ⬜ | `GET /api/v1/payments/gateway/active` | WP17.2 — checkout shows the standalone hint or the selected provider with "integration pending" (`CheckoutDialog.tsx:226-233`). |

## 2. Receipts & hardware

| Capability | Web | MAUI | Backend | Notes |
|---|:--:|:--:|---|---|
| Silent receipt print | ✅ | ✅ | — | Different transports, same outcome: web goes through `Plutus.TillAgent`, MAUI drives OPOS directly. MAUI does **not** need the agent. |
| Cash drawer kick | ✅ | ✅ | — | As above. |
| **Portal-controlled receipt template** | ✅ | ⬜ | `GET /api/v1/stores/{id}/receipt-template` | Shipped 2026-08-06. MAUI's layout is **hardcoded C#** (`PosPrinterManager.cs:138-167`) — it *does* print the store's name/logo/address/VAT from the locally-synced StoreModel, but it ignores the portal template entirely (header/footer lines, toggles), contradicting the "all receipts controlled from the portal" requirement. Not in any WP yet. |
| Reprint from a past sale | ✅ | 🟡 | `GET /api/v1/sales/{saleId}` | MAUI reprints locally only; cross-till needs WP11. |
| Barcode on receipt (sale id) | ✅ | ✅ | — | |

## 3. Tabs the MAUI app has no equivalent for

| Area | Web | MAUI | Retrofit |
|---|:--:|:--:|---|
| **Cash** — float, paid in/out, X, Z | ✅ | ⬜ | WP9. MAUI has only `POSCashDrawer.cs`, a solenoid driver. |
| **Loyalty** — members, tiers, credit | ✅ | ⬜ | WP12. Zero references in MAUI. |
| **Users** — employee list/create + set password (legacy `/api/Employee`, `/api/Auth/SetPassword`) | ✅ | ⬜ | **Not in any WP.** MAUI's add-user command is a stopgap dialog reading *"not available in this version yet"*. Roles and effective-permissions management are **portal-side**, not till capabilities — the MAUI parity target is this smaller surface. Overlaps WP8 (RBAC sync) but is a distinct screen. |

## 4. Tabs both have, in different shapes

| Area | Web | MAUI | Retrofit |
|---|:--:|:--:|---|
| Inventory CRUD | ✅ | ✅ | — |
| Stock as a movement ledger | ✅ | ⬜ | WP10. MAUI writes a flat quantity column; concurrent edits are last-write-wins. |
| VAT-band consistency guard | ✅ | ⬜ | WP10 |
| **Portal-published VAT bands** (`GET /api/v1/vat/bands`) | ✅ | ⬜ | **WP2c shipped 2026-08-08 (backend + web till).** The web till caches the bands and their whole effective-dated timeline at boot + every 60 s, and its last hard-coded VAT rate (the single-purpose gift-card `/1.2`) is gone. MAUI must consume the same contract before it prices anything — `PlutusApiClient.GetVatBandsAsync` + `VatBandsResult.RateBpAt` already exist for it. ⚠ **Caching only "today's rate" is a bug**: the timeline is what lets an offline till apply a future-dated change on the day. |
| **VAT band on the sale line** (`LineMeta.vatBand`) | ✅ | 🟡 | **WP2c-exempt, 2026-08-08.** Zero-rated and exempt both declare `vatRateBp = 0`; the band is the only thing separating them, and it decides whether input tax is recoverable (Notice 706). **MAUI SHOULD send it** — resolve from the item's legacy `TaxId` against each band's published `legacyTaxIds`; `LineMeta.VatBand` exists in `Plutus.Contracts.Client`. **Leave it null rather than guessing** when the portal hasn't mapped the tax row. 🟡 not ⬜ because **the server now backfills any line that arrives without one** (`VatBandStamp`, applied in `SalesIngestService` — the single choke point every channel passes through), so MAUI is correct-by-default and only needs to send it for cases the catalogue can't know. A single-purpose gift-card activation/redemption is `"standard"` by the voucher treatment, not its catalogue row — that IS a case the server can't know. |
| **VAT line arithmetic** | ✅ TS | ✅ shared | `VatLineMath` in `Plutus.SharedKernel` is now THE implementation: rate from the price pair, VAT = gross − ex, discount scaled by the ex/inc ratio, returns negated with the discount dropped. `Plutus.Client.Core`/`.Storage` reference SharedKernel and are plain `net10.0`, so **any .NET till on Windows, macOS or Linux gets it unchanged**. The web till stays a deliberate second implementation in TypeScript; `VatLineMathTests` pins both to the same numbers (incl. the JS-vs-.NET midpoint-rounding trap). ⚠ Don't hand-roll a third copy. |
| Category create / rename / reassign / delete | ✅ | 🟡 | WP10. MAUI creates locally; no reassign UI, so the server's 409 has nowhere to land. |
| The Bin (soft delete) + untracked stock | ✅ | ⬜ | FE5 concepts MAUI has no model for. Fold into WP10. |
| Reporting — cross-till, server-aggregated | ✅ | ⬜ | WP11. MAUI queries its **own** SQLite, so it can only ever show one till. |
| Store Information | read-only ✅ | local editor ⬜ | WP6 — the two are *inverted*; this is a deletion, not a build. |
| Settings — local prefs | ✅ | ✅ | — |
| Settings — device enrolment & identity | ✅ | ⬜ | WP4. The largest missing sub-area; no local equivalent to extend. |
| First-time startup / setup | ➖ | ✅ | MAUI-only, and correct — a browser has no first run. |

## 5. Platform citizenship

Everything here exists in the web till and is **absent from MAUI**. None of it was in the original
parity analysis.

| Capability | Web | MAUI | Backend | Notes |
|---|:--:|:--:|---|---|
| Offline trading + outbox | ✅ | 🟡 | `POST /api/v1/sales` | MAUI is offline-*only* — it has local persistence but nothing to sync to. WP3. |
| Device enrolment | ✅ | ⬜ | `/api/v1/tills/enrol` | WP4 |
| Un-enrol request + manager approval | ✅ | ⬜ | `/api/v1/tills/unenrol-request` | WP4 |
| Heartbeat / fleet status | ✅ | ⬜ | *general till heartbeat missing — build it (WP5)* | The web till's presence comes via device enrolment + the agent-status route below; a proper `POST /api/v1/heartbeat` for any till is still to build. |
| Hardware-agent telemetry (agent version/health → Locations) | ✅ | ➖ | `POST /api/v1/tills/agent-status` (`TillsController.cs:283`) | FE3.0. ➖ for MAUI: it drives OPOS directly and has no agent to report on. |
| **Pick-from-floor notifications** (web sale sold shop-floor stock) | ✅ | ⬜ | `GET /api/v1/notifications?unackedOnly=true` + `POST /api/v1/notifications/{id}/ack` | Phase 6. Banner + acknowledge on the till (`App.tsx:264-272`), polled on the 60s cadence. Not in any WP. |
| **Announcements banner** (maintenance/incident) | ✅ | ⬜ | `GET /api/v1/announcements/active` | Not in any WP. Cheap — one poll, one banner. |
| **Help / support tickets** | ✅ | ⬜ | `/api/v1/support/tickets` | Not in any WP. Closes the `support-heavy` churn signal. |
| App-update prompt | ✅ | ⬜ | — | Web polls a build stamp; MAUI needs the `426 Upgrade Required` path in WP5. |
| **Portal-controlled theming** (colour schemes pushed to stores / tills / groups) | ✅ | ⬜ | `GET /api/v1/themes/effective` + `/api/v1/themes` CRUD | Shipped 2026-08-07. Built-in light/dark + custom schemes, assigned per tenant/store/group/till from Locations. MAUI: consume the same effective endpoint and map the eight slots onto XAML resources — folded into retrofit WP7. |
| Operator RBAC + offline login | ✅ | 🟡 | *`tills/{id}/operators` missing* | MAUI logs in locally with no server-derived permissions. WP8. |

## 6. Behavioural parity that isn't a feature

Easy to miss because nothing is "absent" — the two tills just behave differently.

| Behaviour | Risk |
|---|---|
| **Item search matching** | The web till's word-matching (`batman one` → *Batman Year One*) and `"quoted"` exact-phrase are a **client-side device pref** — the server default is whole-phrase. MAUI searching a cached catalogue offline will return *different results for the same query* unless the matcher is shared code. The retrofit plan's WP1 already puts a shared matcher in `Plutus.Client.Core`; this is the reason it matters. |
| **Money** | Web is integer pence throughout; MAUI is `decimal` across 11 model files. WP2. |
| **IDs** | Web mints UUIDv7; MAUI uses string ids on the legacy schema. WP2. |
| **Rounding & VAT derivation** | Must come from one shared validator, not two implementations. WP1/WP2. |

---

## Not yet in any work package

Found by this audit; the retrofit plan predates them. Decide whether each is in or out:

1. **Gift cards** (§1) — a whole feature, and the VAT-treatment gate makes it more than a screen.
2. **Portal-controlled receipt templates** (§2) — contradicts a stated requirement today.
3. **Users / employee management** (§3) — a whole screen.
4. **Announcements** and **Help/support tickets** (§5) — both small, both platform-citizenship.
5. **Refund-only baskets** and **add-unknown-item** (§1) — both small, both shipped 2026-08-07.
6. **The Bin + untracked stock** (§4) — inventory concepts with no MAUI model.
7. **Pick-from-floor notifications** (§5) — small; poll + banner + acknowledge.
8. **Theming** (§5) — consume `GET /api/v1/themes/effective`; already named in WP7, needs its scope extended from "port the palette" to "apply the pushed theme".

---

## Keeping this true

- **Add the row in the same commit as the feature.** Not afterwards.
- **A deliberate ⬜ is fine; an empty row is not.** Say why in Notes.
- **Verify against code, not memory,** when you review this. Every ⬜ above was checked by grepping
  both codebases on 2026-08-07 — that is the standard, and it took under an hour.
- **The API is the mechanical backstop.** Endpoint usage can be diffed without judgement:

  ```bash
  # every endpoint the web till calls — MUST exclude src/api/types.gen.ts: it is a GENERATED
  # OpenAPI type file whose path string-literals are not calls (including it injects ~30
  # endpoints the till never touches: purchase-orders, tenants, suppliers, roles, periods…)
  grep -rhoE '"/api/[^"`]*|`/api/[^`"$]*' Plutus/Frontend/Plutus.Frontend.WebApp/src \
    --exclude=types.gen.ts | sed 's/^[`"]//; s/[?&].*//' | sort -u
  ```

  Run the equivalent over the MAUI client once WP1 lands and diff the two lists. An endpoint one
  till calls and the other doesn't is either a missing row here or a deliberate ⬜ — never a
  surprise. Two caveats: about a third of the web till's calls are still **legacy `/api/*`
  controllers** (Item, Employee, SavedTransaction, Auth/Login…), so a diff against a
  `/api/v1`-only MAUI client must not read those as parity gaps; and the diff won't catch
  behavioural drift (§6), which is why §6 exists as prose.
