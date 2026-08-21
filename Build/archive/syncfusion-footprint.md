# Syncfusion — what it held up, and what replaced it

> ## 📦 ARCHIVED 2026-08-20 — merged into [`To do/Shrink MAUI Build.md`](../To%20do/Shrink%20MAUI%20Build.md) **§4**
>
> **Two documents were describing the same 76 MB from two ends** — this one *"what the controls held up
> and what replaced them"*, the other *"how big it is and the order to remove it"* — and the removal
> order in §4.4 depends entirely on the analysis in here. Reading one without the other was the
> failure mode: **the licence/trial-dialog hazard is the reason the order is load-bearing**, and it
> lived in this document while the order lived in the other.
>
> Everything here is now in that document, nothing dropped:
>
> | This document's section | Now |
> |---|---|
> | *Why it came up* — version-specific keys, no key coming, the modal with no way back | **§4.0**, where it explains why §4.4's order matters |
> | *What was removed, and what replaced it* — the swap table and what each swap cost | **§4.5** |
> | *What is still left* | **§4.1** and the tiers in **§4.3** |
> | *What to watch on the next hand-run* | **§4.6**, cross-referenced to [`Test Maui.md`](../Test%20Maui.md) |
>
> ⚠ **The one line worth carrying even if you read nothing else:** `SfNumericEntry`'s `Minimum="1"`
> lived in the **control's markup**, so it left with the control — and a quantity of 0 rings a basket
> line that charges nothing and looks exactly like a sale. **A rule that lives in a control's markup
> leaves with the control.** That is the hazard for the rest of tier C, not the megabytes.
>
> ⚠ **It moved to `To do/` rather than staying here** because the work it describes is **unbuilt** —
> tier C is blocked by L4's ruling. The folder rule decides. When the removal is done, the merged
> document archives as one.

**Matt, 2026-08-10:** *"Can Syncfusion be replaced by the reporting we have on the back end or web?"*
then, on reading the answer: **"I am not going to renew Syncfusion, it seems like it can be
replaced."**

**Status: as of till 1.33.0, NO Syncfusion control is on any screen an operator can reach.**
What is left is the two hidden legacy report screens and the spreadsheet export one of them uses.

---

## Why it came up

Syncfusion licence keys are **version-specific**. The packages are on **34.1.32**; the key
registered in `App.xaml.cs` predates that. A licensed control that renders therefore puts a modal in
front of the page — and on `SalesReportsView` that page had no way back, which is how a shop floor
loses a till.

⚠ **There is no key coming.** Matt has decided not to renew, so the fix is removal, not renewal.

---

## What was removed, and what replaced it

| Was | Where | Now | ⚠ What the swap cost |
|---|---|---|---|
| `SfNumericEntry` | **`TillView` — the QUANTITY box**, the selling path | `Entry` + explicit − / + buttons | ⚠ `Minimum="1"` lived in the CONTROL'S MARKUP. It now lives in `TillViewModel.Quantity`'s setter — a quantity of 0 adds a basket line that charges nothing and looks exactly like a sale. **A rule that lives in a control's markup leaves with the control.** Digits-only is enforced in `Quantity_TextChanged`; an unparseable string would otherwise leave the binding showing one number and the viewmodel holding another |
| `SfPicker` | `TillView` — the alterations picker | `DisplayActionSheet` | The empty-list guard is KEPT even though an action sheet cannot repeat the `SelectedIndex == 0` crash — an empty sheet is still a dead end. ⚠ `IsBusy` is released before dispatching, or the discount silently does nothing |
| `SfListView` + `SfPopup` context menu | `ViewAllView` — the item list | `CollectionView` (`IsGrouped`) | Grouping moved into the VIEWMODEL (`ItemGroups`), which deletes the crash-on-open class of bug — there is no longer a shared mutable `DataSource` between view and viewmodel to get the assignment order wrong on. ⚠ The column header is now a fixed Grid ABOVE the list, so it no longer scrolls away. ⚠ `SelectedItem` is cleared on every tap: a `CollectionView` will not raise SelectionChanged twice for the same row, so without it the second tap does nothing and reads as a freeze. The right-click menu is gone — replaced by the **visible per-row Edit button** |
| `SfListView` (multi-select) | `InputMultiSelectAlert` — which basket lines a discount applies to | `CollectionView`, `SelectionMode.Multiple` | ⚠ `Header`/`Footer` take VIEWS, not DataTemplates — handing them a template renders nothing, silently. ⚠ `SelectedItems` must be **assigned**, not mutated: the notification comes from the setter, so adding in place updates the count and leaves the rows unhighlighted. The footer scrolls rather than sticking; on a handful of basket lines, nobody will see it |
| `SfCartesianChart`, `SfCalendar` | `SalesReportsView`, `StockOuttakeView` | **Already replaced** — both screens read the pre-cutover legacy database and are hidden; the till's figures come from `GET /api/v1/reports/summary` and the portal has the full suite | — |

⚠ **The basket list was never Syncfusion** — `TillView` uses a plain MAUI `ListView`. The most
critical screen in the app was already free of it.

---

## What is still left

| Thing | Where | Goes when |
|---|---|---|
| The two chart screens | `SalesReportsView`, `StockOuttakeView` | They are already HIDDEN and listed for deletion in [`MAUI-retrofit.md`](To%20do/MAUI-retrofit.md) §10 **L4** |
| `XlsIO` spreadsheet export | `Helpers/FileIO/ExcelHandling.cs` | Used **only** by `SalesReportsViewModel`, so it goes with L4 |
| `RegisterLicense` + `ConfigureSyncfusionCore` + the package references | `App.xaml.cs`, `MauiProgram.cs`, the csproj | **Last.** ⚠ Removing the registration while a licensed control still exists in the assembly is *worse*, not better: it turns a dormant screen into a trial-dialog screen |

⚠ **Do not add a Syncfusion control to a live screen.** There is no key that will license it, and
the failure is a modal on the shop floor, not a build error. `App.xaml.cs` carries the same warning.

---

## What to watch on the next hand-run

Ranked by how likely and how much it matters — this is a UI swap on screens with **no automated
coverage**, because that needs a running UI host and this repo has none.

1. **The quantity box** — it is on the money path. Type `3`, scan, check three went in; then type
   `0` and check it becomes 1 rather than ringing a free line.
2. **The item list** — grouping, the A–Z headers, search narrowing, and tapping the SAME row twice.
3. **The discount multi-select** — Select all / Unselect all, and that the chosen lines are the ones
   that get the discount.
4. **The alterations sheet** — that picking one actually opens the amount prompt.

---

## Related

- [`till-design.md`](till-design.md) — the printing and item-edit rows in Part B, and C1/C2.
- [`MAUI-retrofit.md`](To%20do/MAUI-retrofit.md) §10 — L4, the two chart screens and the export.
- [`shop-day-test.md`](shop-day-test.md) — the hand-run.
