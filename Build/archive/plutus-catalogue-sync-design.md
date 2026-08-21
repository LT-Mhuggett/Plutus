# Plutus — Catalogue Management & Multi-Till Sync Design

> ## 📦 ARCHIVED 2026-08-20 — a GREENFIELD DESIGN STUDY. Read it for the reasoning, **never as a description of this schema**
>
> ⚠⚠ **THIS DOCUMENT DESCRIBES A DATABASE PLUTUS DOES NOT HAVE, AND ITS CENTRAL RECOMMENDATION WAS
> DELIBERATELY REJECTED.** It is kept because it is the study that produced the multi-barcode work
> (Matt read it, then asked for the plan) and because its *principles* are sound. But §3.1 proposes a
> schema that is not this one, and somebody implementing from it would break the platform.
>
> ### What was ADOPTED
>
> | This document's idea | Where it lives now |
> |---|---|
> | **Barcodes are aliases that resolve to an item** — several codes, one item | `ItemBarcode` (2026-08-20) · [`Multi-barcode plan.md`](../archive/Multi-barcode%20plan.md) |
> | **Both codes resolve during a supplier changeover** — the argument for the whole feature | Live, and it is the wording used in the portal's own help text |
> | **Sale lines snapshot the item** so history is immune to later edits | Live — `SaleLine` + `LineMeta` |
> | **Change log + keyset delta pull + idempotent till-side apply** | Live — `GET /api/v1/catalogue/changes`, cursor over `(ModifiedAt, IdOne)` |
> | **Single writer: the back office; tills hold read-only replicas** | Live, and now a ruling — [`till-design.md`](../till-design.md) C1 *"Portal decides, till obeys"* |
> | **Tills must sell with no network** | Live on both tills |
>
> ### ⚠⚠ What was REJECTED, and why it was never available
>
> **§3.1's `item_id UUID PRIMARY KEY` with barcode demoted to an alias row is the opposite of what
> Plutus does, and cannot be adopted.** In this platform **`Item.IdOne` IS the barcode AND the
> identity**:
>
> - it seeds `DeterministicGuid.ForItem` — a **frozen golden vector** with a TypeScript twin, so
>   changing it changes every item GUID ever derived;
> - it is half a **composite primary key** with five FK families hanging off it;
> - it is on **every historical sale line** — 74,830 of them in the Kapow data alone.
>
> Re-keying was therefore never a migration; it was a rewrite of the sale history. So this document's
> opening claim — *"the single most common design mistake in POS databases is using the barcode as the
> primary key"* — describes **exactly what Plutus does, knowingly**. The multi-barcode work took the
> *behaviour* (many codes resolve to one item) without the *re-keying*, which is why aliases are
> additive rows and the alias string is forbidden from travelling past resolution.
>
> Also **not** adopted, each for a stated reason:
>
> - **`barcode TEXT PRIMARY KEY` (globally unique)** — Plutus is **multi-tenant**, and this document is
>   single-tenant throughout. The real index is unique on **`(TenantId, Code)`**. A global PK would let
>   one shop's code collide with another's.
> - **`pack_qty` / case barcodes** ("scanning sells N units") — **not built**, and not asked for. Do not
>   assume it exists.
> - **`item_price` effective-dated rows** — Plutus has a price timeline, but not this shape; the live
>   rules are in [`till-design.md`](../till-design.md) C1, not here.
> - **§6.3 maker-checker approval** — still unbuilt and still optional, as this document says.
> - **§4.1's WebSocket/SSE nudge** — not built; both tills poll on a cadence.
>
> ⚠ **Nothing in here is outstanding work.** It was never a plan with packages; it is the reasoning
> behind one. The plan it produced is archived beside it.

**Status:** ⛔ Superseded as a schema proposal · Draft v1 · August 2026 — **archived 2026-08-20**
**Scope:** How stock item master data (barcodes, descriptions, prices, tax rates, etc.) is created, changed, approved, and propagated to tills in a multi-till Plutus deployment.

---

## 1. Design Principles

These five principles drive every decision in this document. If a future feature request conflicts with one of them, treat that as a red flag.

1. **Single source of truth.** The back office (cloud/server database) is the *only* authoritative store of item master data. Tills hold read-only replicas.
2. **Single writer.** Item master data is only ever edited through the back office. Tills never mutate the catalogue. This eliminates conflict resolution entirely — there is nothing to merge because only one place can write.
3. **Tills must work offline.** Every till holds a complete local copy of the catalogue and can scan, price, and sell with no network. Sync is a catch-up mechanism, not a dependency of selling.
4. **History is immutable.** Completed transactions are never rewritten by later catalogue edits. Sale lines carry a snapshot of the item as it was at the moment of sale.
5. **Identity is internal and permanent.** Items are identified by an internal immutable ID. Barcodes, PLUs, and SKU codes are mutable *aliases* that point at an item — never the item's identity.

---

## 2. The Core Model: Master → Replica

```
                ┌─────────────────────────┐
                │   Back Office Portal    │
                │  (authoritative store)  │
                │                         │
                │  items / barcodes /     │
                │  prices / tax / etc.    │
                │           │             │
                │     catalogue_change    │
                │      (append-only)      │
                └───────────┬─────────────┘
                            │
              delta sync (pull, seq-based)
              + push nudge (WebSocket/SSE)
                            │
        ┌─────────────┬─────┴───────┬─────────────┐
        ▼             ▼             ▼             ▼
   ┌─────────┐   ┌─────────┐   ┌─────────┐   ┌─────────┐
   │ Till 1  │   │ Till 2  │   │ Till 3  │   │ Till N  │
   │ local   │   │ local   │   │ local   │   │ local   │
   │ replica │   │ replica │   │ replica │   │ replica │
   └─────────┘   └─────────┘   └─────────┘   └─────────┘
```

Every serious POS system — from legacy chain retail (item maintenance batch files) to modern cloud POS (Square, Lightspeed, Shopify POS) — uses this shape. The differences are only in *latency* (nightly batch vs. near-real-time) and *transport*. For a new build in 2026, build near-real-time; the same delta mechanism doubles as your offline catch-up path for free.

### What flows down to tills

Everything a till needs to sell without network:

- Items (description, department/category, tax code, flags like age-restriction)
- Barcodes/aliases
- Prices (including *future-dated* prices — see §6)
- Tax rates, departments, promotions
- Operator accounts and permissions

### What flows up from tills

Only transactional data: sales, refunds, no-sales, cash movements, stock adjustments *as events* (e.g. "sold 2 of item X"), and operational telemetry. Tills report facts; they never edit master data.

---

## 3. Data Model

### 3.1 Items — identity vs. aliases

The single most common design mistake in POS databases is using the barcode as the primary key. Barcodes get reused by suppliers, change with packaging redesigns, and one product legitimately has several (single unit vs. case, old vs. new supplier code). Model them as a separate alias table:

```sql
CREATE TABLE item (
    item_id         UUID PRIMARY KEY,          -- immutable, internal, never reused
    description     TEXT NOT NULL,             -- till/receipt display name
    long_description TEXT,                     -- back-office / label name
    department_id   UUID NOT NULL REFERENCES department,
    tax_code_id     UUID NOT NULL REFERENCES tax_code,
    flags           JSONB NOT NULL DEFAULT '{}',  -- age_restricted, weighed, open_price...
    is_active       BOOLEAN NOT NULL DEFAULT TRUE, -- soft delete / delist
    created_at      TIMESTAMPTZ NOT NULL,
    updated_at      TIMESTAMPTZ NOT NULL
);

CREATE TABLE item_barcode (
    barcode         TEXT PRIMARY KEY,          -- the scanned string (EAN-13, UPC-A, etc.)
    item_id         UUID NOT NULL REFERENCES item,
    pack_qty        NUMERIC NOT NULL DEFAULT 1, -- case barcode support: scanning sells N units
    is_active       BOOLEAN NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMPTZ NOT NULL,
    updated_at      TIMESTAMPTZ NOT NULL
);
```

Consequences of this shape:

- **"Changing a barcode" is not a mutation of the item.** It's deactivating one alias row and inserting another. Both operations are trivially syncable and auditable, and during transition *both* barcodes can resolve to the item — very handy when a supplier changes codes and you still have old stock on the shelf.
- **Barcode uniqueness is enforced globally** (it's the PK). A scan is a single indexed lookup on the till's local replica.
- **Description changes are free.** Because sale lines snapshot the description (§7), you can rename an item without any effect on history.

### 3.2 Prices as first-class, effective-dated rows

Don't store `price` as a column on `item`. Store price *events*:

```sql
CREATE TABLE item_price (
    price_id        UUID PRIMARY KEY,
    item_id         UUID NOT NULL REFERENCES item,
    price           NUMERIC(10,2) NOT NULL,
    price_list_id   UUID NOT NULL DEFAULT default_list, -- future: per-site/per-channel
    effective_from  TIMESTAMPTZ NOT NULL,     -- may be in the future
    effective_to    TIMESTAMPTZ,              -- NULL = open-ended; set for promos
    created_by      UUID NOT NULL REFERENCES back_office_user,
    created_at      TIMESTAMPTZ NOT NULL
);
```

The price of an item at any moment is: *the row with the latest `effective_from` ≤ now that hasn't expired.* This one table gives you scheduled price changes, time-boxed promotions, price history for reporting, and a clean audit trail — and it's the foundation of the "queue changes and release them" behaviour you asked about (§6), without needing a separate queuing subsystem.

### 3.3 The change log — the heart of sync

Every committed change to any catalogue entity appends a row to a single append-only log with a **monotonic global sequence number**:

```sql
CREATE TABLE catalogue_change (
    seq             BIGSERIAL PRIMARY KEY,     -- the sync cursor
    entity_type     TEXT NOT NULL,             -- 'item' | 'item_barcode' | 'item_price' | ...
    entity_id       TEXT NOT NULL,
    op              TEXT NOT NULL,             -- 'upsert' | 'delete'
    payload         JSONB,                     -- full current row for upserts; NULL for deletes
    changed_by      UUID,                      -- back office user (audit)
    changed_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
```

Notes on this design:

- **Full-row payloads, not diffs.** Each upsert carries the entire current state of the entity. This makes application on the till idempotent and order-tolerant *per entity*: applying change 500 for item X when you've already applied change 512 for item X can be guarded by comparing sequence numbers, and re-applying the same change twice is harmless.
- **Tombstones, not hard deletes.** A delete is an `op='delete'` row (and `is_active=false` on the master row). A till that was in a cupboard for three weeks must be able to learn that an item was removed; if you hard-delete, there's nothing to sync.
- **Write it in the same transaction** as the change to the master table (or use CDC/logical decoding later). If the item update commits but the change-log row doesn't, that till never hears about it — this must be atomic.
- **Compaction (optional, later):** you only ever need the *latest* log row per entity for catch-up, so old superseded rows can be pruned once all tills are past them. Don't build this on day one.

### 3.4 Till sync state

```sql
CREATE TABLE till (
    till_id         UUID PRIMARY KEY,
    site_id         UUID NOT NULL,
    name            TEXT NOT NULL,
    last_acked_seq  BIGINT NOT NULL DEFAULT 0, -- server-side visibility of till progress
    last_seen_at    TIMESTAMPTZ
);
```

Each till also stores its own cursor locally (`last_applied_seq`). The server-side copy is for monitoring — your back office portal should show a **fleet sync dashboard**: every till, its last-seen time, and how far behind the head of the change log it is. When a shop manager says "Till 3 is showing the old price", this table answers the question in one glance.

---

## 4. The Sync Protocol

### 4.1 Transport: pull for transfer, push for latency

Use both, with pull as the foundation:

- **Pull (the workhorse):** the till periodically calls
  `GET /sync/catalogue?since={last_applied_seq}&limit=500`
  and receives an ordered batch of change-log rows plus the current head sequence. It applies them and advances its cursor. Poll every 30–60 seconds. This alone is a complete, correct sync system — simple, resumable, firewall-friendly, and it *is* your offline recovery path.
- **Push (the accelerator):** the till holds a WebSocket/SSE connection; when the change log advances, the server sends a content-free nudge ("head is now 48,213"). The till responds by doing a normal pull. **The push channel never carries data** — it only shortens the poll interval to near-zero. If the socket dies, behaviour degrades gracefully to the poll interval and nothing breaks.

This "notify-then-pull" pattern is worth being disciplined about: it means you have exactly one code path that transfers and applies data, exercised constantly, instead of a fast path that works and a rarely-used recovery path that has rotted.

### 4.2 Applying a batch on the till

```
1. Receive batch [seq 48101..48213]
2. BEGIN local transaction
3. For each change: upsert/tombstone into local replica tables
4. Set local last_applied_seq = 48213
5. COMMIT
6. ACK to server (updates till.last_acked_seq)
```

Rules:

- **Atomic per batch.** The local cursor and the data move together in one transaction. A crash mid-batch leaves the till at the old cursor and it simply re-pulls — idempotent application makes the retry safe.
- **Never disturb an open basket.** Applying catalogue changes must not alter lines already rung into a transaction in progress. Because sale lines snapshot their data at scan time (§7), this falls out naturally — a mid-basket price change affects the *next* scan of that item, not lines already on the receipt. That is also the correct and legally safest customer-facing behaviour.
- **New/cold till bootstrap:** a brand-new till (or one wiped for repair) does a **full snapshot download** (`GET /sync/catalogue/snapshot` → current state of all entities + the head seq as its starting cursor), then joins normal delta sync. Don't make new tills replay the entire historical log.

### 4.3 Failure modes and how this design handles them

| Failure | Behaviour |
|---|---|
| Till offline for hours/days | On reconnect it pulls the delta from its cursor. Nothing special happens. |
| Till offline for weeks, log compacted past its cursor | Server responds "cursor too old" → till does a snapshot re-bootstrap. |
| Crash mid-apply | Cursor didn't advance (atomic batch); till re-pulls, idempotent re-apply. |
| Push socket down | Poll interval becomes the worst-case latency. Selling unaffected. |
| Back office down entirely | Tills keep selling from local replica; queued upstream sales transmit later. |
| Two back-office users edit the same item | Resolved at the master DB with normal transactions/optimistic locking (§5); tills just see the final state. |

---

## 5. Editing, Concurrency & Audit in the Back Office

Because tills never write master data, all concurrency questions collapse into ordinary web-app concerns at the back office:

- **Optimistic locking** on edit forms: forms carry the item's `updated_at`/version; a stale save returns "this item was changed by Sarah 2 minutes ago" rather than silently overwriting.
- **Role-based permissions:** who may edit descriptions vs. prices vs. tax codes. Tax code changes in particular deserve a tighter permission — getting VAT treatment wrong has consequences beyond a mispriced can of beans.
- **Audit log on everything:** who, when, entity, field, old value → new value. The `catalogue_change` log gives you most of this for free (`changed_by`, `changed_at`, payload); add old-value capture either via a trigger or by logging the diff alongside. In practice a good audit trail removes most of the demand for approval workflows (§6.3) — people are careful when changes are attributable, and mistakes are diagnosable and reversible.

---

## 6. "Do I need a queue-and-approve section?" — Three separate features

Your instinct points at something real, but it bundles three different features. Separate them and build them in this order.

### 6.1 Immediate apply (the default) — build first

Operator fixes a typo in a description → saves → change-log row written → tills have it within seconds. No ceremony. This is what every modern cloud POS does by default, and for a huge share of edits (descriptions, new barcodes, new items) any added friction is pure cost. **The propagation mechanism (§3.3/§4) already is your "push out and overwrite" system** — you don't need a separate one; every save is a queued change that tills consume in order.

### 6.2 Scheduled / effective-dated changes — build second; this is the valuable "queue"

The genuinely useful version of "queue changes for release" is **effective dating**, and the schema in §3.2 already provides it for prices:

- Friday afternoon: manager keys Monday's promo prices with `effective_from = Monday 00:00`.
- The rows sync to every till *immediately* — as pending data.
- At Monday 00:00 each till starts resolving the new price **locally**, even if it's offline at that moment, because the pending row is already sitting in its replica.

This is strictly better than a server-side "release button pressed at midnight": no one has to be awake, and offline tills switch over on time anyway. The same pattern extends later to scheduled description/packaging changes or scheduled delisting (`effective_from` on an `item_revision` table) — but prices are 95% of the real-world demand, so start there.

Add a back-office **"Upcoming changes"** view: everything effective-dated in the future, with the ability to amend or cancel before it goes live (cancelling = tombstoning the pending price row, which syncs like any other change). This gives you the *reviewability* you were reaching for with an approval queue, without gating day-to-day edits.

### 6.3 Maker-checker approval — optional, later, permission-gated

Some larger operators want a junior's edits to sit in `pending_approval` until a manager approves. Legitimate, but:

- It's an **optional workflow layer above the master DB**, not part of the sync architecture. A pending change simply *hasn't been written to the master tables yet*, so nothing syncs until approval. Model it as a `change_request` table (proposed payload, requester, status, reviewer, decided_at); approval executes the write, which flows through the normal change log.
- Make it a **per-role toggle** ("edits by role X require approval"), off by default. Small operators will hate mandatory approval of their own changes.
- Ship it only when a customer segment actually asks. The audit log (§5) plus the upcoming-changes view (§6.2) covers most of the governance need.

**Summary answer to your question:** you don't need an approval queue as the core mechanism. You need (a) an append-only change log with sequence numbers as the propagation mechanism, (b) effective dating for prices as the scheduled-release mechanism, and (c) maker-checker as an optional permission layer if/when you move upmarket.

---

## 7. Transaction Immutability: Snapshot Sale Lines

When a till rings an item, denormalise the sellable facts into the sale line at scan time:

```sql
CREATE TABLE sale_line (
    sale_line_id    UUID PRIMARY KEY,
    sale_id         UUID NOT NULL REFERENCES sale,
    item_id         UUID NOT NULL,             -- reference for reporting/joins
    barcode_scanned TEXT,                      -- which alias was actually scanned
    description     TEXT NOT NULL,             -- ← snapshot
    unit_price      NUMERIC(10,2) NOT NULL,    -- ← snapshot (post price-resolution)
    tax_rate        NUMERIC(6,4) NOT NULL,     -- ← snapshot
    qty             NUMERIC NOT NULL,
    line_total      NUMERIC(10,2) NOT NULL
);
```

Why this matters more than it first appears:

- **Receipts are documents.** If a customer disputes a charge or a refund is processed weeks later, the receipt must reflect what was actually shown and charged — not what the item is called today.
- **Tax is a legal snapshot.** If the VAT rate or an item's tax code changes, historical transactions must keep the rate that applied at sale time. Recomputing tax on old sales from current master data is an accounting error waiting to happen.
- **Refund by receipt** works by reading the sale line, not by re-looking-up the item — so refunds are correct even if the item was since renamed, repriced, or delisted.
- **Reporting still joins on `item_id`** for "sales by product over time" regardless of renames — the immutable internal ID is what makes both properties (stable analytics + immutable documents) hold at once.

Also record `barcode_scanned`: when a supplier barcode migration goes wrong, knowing *which alias* tills actually scanned is gold for debugging.

---

## 8. Operational Details Worth Getting Right Early

**Clock discipline.** Effective dating (§6.2) makes till clocks matter. Run NTP on tills, have the sync response include server time, and have tills alert (and log) if local clock skew exceeds a threshold (e.g. 60s). A till with a wrong clock switching promo prices early/late is a subtle, real-world bug class.

**Sync health monitoring.** Surface `till.last_seen_at` and lag (`head_seq - last_acked_seq`) prominently in the portal, with alerts for tills silent > N minutes or lagging > N changes. Most "the price is wrong on till 3" support calls are actually "till 3 hasn't synced since Tuesday", and this dashboard turns a mystery into a one-glance diagnosis.

**Batch/bulk changes.** Supplier price files and range resets arrive as spreadsheets of hundreds of changes. Build CSV import in the portal early, with a **preview/validate step** (what will change, highlighted deltas, rejected rows and why) before commit. The commit is just N ordinary writes → N change-log rows; sync needs nothing special. This preview step, incidentally, delivers much of the "review before push" feeling you wanted — at the point of bulk entry, where review is actually useful.

**Weighed & open-price items.** Weight-embedded barcodes (price/weight encoded in the EAN, common in UK retail — prefix 20–29) need the till's barcode resolver to parse the code and compute the line locally. This is a till-side scan-path feature; it doesn't change the sync design, but design the `item_barcode` resolver as a pipeline (exact match → weight-embedded pattern → fallback) from the start so it slots in.

**Multi-site (future).** The `price_list_id` in §3.2 and `site_id` on tills are deliberate seams: per-site pricing later becomes "which price list does this site's tills subscribe to" and sync filtering becomes "changes relevant to this till's subscriptions", without reshaping the core. Don't build multi-site now; just don't paint over these seams.

**What NOT to build:** peer-to-peer till sync (tills syncing catalogues from each other), tills editing master data with merge logic, or a bespoke message broker on day one. Postgres + an HTTP delta endpoint + one WebSocket nudge channel is a complete, robust v1.

---

## 9. Worked Examples

**Changing a barcode** (supplier switched codes on a product):
1. Back office: on the item's barcode list, add new barcode `5012345678900`; optionally leave the old one active during stock transition, deactivate it later.
2. Two rows append to `catalogue_change` (upsert new alias; later, upsert old alias with `is_active=false`).
3. Tills nudged, pull, apply. Within seconds both codes scan to the item; after the old alias is deactivated, only the new one does.
4. Old sale lines are untouched — they carry `barcode_scanned` and their own snapshots.

**Changing a description:**
1. Edit → save (optimistic-lock checked) → one change-log row → tills within seconds.
2. Receipts printed before the change keep the old text; new scans show the new text. Reporting by `item_id` spans the rename seamlessly.

**Scheduled price change:**
1. Friday: new `item_price` rows keyed with `effective_from = Monday 00:00`. Visible in "Upcoming changes"; amendable/cancellable until then.
2. Rows sync to all tills immediately as pending data.
3. Monday 00:00: every till — online or offline — resolves the new price locally. No release step, no one awake.

**Delisting an item:**
1. Back office: mark inactive (soft delete). Tombstone syncs.
2. Tills stop resolving its barcodes; historical sales, reporting, and refund-by-receipt continue to work forever via `item_id` + snapshots.

---

## 10. Build Order Recommendation

1. **Core master schema** — `item`, `item_barcode`, `item_price` (effective-dated from day one), departments, tax codes.
2. **Change log + delta pull endpoint + till-side apply loop** — this is the whole sync system; get it solid and idempotent.
3. **Snapshot sale lines** on the till transaction path.
4. **Snapshot bootstrap** for new tills + fleet sync dashboard in the portal.
5. **WebSocket/SSE nudge** for near-real-time latency.
6. **Upcoming-changes view** + CSV bulk import with preview.
7. *(Later, on demand)* maker-checker approvals; multi-site price lists; log compaction.

Steps 1–3 are the architecture; everything after is additive. Nothing in 4–7 requires reworking 1–3 — which is the sign the core shape is right.
