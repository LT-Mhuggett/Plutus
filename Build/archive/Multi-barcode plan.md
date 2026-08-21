# Multi-barcode plan — one item, many barcodes

**Product:** An item scans under MORE THAN ONE barcode. The item's identity does not change.
**Author:** Matt Huggett (Leading Talent) with Claude
**Date:** 20 August 2026
**Status:** ✅ **DELIVERED — built AND deployed 2026-08-20, in two halves.**

> ## 📦 ARCHIVED 2026-08-20 — delivered, live, and the endpoints exercised against the real server
>
> **MB1–MB6 shipped in TWO deploys, and the gap between them is the lesson of this plan:**
>
> | | What went out | What it left |
> |---|---|---|
> | **1.19.0** (backend, ⚠ carried the `AddItemBarcodes` migration) · portal 1.15.0 · web till 1.28.0 · MAUI 1.109.0 | The **whole spine** — entity, migration, endpoints, the feed field, alias resolution at all three doors, both tills' offline caches | ⚠⚠ **No way for a person to use any of it.** Neither surface had a control |
> | **1.20.0** (backend) · portal 1.16.0 · web till 1.29.0 | The **UI half** — the barcode list on both item editors, `PUT …/barcodes/{code}` for atomic correction, per-row locking, live checks, and an item change history | Nothing outstanding |
>
> ⚠⚠ **THAT SPLIT IS THE THING TO CARRY FORWARD.** This plan's package list ran from the entity up to
> the sync feed and stopped. It was executed faithfully and the result was a complete, tested,
> deployed feature **that no operator could reach** — Matt found it immediately: *"When I am trying to
> edit an item in the portal or on the webtill, I cannot edit or add a new barcode?"* A plan whose last
> package is a sync feed has no package for the screen. **A feature is not delivered when the data
> path works; it is delivered when somebody can use it.**
>
> ✅ **EXERCISED LIVE against the real backend** (not merely deployed): add → 201 · rename → 204 ·
> a membership-card shape → 400 with its sentence · an interior space → 400 · a code another item owns
> → 409 naming that item · a padded code → trimmed and accepted · history → 3 rows including the
> honest *"Created before change logging began"* bookend · **and the load-bearing one: an alias scan
> answered the CANONICAL `idOne`**, byte-identical to scanning the item's own barcode. Test aliases
> removed afterwards.
>
> **What remains: only the hand-run** — **§G66** and **§G67** of
> [`Build/Test Maui.md`](../Test%20Maui.md) (§G66a first; §G67 is the UI half). Nothing in this
> document is outstanding work.
>
> ⚠⚠ **`Item.IdOne` IS STILL THE IDENTITY, and this plan deliberately did NOT change that** — it seeds
> `DeterministicGuid.ForItem` (frozen golden vector with a TS twin), it is half a composite PK with
> five FK families on it, and it is on every historical sale line. Barcodes are **additive rows that
> resolve to an item**; nothing was re-keyed. ⚠ The design study this plan grew out of recommended the
> opposite — see [`archive/plutus-catalogue-sync-design.md`](../archive/plutus-catalogue-sync-design.md),
> whose banner records what was adopted and what was rejected, and why re-keying was never available.
>
> **Where the durable content went:**
>
> | What | Now lives in |
> |---|---|
> | What may be an item's additional barcode | `SharedKernel/ItemBarcodeRules.cs` — [`till-design.md`](../till-design.md) **C1** |
> | What the operator is told while typing one | `barcodeProblem.ts`, byte-identical in both frontends — **C1**, and **C2** for the twin |
> | Which item a scanned code resolves to (three copies) | **C2** — the row that matters most here |
> | The capability rows, incl. MAUI's deliberate ⬜ | **A0** and **Part B** |
> | Deploy state | [`MAUI-retrofit.md`](../To%20do/MAUI-retrofit.md) **§0.1** |

> ## ✅ WHAT WAS BUILT, AND THE FIVE THINGS WORTH KNOWING
>
> Executed against this plan package by package. Every decision in §2 held; nothing needed asking.
>
> **1. Verified.** unit **1561** · MAUI **625** · architecture **26** · web till **340 vitest** (⚠ run
> on the build Mac, not merely written) · `tsc --noEmit` clean and `eslint` **0 errors** on both
> frontends · both frontends `vite build` clean · backend builds. **Two mutants killed and watched
> dying**: removing the member-card guard killed `A_member_card_shape_is_refused_with_the_reason` by
> name; making the till's alias upsert APPEND instead of REPLACE killed three tests including the
> code-moves-between-two-items case.
>
> **2. ⚠ The migration's `Up()` was correct this time — and reading it was still the right call.**
> One `CreateTable`, six columns, the unique `(TenantId, Code)` index and the `(TenantId, ItemIdOne)`
> one, plus the tenant-filter index EF adds by convention. Nothing else. (Contrast the discount
> migration two days earlier, where reading it caught a default that would have switched off every
> discount a shop had.)
>
> **3. ⚠⚠ The web till had a real fault in the door this needed, and it is now fixed.**
> `findItemById` returned null the instant the server answered 404 — **without consulting the offline
> cache at all**. So an alias scan on a till with a perfectly warm cache would have fallen through to
> the unknown-scan *"Add this item"* offer and **minted a duplicate item**. It now tries the cache and
> then the alias store on a 404 as well as on a network failure. A stale cache hit is the better
> failure by a wide margin.
>
> **4. The Woo hash bug (D11) is fixed, and it was behaviour-neutral today.**
> `CatalogueSkuResolver` hashed *the SKU string it was given* rather than the item's own `IdOne`.
> Identical today (the query matched on `IdOne`), wrong the day anyone makes that resolver
> alias-aware. Fixed while it was free.
>
> **5. What did NOT need touching, verified by reading rather than assumed.** MAUI's view models need
> no change for resolution — `FindItem` already calls `FindByBarcodeAsync` and already canonicalises
> at `Id = found.IdOne`. Pricing, basket merge, `DeterministicGuid`, the legacy bridge and receipts are
> all untouched, because every one of them already keys on the canonical id. The golden vector
> (`Deterministic_item_guid_is_stable_and_well_formed`) never moved.
>
> ### ⚠ One deliberate difference from the plan
>
> §5 MB3 said to leave `ApplyCatalogueChangesAsync` alone with a one-line comment. Done — but the
> comment is a full note, because that method's own header warns future readers that the two upsert
> branches "have to be kept in step by hand". Aliases are the one field where they legitimately
> differ (that overload takes mapped rows and predates the DTO), and a bare "not here" would have read
> as the omission it warns about.
>
> ### ⬜ What is NOT done
>
> **The hand-run, §G66** — eight sections, written and never run. ⚠ **§G66e is the money check**: a
> phantom stock row against the alias code is the one outcome that matters most, because that failure
> is silent by construction. ⚠ Every register row this work touched is **🟡, never ✅**. ⚠ The web
> till's offline alias store (§G66c) has **no automated test anywhere** — `offline.ts` has never had a
> harness, because node has no IndexedDB — so that hand-check is the only coverage it will get.

> ## The ask — Matt, 2026-08-20, verbatim
>
> *"I know the till currently uses the barcode as a unique entry at the moment, but I need to move
> to having multiple barcodes. Can you write an implementation plan on how I can migrate to this."*
>
> ⚠⚠ **THIS REVERSES A RECORDED RULING, ON PURPOSE.** `TillStore.cs:59-69` and
> `LocalSchema.cs:90-98` record that a local `BarcodeAlias` table existed, was never written by
> anything, and was deleted on 2026-08-09 — *"Matt confirmed the same day that multi-barcode items
> are not needed."* That deletion note itself says what real support requires: *"a server entity, a
> feed field and a portal UI first — a platform decision, not a till change."* **This plan is that
> platform decision.** When you touch those two files, rewrite their comments to record the
> reversal and point here — do not leave a comment asserting the opposite of what the code does.
>
> ⚠ **The architecture was always on this side.** `plutus-platform-architecture.md:155` declares
> `Item ─ Barcodes (many per item)` in the core data model, `:188` gives `Plutus.Catalogue` "items,
> **barcodes**, band validation, publish feed", and `kapow-db-gap-analysis.md:24-25` (F4) calls
> barcode-as-PK a defect: *"one product with multiple barcodes"* is the second failure it lists.
> `Build/To do/plutus-catalogue-sync-design.md` §3.1 gives the same shape (an alias table) and §9
> the same worked example (supplier changes codes; both resolve during transition). Architecture
> wins design conflicts, and it agrees with this plan.

---

## Contents

- §1 How to execute this plan (read first, every session)
- §2 Decisions — ratified, do not revisit
- §3 What exists today, verified 2026-08-20 (file:line)
- §4 The design in one page
- §5 The work packages — MB1…MB6, in order
- §6 ⚠⚠ The traps — what you must NOT do
- §7 Registers this work must leave true
- §8 What is deliberately NOT in this plan
- §9 Size, honestly

---

## 1. How to execute this plan

**Authority order on any conflict:** `plutus-platform-architecture.md` → `Build/till-design.md` →
this plan. If the CODE contradicts a file:line in §3, trust the code, note the drift in your
final summary, and keep going — the design decisions in §2 still bind.

**Read before writing anything** (in this order): `CLAUDE.md` · `Build/till-design.md` Part C1
"Money and VAT" + "Identity and sale shape" + Part C2 + Part D3 · `Build/repo-runbook.md` (all of
it — the pitfalls each cost a session) · this plan end to end.

**Environment facts you would otherwise waste an hour discovering:**
- Windows dev box. `dotnet` on PATH is the **x86 one with NO SDKs** (runbook pitfall 19). Always
  use `& 'C:\Program Files\dotnet\dotnet.exe' …` (PowerShell) or
  `"/c/Program Files/dotnet/dotnet.exe"` (bash).
- **No Node on Windows.** TypeScript is verified on the build Mac:
  `ssh -i ~/.ssh/plutus_mac_ed25519 admin@10.1.1.40`, `export PATH=/opt/homebrew/bin:$PATH` first.
  Sync source by tar/scp (whole project incl. `index.html`, `vite.config.ts`, `eslint.config.js` —
  never just `src/`), symlink `node_modules` from `~/PLUTUS/Plutus.Frontend.WebApp`, run
  `npx vitest run`, `npx tsc --noEmit`, `npx eslint . --quiet`. ⚠ **If the Mac is unreachable, do
  not stop**: finish the work, run every .NET suite, and state plainly in your summary and in
  `HANDOVER.md` that the TypeScript halves are unexecuted — that honesty is the fallback, not a
  failure.
- **Suites that must be green after every package** (all four):
  `tests\Plutus.Tests.Unit`, `tests\Plutus.Tests.Architecture`,
  `Plutus\Frontend\Plutus.Frontend.AppClient.Tests`, and (on the Mac, if reachable) the web till's
  vitest. `tests\Plutus.Tests.Integration` needs live MySQL — run it if it runs on this box;
  otherwise say so, don't fake it.
- **EF migration**: the exact incantation is in `repo-runbook.md` §"EF migration". ⚠⚠ **OPEN THE
  GENERATED `Up()` AND READ IT** — on 2026-08-20 EF generated a wrong default that would have
  switched off every discount a shop had, and on 2026-08-09 a migration named for three columns
  contained only an index. Never run `ef migrations remove`.
- **Versions:** bump in the same commit as the change — `versions/backend.txt` (Y-bump: new
  capability), `versions/platform.txt` (Y-bump: SharedKernel/Contracts/Client.* changed),
  `versions/till-web.txt`, `versions/till-maui.txt`, `versions/portal.txt`. Read
  `versions/README.md` first; take current values from the files, do not assume the ones in
  this plan's examples.
- ⚠ **Do NOT commit, deploy, or build the MAUI artefact** unless Matt asks. Building the MAUI
  *test* suite and `dotnet build` for validation is fine and required.
- **Register discipline (till-design D3):** fill the Part B row FIRST, before code, all columns.
  Update `till-design.md` in the same body of work as the code it describes. §7 drafts the rows.
- **When done:** rewrite `HANDOVER.md`'s START-HERE for the next session (it is one day long, on
  purpose), add hand-run section **§G66** to `Build/Test Maui.md` (§5 MB6 drafts it), and leave
  every new capability row **🟡, never ✅** — only a person at a screen makes ✅.

---

## 2. Decisions — ratified, do not revisit

| # | Decision | Why (recorded so nobody re-litigates) |
|---|---|---|
| **D1** | **`Item.IdOne` stays the item's identity, forever.** A new table holds ADDITIONAL barcodes ("aliases") that resolve to the item. Nothing is re-keyed. | `IdOne` is the composite PK half with FKs from `Stock`, `Transaction`, `Refund`, `Discount_Item`, `CheckoutItemChange` (`RepositoryContext.cs:194-342`); it seeds `DeterministicGuid.ForItem` whose output is **frozen by a golden vector** (`LegacySaleBridgeTests.cs:319-324`) and twinned in TS (`pipeline.ts:199`); it is on every historical sale line (`SaleLine.ItemIdOne`), every price/stock row, and the sync cursor. Re-keying is a rewrite of the platform; aliasing is additive. The catalogue-sync design's principle 5 ("identity is internal and permanent; barcodes are mutable aliases") is SATISFIED by treating `IdOne` as the permanent internal id — one alias happens to share its string. |
| **D2** | **Canonicalise at the resolution boundary; the alias string never travels.** A scan of an alias resolves to the item, and everything downstream — basket line, `LineMeta.itemIdOne`, `DeterministicGuid`, prices, stock, discounts, merge — sees only the canonical `IdOne`. | An alias that leaks onto a sale line creates **phantom stock** (`StockLedger.cs:116-131` inserts a `StockLevel` for any unknown string, no FK, silently) and a **dropped VAT band** (`VatBandStamp.cs:57-86` joins the line's string to `Items.IdOne` and quietly gives up). Both are silent money/compliance faults. `SaleAssembler` (`Basket.cs:123-127`) already throws on a GUID/IdOne mismatch — that guard is the tripwire that makes D2 enforceable. |
| **D3** | **The scanned alias IS recorded** — a new optional `barcodeScanned` field on `LineMeta`, written only when it differs from `itemIdOne`. | Catalogue-sync design §7: *"when a supplier barcode migration goes wrong, knowing which alias tills actually scanned is gold."* Omitted when equal, so every existing payload stays byte-identical. The server stores `DiscountsJson` verbatim (proven: `discountAuthority` survived the same way), so **no server ingest change**. |
| **D4** | **Aliases are managed in the PORTAL only.** Tills read. Writes gated `perm:portal.stock.adjust`; reads `[Authorize]`. | Single-writer principle (sync design §1.2). `portal.stock.adjust` is what gates the closest precedent — category create/edit/delete (`CategoriesController.cs:72-149`): a catalogue-structure edit affecting one item. `inventory.bulk` is deliberately for thousand-row mistakes; this is not that. |
| **D5** | **Aliases ride ON the item** in both sync paths: a `Barcodes` array on `CatalogueItemDto` (MAUI feed) and a whole-list endpoint `GET /api/v1/items/barcodes` (web till full-pull). Alias writes touch `Item.ModifiedAt`. Removal = row deleted, list replaced at the till — **no tombstones**. | The feed cursor is over `Item` (`CatalogueCursor.cs`) — an alias table gets no feed coverage unless the item is touched, exactly the `TouchItemForSyncAsync` lesson prices already learned (`Pricing.cs:75-99`). Full-set-per-item replacement makes application idempotent (sync design §3.3 "full-row payloads"); an alias is never referenced by history (D3 stores a STRING snapshot, no FK), so hard delete is safe and tombstones would be machinery without a customer. |
| **D6** | **Matching is exact-after-Trim, per store, exactly as `IdOne` matches today.** No case-folding is added anywhere. | Today `IdOne` matching is: MySQL collation (server), `==` on SQLite TEXT (case-SENSITIVE, `TillStore.cs:74`), IndexedDB key (case-sensitive, `offline.ts:62`). That asymmetry already exists for `IdOne` and has never bitten, because scanners emit exact strings. Aliases inherit the identical behaviour; inventing normalisation would make an alias match where an `IdOne` would not — a new inconsistency in the name of fixing a theoretical one. |
| **D7** | **Reserved shapes may never be aliases** — refused by a SharedKernel rule with a sentence each: member-card scans (`MemberNumbers.LooksLikeMemberScan`), gift-card codes (`GiftCardCodes.LooksLikeCard`), carrier bags (`CarrierBags.IsBagId`), `GIFT-CARD`, `CARD-SURCHARGE` (both OrdinalIgnoreCase), empty/whitespace, interior whitespace, length > 20. | Member and gift-card routing runs BEFORE item lookup on both tills (web `TillPage.tsx:343/:356`; MAUI `TillViewModel.cs:1559`), so such an alias could never be scanned — it would be a dead row that looks configured. Bags/`GIFT-CARD`/`CARD-SURCHARGE` are identities with their own money rules (till-design C1). 20 = `Item.IdOne`'s `[MaxLength(20)]` (`Item.cs:17`). |
| **D8** | **Uniqueness:** an alias is unique per tenant across BOTH namespaces — it may not equal any `Item.IdOne`, nor another item's alias. Re-adding the same alias to the same item is an idempotent no-op. And the reverse guard: **creating an item whose `IdOne` is a taken alias is refused** (409). | Two items answering one scan is the ambiguity the till-side unique index exists to prevent (`TillDbContext.cs:44`). The reverse guard closes the back door: `POST /api/Item` today has NO duplicate check beyond the PK (`portal api.ts:746-751`), and the portal's `checkBarcodeFree` calls `findItemByBarcode` → `/api/Item/{id}` — which becomes alias-aware in MB2, so the portal's guard catches aliases with zero portal changes. |
| **D9** | **Aliases resolve on the EXACT-SCAN path only.** Token search (name/idOne/brand — `ItemSearch.Matches`, `ItemParameters`, `TillStore.SearchAsync`, `offline.ts cachedItemSearch`) does NOT search aliases in v1. | Typing/scanning the full alias resolves (the exact-lookup step runs before search on both tills). Substring search over aliases needs four implementations moved in lockstep (`ItemSearch.cs:66-67` says exactly this) — a separate slice if ever asked for. Recorded in §8. |
| **D10** | **Aliases on a BINNED item do not scan, and cannot be added.** Adding to a binned item → 400 with a sentence ("restore it first"). Resolution excludes binned items exactly as `IdOne` lookup does today. | `FindByBarcodeAsync` already refuses `Removed` (`TillStore.cs:75`); `ItemController.FindById` already 404s binned without `includeBinned` (`ItemController.cs:52-53`). Aliases inherit both. |
| **D11** | **The webstore stays exact-`IdOne` in v1 — but its latent hash bug is fixed in MB2:** `CatalogueSkuResolver.cs:33` returns `DeterministicGuid.ForItem(hit.IdTwo, s)` — hashing **the given SKU string**. It must hash `hit.IdOne`. | Today `s == hit.IdOne` always (the query matched on it), so the fix is behaviour-neutral NOW — and the moment anyone later makes the resolver alias-aware, the old line would mint a DIFFERENT item GUID for the same physical item than every till does. Fix it while it is free; alias-aware Woo SKUs are §8. |
| **D12** | **No FK from the alias table to `Items`.** The writer validates the item exists; orphans are impossible because items are never hard-deleted (Bin only). | House precedent: `PriceListEntry`/`PriceOverride`/`ItemPricePolicy`/`StockLevel` are all plain `ItemIdOne` strings with no FK (`MySqlDbContext.cs:782-855`). |

---

## 3. What exists today — verified against the code 2026-08-20

**There is NO barcode/alias entity anywhere.** Exhaustive search of `Plutus.Entities\Models`,
legacy `Plutus\Data\Database\Models`, and all migrations: the only alias-ish thing is
`WebstoreSkuMap` (`Models\Webstore.cs:109`). The till-side stub was deleted 2026-08-09 (see the
banner above).

### The identity chain (do not touch any of it)

| What | Where |
|---|---|
| `Item : CompositeBase<string, Guid>` — `IdOne` `[Key, MaxLength(20)]`, doc: "EAN/UPC product code or other Primary Identifier" | `Plutus\Commons\Plutus.Entities\Models\Item.cs:14-18` |
| Composite key + five FK families on `(ItemIdOne, ItemIdTwo)` | `RepositoryContext.cs:120-121, 194, 219, 262, 282, 342` |
| `DeterministicGuid.ForItem(businessId, itemIdOne)` — SHA-256 of `"plutus:item:{businessId:D lower}:{itemIdOne}"` | `src\Plutus.SharedKernel\DeterministicGuid.cs:24-27` |
| ⚠⚠ Its **frozen golden vector** (`4abfb7bf-…e22e` for `TEST-ITEM`) | `tests\Plutus.Tests.Unit\LegacySaleBridgeTests.cs:319-324` |
| Its TS twin | `Plutus.Frontend.WebApp\src\pipeline.ts:199` (`itemGuid`) |
| `SaleAssembler` refuses a line with no `IdOne` and THROWS on a GUID/IdOne mismatch | `src\Plutus.Client.Core\Basket.cs:111-127` |
| Ingest copies `itemIdOne` verbatim from `LineMeta`, never validates it against `Items` | `src\Plutus.Sales\SalesIngestService.cs:93, 430-436` |
| ⚠⚠ Stock projection: unknown `itemIdOne` ⇒ **phantom `StockLevel`, silently** | `src\Plutus.Catalogue\StockLedger.cs:104-131` (esp. `:116-120`) |
| ⚠⚠ VAT band stamp: unknown `itemIdOne` ⇒ band silently left null | `Plutus\Commons\Plutus.Entities\VatBandStamp.cs:57-86` |

### The resolution doors (where the work goes)

| Door | Where | Today |
|---|---|---|
| 1. Server exact lookup — **the till scan endpoint** | `src\Plutus.Catalogue\ItemController.cs:49-56` `FindById(string id1, …)`, binned→404 at `:52-53` | `IdOne` only |
| 2. MAUI/local exact lookup | `src\Plutus.Client.Storage\TillStore.cs:71-76` `FindByBarcodeAsync` — `IdOne == code && !Removed` | `IdOne` only; doc `:59-69` is the ruling being reversed |
| 3. Web till online lookup | `WebApp\src\api.ts:288` `findItemById` → `GET /api/Item/{id}`; ⚠ **404 returns null WITHOUT trying the cache** (`if (res.status === 404) return null;`) — so door 1 must be alias-aware or online alias scans fail even with a warm cache | server-keyed |
| 4. Web till offline lookup | `WebApp\src\offline.ts:62` `cachedItemById` — IndexedDB `items` store, keyPath `idOne`, `DB_VERSION = 3` (`:12-34`), **zero indexes** | `IdOne` only |
| 5. Webstore SKU resolver | `src\Plutus.Webstore\CatalogueSkuResolver.cs:21-33` — matches `Items.IdOne`, ⚠ hashes the GIVEN string at `:33` (D11) | exact `IdOne` |
| 6. The unknown-scan create flows — **where an unresolved alias mints a DUPLICATE item** | web `TillPage.tsx:578-587` → `newItemHandoff.ts` → `InventoryPage`; MAUI `TillViewModel.cs:3684` `OfferToAddUnknownAsync` → `ViewAllViewModel.cs:1157` (`Id = barcode, IdOne = barcode`) | reached whenever doors 1-4 miss |

### The sync paths

- **MAUI**: `CatalogueItemDto` (`src\Plutus.Contracts.Client\SyncContracts.cs:83-133`; ⚠ a
  deliberate TWIN copy lives at `CatalogueChangesController.cs:20-40` — change both). Feed:
  `src\Plutus.Tenancy\Controllers\CatalogueChangesController.cs:77-195`, cursor over
  `(ModifiedAt, IdOne)` (`CatalogueCursor.cs:40-47`); per-page joins key on `pageIds` of `IdOne`
  (`:133-161`). Client loop `SyncClient.cs:133-162` → `TillStore.ApplyCatalogueAsync`
  (`TillStore.cs:355-397`, one transaction/page, matches on `dto.Id`), second upsert branch
  `ApplyCatalogueChangesAsync` (`:287-315`), `Map(dto)` (`:410-434`). Local store:
  `CatalogueItem` (`LocalSchema.cs:27-78`), `TillDbContext.SchemaVersion = 6` (`:21`), upgrade
  steps `:133+`, v5 precedent for **cursor reset to backfill a new wire field** (`:227-228`),
  `AddColumnIfMissingAsync` whitelists exactly three DDL fragments (`:276`).
- **Web till**: **full re-pull every boot**, no cursor — `api.ts:658` `syncCatalogue` pages
  `/api/Item/Index?...&IncludeCarrierBags=true` into `cacheItems` (`offline.ts:51`). Called
  fire-and-forget at `App.tsx:170`.
- **Feed reachability rule**: `PricingService.TouchItemForSyncAsync` (`src\Plutus.Catalogue\
  Pricing.cs:91-99`) — marks the `Item` Modified so `RepositoryContext.SaveMethods()` stamps
  `ModifiedAt`; deliberately no SaveChanges (caller batches). Pinned by
  `CatalogueSyncE2eTests.cs:287`.

### Scan routing order (both tills, unchanged by this plan)

Web (`TillPage.tsx:332-409`): member regex → gift regex → **`findItemById` exact** → token search →
loose member rescue → unknown-scan offer. MAUI (`TillViewModel.cs:455-460, 1559, 3615-3652`):
`TryRouteMemberScanAsync` (member + gift, gift is terminal) → **`FindByBarcodeAsync` exact** →
`SearchForOneAsync` → `OfferToAddUnknownAsync`. Aliases slot inside the EXACT step on each till —
the routing order itself does not change.

### Reserved identity shapes (D7 refuses them all)

`MemberNumbers.LooksLikeMemberScan` (`MemberNumbers.cs:150-157`) · `GiftCardCodes.LooksLikeCard`
(`GiftCardCodes.cs:93`) · `CarrierBags.IsBagId` (`CarrierBags.cs:69-72`) · `GiftCards.ItemIdOne`
= `"GIFT-CARD"` (`GiftCards.cs:27`) · `CardSurchargeVat.ItemIdOne` = `"CARD-SURCHARGE"`
(`CardSurchargeVat.cs:30`).

### Tests that pin today's behaviour

Will need **rewriting, not deleting**: `TillStoreTests.cs:141`
`Barcode_lookup_refuses_binned_items` (its comment asserts the deleted-alias ruling).
Must stay green UNTOUCHED: `MemberNumberTests.cs:185/:211`, `GiftCardCodeTests.cs:198`,
`LegacySaleBridgeTests.cs:319`, `ItemSearchTests.cs` (all), `basketMerge.test.ts`,
`pipeline.test.ts`. Guards that watch upserts: `CatalogueUpsertTests.cs:144`
`EVERY_catalogue_field_is_copied_on_UPDATE` — ⚠ aliases live in a SEPARATE local table, so
`CatalogueItem` gains **no field** and this test stays green by design; if you find yourself
adding a column to `CatalogueItem`, you have left the plan.

---

## 4. The design in one page

**A new server entity — `ItemBarcode` — additive, tenant-scoped, no FK:**

```csharp
// Plutus\Commons\Plutus.Entities\Models\ItemBarcode.cs   (server-only: DbSet on MySqlDbContext)
public class ItemBarcode
{
    public Guid Id { get; set; }              // Uuid7.New(), ValueGeneratedNever
    public Guid TenantId { get; set; }        // real column; add typeof(ItemBarcode) to TenantOwned
    public string Code { get; set; }          // MaxLength(20) — the alias as scanned
    public string ItemIdOne { get; set; }     // MaxLength(20) — the item's CANONICAL id
    public Guid BusinessId { get; set; }      // Items.IdTwo of the item (audit/joins)
    public DateTime CreatedAtUtc { get; set; }
}
// Indexes: (TenantId, Code) UNIQUE · (TenantId, ItemIdOne)
```

**One SharedKernel rule** (`ItemBarcodeRules`) answers "may this string be an alias" (D7) with a
sentence per refusal — enforced server-side; the portal displays the sentence from the 400.

**Three resolution doors become alias-aware, each canonicalising (D2):**
1. `ItemController.FindById` — miss on `IdOne` → look up `ItemBarcodes` → return **the item**.
2. `TillStore.FindByBarcodeAsync` — miss on `IdOne` → local `LocalItemBarcodes` join → the item.
3. Web offline `cachedItemById` — miss in `items` → new `aliases` store → get by canonical.

**Sync:** `CatalogueItemDto` gains trailing `string[]? Barcodes = null` (both twins); the feed
joins the alias table per page; every alias write calls the touch-for-sync pattern; MAUI local
schema v7 adds `LocalItemBarcodes` + resets the catalogue cursor once (backfill); the web till
gains `GET /api/v1/items/barcodes` pulled inside `syncCatalogue` into the new store.

**Wire:** `LineMeta` gains optional `barcodeScanned` (D3); `BasketLine` gains
`string? ScannedBarcode = null`; both tills write it only when it differs from the canonical id.

**Portal:** a "Barcodes" list inside the existing item edit dialog (`InventoryItems.tsx`
`ItemDialog`) — add/remove, edit-mode only, sentences shown verbatim from the server.

**What never changes:** `DeterministicGuid`, `LineMeta.itemIdOne` semantics, `BasketMerge`,
`ItemSearch`, prices/stock/discount keying, the legacy bridge, receipts.

---

## 5. The work packages — in order, each green before the next

> ✅ **ALL SIX ARE BUILT (2026-08-20); only §G66's hand-run remains.** The briefs below are kept as
> written, unedited, because they say *why* each slice was shaped that way — the status box at the top
> of this document says what actually happened, including the one deliberate divergence.

> ⚠⚠ **Ground rules for every package:** read till-design C2 before touching anything that
> computes money on a client · every dialog keeps its ✕ (D4 contract) · a twin's vectors go on
> BOTH sides and the TS side must be RUN, not written (or honestly reported unexecuted, §1) ·
> registers update with the code · run the §1 suites before declaring a package done.

### MB1 — the rule, in SharedKernel · ~½d

**Files:** new `src\Plutus.SharedKernel\ItemBarcodeRules.cs`; new
`tests\Plutus.Tests.Unit\ItemBarcodeRuleTests.cs`.

**Build:** `public static class ItemBarcodeRules` with:
- `public const int MaxLength = 20;` — cite `Item.cs:17` in the doc comment.
- `public static string? Normalise(string? raw)` — Trim; null/empty/whitespace → null.
- `public static string? WhyRefused(string code)` — null when acceptable, else ONE sentence a
  portal user can act on, tested verbatim, in this order: length > MaxLength → "That is longer
  than a barcode can be — 20 characters at most."; interior whitespace → "A barcode cannot
  contain spaces."; `MemberNumbers.LooksLikeMemberScan(code)` → "That is the shape of a member
  card, which is scanned before items are — it could never scan as this item."; `GiftCardCodes.
  LooksLikeCard(code)` → same sentence shape for gift cards; `CarrierBags.IsBagId(code)` → "That
  is a carrier-bag id — bags are set up on the Company page, not as barcodes."; equals
  `GiftCards.ItemIdOne` or `CardSurchargeVat.ItemIdOne` (OrdinalIgnoreCase) → "That id belongs to
  the platform and cannot be a barcode."
- Doc header: cite D7's reasoning — member/gift routing runs BEFORE item lookup on both tills
  (`TillPage.tsx:343/:356`, `TillViewModel.cs:1559`), so a reserved-shape alias is a dead row that
  looks configured.

**Tests (≈14):** one per refusal with the sentence asserted VERBATIM (a reworded sentence is a
register drift); acceptance cases: a plain EAN-13, a 20-char code, a code with leading/trailing
spaces normalising then passing; `"BAG-10"` refused but `"BAGGY-10"` accepted (IsBagId is exact);
`"gift-card"` refused case-insensitively.
**Mutation check (run, watch die, revert):** delete the `LooksLikeMemberScan` branch → the
member-shape test fails by name. State the result in your summary.
**DoD:** unit suite green. **Registers:** C1 row (§7). **Version:** `platform.txt` Y-bump.

### MB2 — the server: entity, migration, endpoints, feed, doors 1 & 5 · ~1–1½d

**Entity + context:** `ItemBarcode` per §4 in `Plutus\Commons\Plutus.Entities\Models\`;
`DbSet<ItemBarcode> ItemBarcodes` on **`MySqlDbContext`** (server-only — NOT `RepositoryContext`,
or the SQLite/MSSQL legacy contexts inherit it); config in `MySqlDbContext.OnModelCreating`
following the pricing block at `:832-855` (`ValueGeneratedNever`, `HasMaxLength(20)` on both
strings, the two indexes from §4); add `typeof(ItemBarcode)` to the `TenantOwned` array
(`MySqlDbContext.cs:183+`) — that array is what makes tenant isolation impossible to forget.

**Migration:** `AddItemBarcodes`, **MySql only** (runbook incantation). ⚠ Read the `Up()`:
it must contain exactly one `CreateTable` with the six columns and two indexes ((TenantId, Code)
unique). Nothing else. If it contains less, the snapshot already thinks the work is done — stop
and hand-write the catch-up per the runbook.

**Touch-for-sync:** every alias write marks the parent `Item` modified, same mechanism as
`Pricing.cs:91-99` (fetch item by `IdOne`, `_db.Entry(item).State = EntityState.Modified`, no
SaveChanges — batch with the alias row). Put the helper ON the new controller; do not widen
`PricingService`.

**New controller** `src\Plutus.Catalogue\ItemBarcodesController.cs`, route-per-method style like
`CategoriesController`, `MySqlDbContext + ITenantContext` ctor, `Actor` claim helper, audited via
`_db.Audit(...)` in the same SaveChanges:
- `GET api/v1/items/barcodes` — `[Authorize]` (any authenticated; the web till pulls it during
  sync) → `[{ code, itemIdOne }]`, whole tenant, ordered by code.
- `POST api/v1/items/{itemIdOne}/barcodes` body `{ code }` — `perm:portal.stock.adjust` (D4).
  Validation order, each with its own 400/404/409 sentence: Normalise; `WhyRefused` (MB1);
  item exists for this business else 404; item binned → 400 "…restore it from the Bin first"
  (D10); `code` equals ANY `Items.IdOne` (this business) → 409 "That is already an item's own
  barcode."; equals another item's alias → 409 naming neither item's details beyond its name;
  equals this item's own `IdOne` → 400 "That is already this item's barcode."; already this
  item's alias → **204 idempotent no-op**. Then insert + touch + audit
  (`"catalogue.item-barcode"`).
- `DELETE api/v1/items/{itemIdOne}/barcodes/{code}` — same gate; idempotent 204 when absent;
  delete + touch + audit (`"catalogue.item-barcode.remove"`).

**Door 1 — `ItemController.FindById`** (`src\Plutus.Catalogue\ItemController.cs:49`): on a miss
(or binned-without-includeBinned), look up the alias in `ItemBarcodes` for the caller's business
and, if found, return the ALIASED item through the same binned/includeBinned rules. ⚠ The
comment must say this is the till's exact-scan door and cite D2. ⚠ `ItemController` currently
sees only `RepositoryWrapper` — inject `MySqlDbContext` for the alias read, following how other
`Plutus.Catalogue` controllers take it.

**Door 8 guard — `ItemController.Post`** (`:58`): before create, refuse an `IdOne` that exists as
any alias (this business) → 409 with "That barcode already points at '<item name>' — remove it
from that item's barcodes first." (D8's reverse guard; the portal and MAUI's unknown-scan create
both surface `problem` strings already.)

**Door 5 fix — `CatalogueSkuResolver.cs:33`:** `ForItem(hit.IdTwo, s)` → `ForItem(hit.IdTwo,
hit.IdOne)`, with a comment recording D11 (behaviour-neutral today; wrong the day the resolver
learns aliases). Select `IdOne` alongside `IdTwo` at `:31`.

**Feed:** add `string[]? Barcodes = null` as the LAST param of BOTH `CatalogueItemDto` twins
(`SyncContracts.cs:83` and the controller-local copy at `CatalogueChangesController.cs:20`);
in `Changes()`, one query per page — aliases where `ItemIdOne` in `pageIds`, grouped — and
project `Barcodes: null` when none (not an empty array; absent keeps old payload shape).

**Tests:** new `tests\Plutus.Tests.Integration\ItemBarcodesE2eTests.cs` (copy the harness shape
of `CatalogueSyncE2eTests`): add→200/204 semantics; each refusal's status+sentence; alias
resolves via `GET /api/Item/{alias}`; binned item's alias 404s; `POST /api/Item` with a taken
alias 409s; **the feed test that matters**: add an alias → the ITEM reappears in
`catalogue/changes` past the old cursor carrying `barcodes:["…"]` (this is the
touch-for-sync proof — name it `An_alias_write_reaches_the_till_feed`); remove → reappears
with `barcodes` absent. Unit: extend nothing in `LegacySaleBridgeTests` (the golden vector must
not move — if it fails you have broken D1; stop).
**DoD:** suites green; integration suite run if MySQL reachable, else stated.
**Registers:** Part B backend column cell; C1 "portal decides, till obeys" row (§7).
**Version:** `backend.txt` Y-bump, `platform.txt` (Contracts changed).

### MB3 — the .NET till store: local table, sync, door 2 · ~1d

**Local schema:** new `LocalItemBarcode` in `LocalSchema.cs` — `Code` (PK, TEXT), `ItemIdOne`
(TEXT, indexed) — plus a header comment REPLACING the deleted-table note at `:90-98`: quote the
old ruling, date the reversal (2026-08-20, this plan), and state the difference — *this time the
platform has the entity, the feed field and the portal UI first.* `TillDbContext`: map it
(`ToTable("LocalItemBarcodes")`, key `Code`, index `ItemIdOne`); `SchemaVersion` 6 → **7**;
`from < 7` step: `CREATE TABLE IF NOT EXISTS` via `ExecuteSqlRawAsync` (the v3/v4 pattern at
`:138/:158`, NOT `AddColumnIfMissingAsync` — that helper only allows three column DDLs) **plus
the cursor reset** (`DELETE FROM "Meta" WHERE "Key" = 'catalogueVersion'` — the v5 precedent at
`:227-228`) so every existing till re-pulls the catalogue once and backfills alias rows. Update
the `TillDbContext.cs:55-58` comment the same way as `LocalSchema`'s.

**Upserts:** in `ApplyCatalogueAsync` (`TillStore.cs:355`) — for EVERY dto (new or existing):
delete this item's alias rows, insert `dto.Barcodes ?? []`, inside the existing per-page
transaction (replacement is what makes removal work with no tombstones, D5). ⚠ Do NOT touch
`ApplyCatalogueChangesAsync` (`:287`) — that branch predates the DTO and carries no aliases; add
one comment line there saying so, or its "second upsert branch" warning will send a future
reader hunting.

**Door 2:** `FindByBarcodeAsync` (`:71`) — on `IdOne` miss, join `LocalItemBarcodes` on `Code ==
code` → load the item by `ItemIdOne`, still refusing `Removed`. **Rewrite the `:59-69` doc
comment** to record the reversal (as above) and D2: *the caller receives the CANONICAL item; the
alias string goes no further than this method* (except D3's snapshot, which the caller takes
from its own input).

**MAUI app:** ⚠ **zero view-model changes for resolution** — `FindItem` already calls
`FindByBarcodeAsync`, and canonicalisation at `TillViewModel.cs:3648` (`Id = found.IdOne`)
already does D2's work. Verify by reading, not by editing.

**Tests** (`TillStoreTests.cs` + `CatalogueUpsertTests.cs`):
`An_alias_scans_to_its_item` · `An_alias_of_a_binned_item_does_not_scan` ·
`An_alias_removed_upstream_stops_scanning_after_the_next_sync` (apply dto with alias, re-apply
without, assert miss) · `An_alias_never_changes_which_item_the_CANONICAL_code_finds` ·
rewrite `Barcode_lookup_refuses_binned_items`'s comment (behaviour unchanged) ·
`Aliases_are_replaced_per_item_not_accumulated` (apply twice with different sets → only the
second set matches). **Mutation check:** make the upsert append instead of replace → the
replaced-per-item test dies.
**DoD:** unit + MAUI suites green (MAUI suite compiles Client.Storage).
**Registers:** Part B MAUI cell 🟡; C2 row (§7). **Version:** `platform.txt`, `till-maui.txt`.

### MB4 — the web till: sync, offline store, door 3 · ~1d

**`api.ts`:** `Item` gains nothing (aliases are NOT on the legacy Item payload). New
`fetchItemBarcodes(): Promise<{code:string; itemIdOne:string}[]>` calling
`GET /api/v1/items/barcodes`; called from INSIDE `syncCatalogue` (`api.ts:658`) after the item
pages complete → `cacheAliases(rows)`; on fetch failure keep the existing cached aliases
(last-known-good — the same fail direction as `fetchDiscountRules`). `findItemById` (`:288`)
gains a final step: when the server 404s AND the offline lookup misses, try
`cachedItemByAlias(id)` — and when the server is UNREACHABLE, the existing catch path likewise
falls through to the alias lookup. ⚠ The server 404 path currently `return null` **without**
consulting the cache; change it to consult `cachedItemById` then `cachedItemByAlias` — the
comment must say why (a stale-cache hit beats a duplicate item minted by the unknown-scan flow;
the worst case is a tombstoned item, which the basket-add price resolution treats like any other
stale cache entry, refreshed on next sync).

**`offline.ts`:** `DB_VERSION` 3 → **4**; `onupgradeneeded` adds
`db.createObjectStore("aliases", { keyPath: "code" })` (guarded by `contains` like the others);
`cacheAliases` = clear + putAll (full-set replacement, D5 — the endpoint returns the whole
tenant); `cachedItemByAlias(code)` = get alias → `cachedItemById(alias.itemIdOne)`. ⚠ Do NOT
touch `cachedItemSearch`'s three-field filter (D9) — add the one-line comment saying aliases are
deliberately not searched, citing this plan.

**D3 (web half):** `basket.ts BasketLine` gains `scannedBarcode?: string`; `TillPage.tsx
submitScan` exact-hit branch (`:360`) passes the scanned term to `addItem` and the reducer
stores it ONLY when `term !== item.idOne`; `api.ts checkout`'s `discountsJson` literal
(`:1282+`) adds `barcodeScanned: l.scannedBarcode || undefined` — omitted when absent so old
payloads stay byte-identical (the `discountAuthority` precedent, same file).

**Tests (vitest, run on the Mac per §1):** a pure-function test file for whatever you extract —
at minimum the reducer change (`scannedBarcode` stored only when different; survives park/recall
via the existing persistence test pattern) in `till\basket` coverage; note honestly that
`offline.ts` has no test harness (node lacks IndexedDB; the file has never had tests —
`C12` in the survey) — §G66 covers it by hand.
**DoD:** vitest + `tsc --noEmit` + `eslint --quiet` clean on the Mac (or stated unexecuted).
**Registers:** Part B web cell 🟡. **Version:** `till-web.txt`.

### MB5 — the wire: `barcodeScanned` end to end · ~½–1d

**Contracts:** `LineMeta` (`SaleContracts.cs:68+`) gains
`[JsonPropertyName("barcodeScanned")] public string? BarcodeScanned { get; set; }` with a doc
citing D3 and the sync-design §7 line ("which alias tills actually scanned is gold").
**Client.Core:** `BasketLine` record (`Basket.cs:45`) gains trailing
`string? ScannedBarcode = null`; `SaleAssembler.Assemble` writes
`BarcodeScanned = line.ScannedBarcode` only when non-null AND != `line.IdOne`.
**MAUI:** `ItemLookup` record (`TillViewModel.cs:3478+` region) gains `string? ScannedCode`;
`FindItem` sets it when the input differs from `found.IdOne`; `BasketItem` gains
`public string ScannedBarcode { get; set; }` (plain settable — the Newtonsoft parked-basket
rule on `BasketAlteration.cs:13-27`); both `ExecuteItemAdd*` doors copy it onto the NEW line
only (a merged line keeps nothing — quantity merged into an existing line does not overwrite);
`CheckoutCommit.LinesFrom` (`CheckoutCommit.cs:69`) passes it into `BasketLine`.
**Server:** none — `DiscountsJson` round-trips verbatim (`SaleContracts` doc `:117-120`).
**Tests:** `SaleAssemblerTests`: written when different · omitted when equal · omitted when null ·
old parked basket (no field) deserialises. MAUI `CheckoutCommitTests`: one end-to-end case
asserting the line meta carries it.
**DoD:** suites green. **Registers:** C1 identity-table note (§7). **Version:** `platform.txt`,
`till-maui.txt` (already bumped this session? bump again only if MB3's bump already shipped —
one Y-bump per package set is fine; use judgement per versions/README, and say what you did).

### MB6 — the portal editor, registers, hand-run · ~1d

**Portal (`InventoryItems.tsx` `ItemDialog`, `:312+`):** edit-mode only (`item != null`), below
the existing fields: a "Barcodes" list — each row the code + a *Remove* button; an add box +
*Add barcode* button; server sentences shown verbatim (the `detail` of the 400/409, the
`CarrierBagsSection` `readError` pattern); list refreshed from
`GET /api/v1/items/barcodes` filtered client-side to this item (the endpoint is whole-tenant;
do not add a per-item GET for one consumer). `api.ts` (portal) gains the three calls. ⚠ The
barcode PRIMARY box stays immutable on edit (`:387-397` — do not "fix" that; it is D1).
**Hand-run:** append **§G66** to `Build\Test Maui.md` + a line in its START-HERE marked "needs
the next build/deploy". Sections: **a** portal — add an alias to a real item; each refusal
sentence checked (member-shape, bag id, taken barcode, binned item); **b** web till — scan the
alias online → canonical item, canonical price; **c** web till OFFLINE (after one sync) → same;
**d** MAUI — same scan resolves (⚠ needs a catalogue re-sync; the v7 cursor reset does it on
first run of the new build — say so); **e** ⚠⚠ the money check: sell via alias, then in
Reporting confirm the sale line shows the CANONICAL item, stock moved for the canonical item
(no phantom row), and the receipt names the item; **f** remove the alias → after next sync it
stops scanning on both tills and the unknown-scan "Add this item" offer appears instead —
**do NOT accept the offer** (it would mint a duplicate item; that refusal path is §G66g:
accept it and confirm the server 409s with the sentence).
**Registers:** everything in §7 lands now if not already. **HANDOVER.md:** rewritten per §1.
**Version:** `portal.txt`.

---

## 6. ⚠⚠ The traps — read before every package

1. **Never touch `DeterministicGuid.ForItem` or its golden vector** (`LegacySaleBridgeTests.cs:
   319`). If that test goes red, you have changed item identity; revert and re-read D1.
2. **The alias string must never reach `LineMeta.itemIdOne`, `BasketLine.IdOne`, or any price/
   stock/discount call.** The tripwires that catch it: `SaleAssembler` throws (`Basket.cs:124`);
   phantom stock (`StockLedger.cs:116`); dropped VAT band (`VatBandStamp.cs:63/:85`). If you see
   any of those, an alias leaked — fix the leak, never the tripwire.
3. **An alias write that doesn't touch `Item.ModifiedAt` is invisible to every till forever.**
   The feed pages by the ITEM. `An_alias_write_reaches_the_till_feed` is the test that keeps
   this true — write it FIRST in MB2 and watch it fail before the touch call exists.
4. **`CatalogueItemDto` has TWO copies** (contracts + controller). Change both or the build
   breaks — and if it doesn't break, you changed the wrong one twice.
5. **MAUI schema: new TABLE, not columns.** `AddColumnIfMissingAsync` whitelists three DDL
   strings (`TillDbContext.cs:276`); use the v3/v4 `CREATE TABLE IF NOT EXISTS` pattern, bump
   `SchemaVersion` and add the upgrade step IN THE SAME COMMIT (the `:18` rule), and include the
   cursor reset or existing tills never backfill.
6. **Do not add alias search to `ItemSearch`/`ItemParameters`/`SearchAsync`/`cachedItemSearch`**
   (D9). `ItemSearch.cs:66-67` documents why a fourth field must move in lockstep everywhere —
   that is its own future slice.
7. **Do not gate `GET api/v1/items/barcodes` behind a portal permission** — the web till reads
   it with an operator token during sync. `[Authorize]` only (the `carrier-bags` precedent).
8. **Do not "modernise" the legacy `/api/Item` controller** while you are in it — no route
   changes, no permission additions beyond the Post guard, no DTO reshaping. The web till and
   MAUI both live on it.
9. **`ExtractItemIdOne` at ingest and `GiftCardItemIdOne` at `SalesIngestService.cs:579`** are
   not yours to touch. Neither is `WooOrderMapper` beyond nothing — MB2 touches ONLY
   `CatalogueSkuResolver.cs:31-33`.
10. **Sqlite/MSSQL legacy contexts must not learn the entity** — DbSet on `MySqlDbContext` only;
    if `Plutus\Commons\Plutus.Entities\Migrations\Sqlite\` sprouts a migration, you put it on
    the wrong context.
11. **If a register row you need contradicts what you built, fix the code or fix the row — never
    leave them disagreeing.** And every new capability row ends 🟡. A ✅ requires a human.

---

## 7. Registers this work must leave true (draft text — copy, adjust line refs)

- **A0 · Stock and the catalogue:** new row *"Scan an item by ANY of its barcodes"* — Web 🟡 ·
  MAUI 🟡 (⬜ until built).
- **Part B · B4/stock section** (beside "Add a new item"): row **Multi-barcode items** — Web 🟡 ·
  MAUI 🟡 · Backend `GET /api/v1/items/barcodes` + `POST/DELETE api/v1/items/{itemIdOne}/barcodes`
  (`perm:portal.stock.adjust`) + migration `AddItemBarcodes` · Notes: D1 (IdOne stays identity,
  aliases additive), D2 (canonicalise at the boundary; the tripwires), the 2026-08-09 ruling
  reversed 2026-08-20, hand-run §G66.
- **C1 · Identity and sale shape:** row *"What may be a barcode ALIAS, and what it resolves to"*
  → `SharedKernel/ItemBarcodeRules.cs` (reserved shapes refused with sentences; the writer is the
  server, the portal only displays) · second implementation: none — server-enforced · pinned by
  `ItemBarcodeRuleTests` + `ItemBarcodesE2eTests`. Amend the existing *"⚠ The barcode is the real
  invariant, ItemId rides along"* row: add *"…and under multi-barcode (2026-08-20) the wire may
  also carry `barcodeScanned`, the alias actually scanned — `itemIdOne` stays CANONICAL, always."*
- **C1 · Portal decides, till obeys:** row for the alias feed — aliases ride `CatalogueItemDto.
  Barcodes` (MAUI) and `GET /api/v1/items/barcodes` (web full-pull); alias writes touch
  `Item.ModifiedAt` (the prices lesson); removal is set-replacement, no tombstones (an alias is
  never referenced by history).
- **C2:** row *"Alias resolution exists THREE times"* — `ItemController.FindById` ↔
  `TillStore.FindByBarcodeAsync` ↔ `offline.ts cachedItemByAlias` — equivalent BY RULE
  (exact-after-Trim, canonical item returned, binned refused), pinned by `ItemBarcodesE2eTests` /
  `TillStoreTests` / hand-run §G66c (the web copy has no executable harness — say so, the honest
  position every C2 row takes).
- **MAUI-retrofit.md:** nothing — this is not retrofit work. Do not add a section there.

## 8. What is deliberately NOT in this plan

- **Alias-aware token search** (D9) — four implementations in lockstep; its own slice if asked.
- **Alias-aware Woo SKU resolution** (D11) — needs the resolver to return the canonical pair and
  `WooOrderMapper:109/:112` to stop writing the raw SKU; the drift reconciler
  (`WebstoreReconciler.cs:300-312`) too. Do AFTER this plan if the shop lists under aliases.
- **Pack quantities on aliases** (case barcodes, sync-design §3.1 `pack_qty`) — a money feature
  (one scan = N units) with basket/receipt/stock consequences; needs its own ruling from Matt.
- **Retiring an item's FOUNDING barcode from scanning** — `IdOne` always scans (it is the
  identity). The alias table's shape supports an `Active` flag later if ever wanted.
- **Weight-embedded barcodes** (prefix 20–29, sync-design §8) — a scan-path parser, untouched.
- **Aliases in the portal Discounts rule "extra barcodes" box** — rules match canonical ids;
  typing an alias there will not match. One-line note on that screen is optional polish, not
  scope.

## 9. Size, honestly

| Package | Size |
|---|---|
| MB1 shared rule | ~½d |
| MB2 server (entity, migration, endpoints, feed, doors 1+5) | ~1–1½d |
| MB3 .NET till store (schema v7, sync, door 2) | ~1d |
| MB4 web till (sync, IndexedDB v4, door 3) | ~1d |
| MB5 wire `barcodeScanned` | ~½–1d |
| MB6 portal editor + registers + §G66 | ~1d |
| **Total** | **~5–6d** |

Strictly in order — every package leaves all suites green and the registers true, so the work can
stop after any package without leaving a half-truth anywhere.
