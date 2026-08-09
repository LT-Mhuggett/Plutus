# MAUI cutover to the v2 store — the ordered plan

**Created 2026-08-09**, from a survey of the actual tree (not the retrofit plan's prose) after
Matt's binding decision: *"I would not bridge the legacy DB, it is not needed."*
See retrofit plan **binding default 9**.

This document exists because "finish WP6–13" turned out to be a much larger and differently-shaped
job than the retrofit plan implies, and the difference is worth writing down before anyone starts.

---

## ⚠ THE HEADLINE: 11–15 WEEKS FOR ONE DEVELOPER

Roughly **55–75 working days**. Measured by surveying every screen, not estimated from the plan.

It is not "a port". Four subsystems the Till screen depends on have **no v2 equivalent on either
side of the wire** — payment-method roster, discount catalogue, parked-basket persistence, and
prior-sale lookup — and one structural prerequisite, an **operator token for the v2 spine**, does
not exist anywhere and gates five work packages.

> ### ⚠ THE SINGLE MOST IMPORTANT FACT
>
> **The MAUI till cannot post a sale to Plutus at all today.** Its checkout writes a legacy EF
> object graph and calls `db.Save()`. It never builds an `IngestSaleRequest`, never touches the
> outbox, never calls `/api/v1/sales`.
>
> **Every screen port is cosmetic until that changes.** A till that looks right and cannot sell
> into the platform is not closer to parity than one that looks wrong.

**Steps 1–14 are the critical path and deliver the most working till soonest**: they take an
offline-only cash register to a till that sells into Plutus with correct VAT and a draining
outbox. Steps 15+ are parity breadth.

---

## What the retrofit plan gets wrong, and should be corrected

⚠ **"WP6–13 need a device to verify" is substantially false.**
`Plutus.Frontend.AppClient.Tests` is a real xunit project with `InternalsVisibleTo` from the app
and existing viewmodel/service tests. **Viewmodel and service logic is headlessly testable today.**
Only XAML rendering, the printer, the drawer and the scanner genuinely need hardware. Treating the
whole of WP6–13 as device-gated has been deferring work that could have been done and tested all
along.

---

## The dependency spine

```
1–4    Foundation: EF9 · reference · TillStoreAccess · Meta populated
         └─ nothing else can open the v2 store or know which store it is
5–8    Line primitives: price PAIR · TaxId · VAT bands · tender enum
         └─ a basket line cannot be built without all four
9–14   THE MONEY PATH: basket → IngestSaleRequest → outbox → server
         └─ where the till starts being a Plutus till
15–18  Returns · parking · reprint — needs a sale READ path that exists nowhere
19     Operator token (BACKEND BUILD) — unblocks WP8/10/11/12/13
20–27  Screens, cheapest-first
```

---

## Phase 0 — Foundation · ~3–5 days · ✅ **DONE 2026-08-09**

| Step | What | State |
|---|---|---|
| 1 | **Unify EF Core on 9.0.18** — ⚠ was the riskiest step | ✅ `ea9787f` |
| 2 | Add the `Plutus.Client.Storage` project reference | ✅ `ea9787f` |
| 3 | Build `Services/Storage/TillStoreAccess.cs` | ✅ `ea9787f` |
| 4 | Route enrolment through `EnrolmentFlow` so Meta is populated | ⬜ **next** |

**Step 1 was the wall.** Referencing the v2 store (EF 9) from an app pinned to EF 3.1 is
impossible — one version of an assembly loads. Bumping the app alone *compiled cleanly* and then
died at runtime with `MissingMethodException: MigrationBuilder.CreateIndex(...)`, because the
legacy migrations were compiled against 3.1. Fixed by retargeting `Plutus/Data/Database` to
`net10.0` and recompiling against EF 9. Pinned by `LegacyDatabaseUnderEf9Tests` — that layer had
**no test coverage at all** beforehand.

⚠ **Step 4 is a silent blocker and is next.** `EnrolmentFlow` exists in `Plutus.Client.Storage` and
writes `MetaKeys.StoreId` / `BusinessId` / `TillId` / `ServerUrl`. **MAUI never calls it** —
`ConnectionViewModel` calls `api.EnrolAsync` directly, so the v2 store never learns which store it
is. ⚠ `BusinessId` is load-bearing and is **not** the TenantId: `DeterministicGuid.ForItem`
needs it, and getting it wrong makes every item id diverge from the web till's, silently.

---

## Phase 1 — Line primitives · ~3–4 days

Small, library-side, fully testable, and each one blocks the money path.

| Step | What | Why it matters |
|---|---|---|
| 5 | `TillStore.EffectivePricePairAsync` — inc **and** ex | The line's VAT rate comes from the price PAIR (C1). Only the inc price exists today. |
| 6 | Expose the item's legacy `TaxId` on `CatalogueItem` | Without it the till cannot tell zero-rated from exempt — the distinction no rate carries. |
| 7 | Build an `IVatBandStore` and wire `VatBandCache` | The cache exists; nothing persists bands on this till. |
| 8 | Move `TenderType` / `SaleChannel` into `SharedKernel` | ⚠ **Drift risk**: they are duplicated per-client today. |

---

## Phase 2 — The money path · ~8–12 days · ⚠ HIGHEST RISK

| Step | What |
|---|---|
| 9 | ⚠ **The big one** — build the basket model and the basket→`IngestSaleRequest` assembler. VAT via `SharedKernel.VatLineMath`, never re-derived. |
| 10 | Repoint item lookup at `TillStore.FindByBarcodeAsync` / `SearchAsync` |
| 11 | Commit through `CommitSaleAsync`; delete the local stock decrement (the server owns stock) |
| 12 | Replace legacy permission gates with `SignedInOperator.Can(...)` |
| 13 | Build `OutboxPushService` + a sync cadence — **neither exists** |
| 14 | Re-signature the receipt off legacy models |

⚠ Everything here is money or VAT. It is the part a shop notices immediately and the part HMRC
sees later. Nothing in this phase should land without tests that state the rule, not the result.

---

## Phase 3 — Returns, parking, reprint · ~5–7 days

| Step | What |
|---|---|
| 15 | Build the sale READ path — **three missing methods** |
| 16 | Wire `SharedKernel.RefundRules` into the return path (the rule landed 2026-08-09; nothing calls it) |
| 17 | ⚠ **Close the server-side refund cap** — BACKEND, MONEY, **needs Matt's decision** |
| 18 | Build parked-basket persistence |

---

## Phase 4 — The operator token · ~2–3 days · ⚠ unblocks five WPs

**Step 19 — does not exist anywhere.** The v2 spine authenticates as a *device*. Five work packages
(WP8 users, WP10 inventory, WP11 reporting, WP12 loyalty, WP13 gift cards) need calls made as the
signed-in *operator*, gated on their `pos.*` permissions. Until this exists they cannot be built,
however finished their screens look.

---

## Phase 5 — Screens, cheapest-first · ~40–50 days

| Step | Work package | Days | Note |
|---|---|---|---|
| 20 | **WP6** Store Information | ~1 | ⚠ **A deletion, not a build.** Best ratio on the list. |
| 21 | WP4/WP16a — delete the obsolete first-run screens, kill the legacy bridge | ~2 | The portal provisions tills now. |
| 22 | WP7a/7b Theming | ~3–4 | |
| 23 | WP9 Cash | ~4–5 | Write half not blocked by the operator token. |
| 24 | WP8 Users screen + two roster cleanups | ~3 | |
| 25 | WP10 Inventory + stock ledger | ~8–10 | ⚠ Largest screen gap. |
| 26 | WP11 Reporting + cross-till lookup | ~8–10 | ⚠ **A rewrite, not a feature** — today's screens query local SQLite and would show one till's data however pretty they looked. |
| 27 | WP12 Loyalty, then WP13 Gift cards | ~12–15 | ⚠ WP12 puts customer PII on a till — GDPR, risk 5. |

---

## The consequence to state out loud

⚠ **The till gets more visibly incomplete before it gets better.** With no bridge, a screen shows
nothing until it is ported. Steps 20–27 will each look like a regression on the way through, and
the first few screen tests after this point will not be a steady improvement. That is the correct
trade — it is what avoids maintaining two schemas and two copies of the catalogue on every till —
but it should not come as a surprise to whoever is doing the testing.
