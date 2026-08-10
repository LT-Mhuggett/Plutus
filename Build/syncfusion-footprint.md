# Syncfusion — what it actually holds up, and what can replace it

**Matt, 2026-08-10:** *"Can Syncfusion be replaced by the reporting we have on the back end or web?"*

**Short answer: for REPORTING, yes — and it already has been.** For the till as a whole, no: three
Syncfusion controls sit on the selling path and one of them is in the checkout.

This page exists because the question is really two questions, and the answers are opposite.

---

## Why it came up

Syncfusion licence keys are **version-specific**. The packages are on **34.1.32**; the key
registered in `App.xaml.cs` predates that. So a licensed control refuses to render and puts a modal
in front of the page — and on `SalesReportsView` that page had no way back, which is how a shop
floor loses a till.

⚠ **A replacement key can only come from Matt's Syncfusion account** (Downloads → Get License Key,
for 34.x). Nothing in this repo can produce or work around one.

---

## The footprint, verified 2026-08-10

| Control | Where | On the selling path? | Replaceable with what |
|---|---|---|---|
| `SfCartesianChart` | `SalesReportsView`, `StockOuttakeView` | ❌ **No** | **Already replaced.** Both screens read the pre-cutover legacy database and are hidden; the till's own figures now come from `GET /api/v1/reports/summary`, and the portal has the full suite |
| `SfCalendar` | the same two screens | ❌ No | Goes with them. A till needs "today", not a date picker |
| `SfListView` (`ListViewWithContextMenu`) | `ViewAllView` — the item list | 🟡 Browsing, not selling | A plain MAUI `CollectionView`. Real work: grouping, sticky headers and the context-menu behaviour are Syncfusion features this screen uses |
| `SfPopup` | `SfListViewContextMenuBehavior` | 🟡 Same screen | Goes with the list. ⚠ Its own positioning maths reads `Button.Width`, which is **-1 before layout** — so it is not a component to port faithfully |
| ⚠ `SfNumericEntry` | **`TillView` — the QUANTITY box** | ✅ **YES** | A MAUI `Entry` with a numeric keyboard and validation. ⚠ **This is on the checkout path**: no quantity, no sale |
| `SfPicker` | `TillView` — the alterations picker | ✅ Yes | `DisplayActionSheet`, which this app already uses for tenders, item search and refund origins |

⚠ **The basket list is NOT Syncfusion** — `TillView` uses a plain MAUI `ListView`. The most critical
screen in the app is already free of it.

---

## So what should happen

**1. Reporting needs nothing from Syncfusion, now or later.** The portal owns real reporting; a till
needs today's takings and an X/Z, which are figures and text. That is what the Statistics tab now
shows, straight from the platform. The two chart screens are hidden and are listed for deletion in
[`legacy-removal.md`](legacy-removal.md) (L4).

**2. Renewing the key is still worth doing, and it is cheap.** Not for charts — for the item list,
the quantity box and the alterations picker, which are all still Syncfusion and all still needed.
⚠ **They are on borrowed time**: they render today, but a licence the library rejects can start
warning at any version bump, and one of them is in the checkout.

**3. Dropping Syncfusion entirely is a real project, not a tidy-up.** Roughly:
- quantity box → `Entry` + validation — ~half a day, ⚠ **on the money path, so it needs a hand-run**
- alterations picker → `DisplayActionSheet` — a couple of hours
- item list → `CollectionView` + grouping + a per-row menu — 2–3 days, and it is the screen most
  likely to regress silently, because **MAUI bindings fail without complaint**

That is ~4 days to remove a paid dependency from a till. Worth doing before a fleet rollout;
not worth doing in the middle of one.

---

## Related

- [`till-design.md`](till-design.md) — the reporting row in Part B, and what the till now reads.
- [`legacy-removal.md`](legacy-removal.md) — L4, the two chart screens.
- [`shop-day-test.md`](shop-day-test.md) — step 5.7 records why the report buttons are gone.
