// Offline support (plan §3.6 + WP2.1): IndexedDB working-set cache + checkout outbox.
// Scope is deliberately "till checkout only" — admin/reports require connectivity.
// v2 (WP2.1, 2026-07-24): the outbox stores the ready-to-send v1 IngestSaleRequest,
// gains a "parked" store for permanently-rejected sales (the client-side dead-letter),
// and hosts the per-device monotonic sale sequence (deviceSeq) — an IndexedDB
// transaction makes the increment atomic even across tabs.

import type { Item, PayMethod, Discount } from "./api.ts";
import type { IngestSaleRequest } from "./pipeline.ts";
import type { ScheduledDiscount } from "./till/scheduledDiscounts.ts";

const DB_NAME = "plutus-till";
// ⚠ v3 (W-P5, 2026-08-17): adds the `cashOutbox` store so cash events survive an outage. ⚠ The bump
// is required BECAUSE a new object store can only be created inside `onupgradeneeded` — the
// `contains` guards below make the upgrade idempotent and leave every existing store untouched.
// ⚠ v4 (multi-barcode, 2026-08-20): adds `aliases`, an item's ADDITIONAL barcodes.
const DB_VERSION = 4;

function openDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, DB_VERSION);
    req.onupgradeneeded = () => {
      const db = req.result;
      if (!db.objectStoreNames.contains("items")) db.createObjectStore("items", { keyPath: "idOne" });
      if (!db.objectStoreNames.contains("meta")) db.createObjectStore("meta");
      if (!db.objectStoreNames.contains("outbox")) db.createObjectStore("outbox", { keyPath: "saleId" });
      if (!db.objectStoreNames.contains("parked")) db.createObjectStore("parked", { keyPath: "saleId" });
      // ⚠⚠ W-P5: cash events queue here when the line is down. A shop opens before its broadband
      // does, and the money moves whether or not the platform hears — so a float that failed to post
      // is a day whose banking cannot be reconciled at all.
      if (!db.objectStoreNames.contains("cashOutbox")) db.createObjectStore("cashOutbox", { keyPath: "eventId" });
      // ⚠⚠ MULTI-BARCODE (2026-08-20): an item may be scanned under more than one code. Keyed on the
      // CODE, so one code cannot point at two items — the same guarantee the MAUI till's local table
      // and the server's unique index give, and for the same reason: an ambiguous scan is
      // unresolvable at a counter.
      //
      // ⚠ A separate STORE rather than an index on `items`: the alias set is small, it is replaced
      // wholesale on every sync, and an index would have needed the alias list to live ON each item
      // row — which would make removal a per-item edit instead of a set replacement.
      if (!db.objectStoreNames.contains("aliases")) db.createObjectStore("aliases", { keyPath: "code" });
    };
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
}

function tx<T>(store: string, mode: IDBTransactionMode, fn: (s: IDBObjectStore) => IDBRequest<T>): Promise<T> {
  return openDb().then(
    (db) =>
      new Promise<T>((resolve, reject) => {
        const t = db.transaction(store, mode);
        const req = fn(t.objectStore(store));
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => reject(req.error);
      }),
  );
}

// ── item catalogue cache ────────────────────────────────────────────────────

export async function cacheItems(items: Item[]): Promise<void> {
  const db = await openDb();
  await new Promise<void>((resolve, reject) => {
    const t = db.transaction("items", "readwrite");
    const s = t.objectStore("items");
    for (const i of items) s.put({ ...i, image: null }); // never cache blobs
    t.oncomplete = () => resolve();
    t.onerror = () => reject(t.error);
  });
}

export const cachedItemById = (id: string): Promise<Item | undefined> =>
  tx("items", "readonly", (s) => s.get(id) as IDBRequest<Item | undefined>);

// ── additional barcodes (multi-barcode, 2026-08-20) ─────────────────────────

/** One row of `GET /api/v1/items/barcodes` — an alias and the item it resolves to. */
export interface ItemAlias {
  code: string;
  itemIdOne: string;
}

/**
 * Replace the cached alias set with the server's.
 *
 * ⚠⚠ CLEARED FIRST, DELIBERATELY. The endpoint returns the WHOLE tenant's aliases, so a
 * put-only update could never remove one — a barcode taken off an item in the portal would go on
 * scanning on this till for ever, with nothing to say so. Replacement is what makes removal work,
 * exactly as the MAUI till replaces an item's rows on every sync.
 *
 * ⚠ One transaction: a cleared store with no rows put back is a till that resolves no aliases at
 * all, which is a visible regression rather than a silent one — but there is no reason to risk it.
 */
export async function cacheAliases(rows: ItemAlias[]): Promise<void> {
  const db = await openDb();
  await new Promise<void>((resolve, reject) => {
    const t = db.transaction("aliases", "readwrite");
    const s = t.objectStore("aliases");
    s.clear();
    for (const r of rows) if (r?.code && r.itemIdOne) s.put({ code: r.code, itemIdOne: r.itemIdOne });
    t.oncomplete = () => resolve();
    t.onerror = () => reject(t.error);
  });
}

/**
 * The item an ADDITIONAL barcode belongs to, from the cache.
 *
 * ⚠⚠ RETURNS THE CANONICAL ITEM. The alias string stops here (multi-barcode plan D2): if it
 * travelled onto a basket line, `StockProjectionConsumer` would create a phantom `StockLevel` and
 * `VatBandStamp` would leave the sale line's VAT band null — both silently.
 *
 * ⚠ Exact match, no case folding: an IndexedDB key lookup is case-sensitive, and so is the item
 * lookup beside it. An alias must not match where the item's own barcode would not.
 */
export async function cachedItemByAlias(code: string): Promise<Item | undefined> {
  const alias = await tx("aliases", "readonly", (s) => s.get(code) as IDBRequest<ItemAlias | undefined>);
  return alias ? cachedItemById(alias.itemIdOne) : undefined;
}

/** Mirror of the server's ItemParameters.Tokenise (keep in sync). Word mode: quoted
 *  segments are literal-phrase tokens (unclosed quote runs to end), the rest splits on
 *  whitespace. Phrase mode: whole input (quotes stripped) is one token. */
export function searchTokens(term: string, matchAllWords: boolean): string[] {
  const lower = term.toLowerCase();
  if (!matchAllWords) {
    const phrase = lower.replace(/"/g, "").trim();
    return phrase ? [phrase] : [];
  }
  const tokens: string[] = [];
  lower.split('"').forEach((part, i) => {
    if (i % 2 === 1) {
      const phrase = part.trim();
      if (phrase) tokens.push(phrase);
    } else {
      tokens.push(...part.split(/\s+/).filter(Boolean));
    }
  });
  return tokens;
}

export async function cachedItemSearch(term: string, limit = 8, matchAllWords = false): Promise<Item[]> {
  const all = await tx("items", "readonly", (s) => s.getAll() as IDBRequest<Item[]>);
  const tokens = searchTokens(term, matchAllWords);
  return all
    // ⚠ THREE FIELDS, and additional barcodes are deliberately NOT among them (multi-barcode plan
    // D9). An alias resolves on the EXACT-scan path, which runs before search on both tills; adding
    // it to substring search would mean moving four implementations in lockstep — `ItemSearch`,
    // `ItemParameters`, `TillStore.SearchAsync` and this — which is its own slice, not a free extra.
    .filter((i) => tokens.every((q) => i.name.toLowerCase().includes(q) || i.idOne.toLowerCase().includes(q) || i.brand.toLowerCase().includes(q)))
    .slice(0, limit);
}

export const cachedItemCount = (): Promise<number> => tx("items", "readonly", (s) => s.count());

// ── reference data (pay methods, discounts, discount rules) ─────────────────

/** The reference-data keys. ⚠ A CLOSED UNION so a typo is a compile error rather than a cache that
 *  silently never hits — which on the offline path would read as "this shop has no discounts". */
export type MetaKey = "payMethods" | "discounts" | "discountRules";

export const cacheMeta = (key: MetaKey, value: PayMethod[] | Discount[] | ScheduledDiscount[]) =>
  tx("meta", "readwrite", (s) => s.put(value, key)).then(() => undefined);

export const cachedMeta = <T>(key: MetaKey): Promise<T | undefined> =>
  tx("meta", "readonly", (s) => s.get(key) as IDBRequest<T | undefined>);

// ── durable till state (§5b) ────────────────────────────────────────────────
//
// ⚠ SEPARATE FROM `cacheMeta` ON PURPOSE. That pair is typed to the two reference-data keys and
// its value type is `PayMethod[] | Discount[]`; widening it would make every caller's type
// meaningless. This pair carries arbitrary state blobs, keyed by a closed union so a typo is a
// compile error rather than a silent miss.
//
// ⚠ Same `meta` object store, so no DB version bump is needed — the store already exists.
export type TillStateKey =
  /** W-P1: set once the platform has explicitly revoked this device. Survives reload BY DESIGN —
   *  a revoked till must not come back by pressing F5. */
  | "deviceRevoked"
  /** W-P2: the whole `TillOperatorsResult` envelope, verbatim. ⚠ The envelope, not rows —
   *  `asOfUtc` is roster-level and is the SERVER's clock, which W-P4's staleness horizons are
   *  measured from. */
  | "operatorRoster"
  /** W-P5: the platform's own words on the last terminally-refused cash event, so the Cash screen
   *  can show it without re-reading the whole outbox. */
  | "lastCashRefusal";

export const putTillState = <T>(key: TillStateKey, value: T): Promise<void> =>
  tx("meta", "readwrite", (s) => s.put(value, key)).then(() => undefined);

export const getTillState = <T>(key: TillStateKey): Promise<T | undefined> =>
  tx("meta", "readonly", (s) => s.get(key) as IDBRequest<T | undefined>);

export const clearTillState = (key: TillStateKey): Promise<void> =>
  tx("meta", "readwrite", (s) => s.delete(key)).then(() => undefined);

// ── device sale sequence (WP2.1) ────────────────────────────────────────────

/** Next per-device monotonic sale sequence. get+put in ONE readwrite transaction —
 *  IndexedDB serialises those across tabs, so two tills in two tabs cannot collide. */
export function nextDeviceSeq(): Promise<number> {
  return openDb().then(
    (db) =>
      new Promise<number>((resolve, reject) => {
        const t = db.transaction("meta", "readwrite");
        const s = t.objectStore("meta");
        const get = s.get("deviceSeq");
        get.onsuccess = () => {
          const next = (typeof get.result === "number" ? get.result : 0) + 1;
          s.put(next, "deviceSeq");
          t.oncomplete = () => resolve(next);
        };
        t.onerror = () => reject(t.error);
      }),
  );
}

/** Reset on (re-)enrolment: a fresh deviceId starts its sequence from 1. */
export const resetDeviceSeq = () => tx("meta", "readwrite", (s) => s.delete("deviceSeq")).then(() => undefined);

// ── checkout outbox (WP2.1: stores the ready-to-send v1 request) ────────────

export interface QueuedSale {
  saleId: string;
  request: IngestSaleRequest;
  queuedAt: string;
}

/** A permanently-rejected sale, kept for inspection (client-side dead-letter). */
export interface ParkedSale extends QueuedSale {
  reason: string;
  parkedAt: string;
}

export const queueSale = (q: QueuedSale) => tx("outbox", "readwrite", (s) => s.put(q)).then(() => undefined);
export const queuedSales = (): Promise<QueuedSale[]> => tx("outbox", "readonly", (s) => s.getAll() as IDBRequest<QueuedSale[]>);

/**
 * W-P5: how many sales for one business day are still waiting to be sent.
 *
 * ⚠⚠ THE Z CLOSE WAITS ON THIS. The platform's expected drawer is float + **cash takings** + ins − outs,
 * so a Z that overtakes queued sales reports a shortage equal to every sale still waiting — a till that
 * traded £400 through an outage would tell the person who counted it correctly that they were £400
 * down. ⚠ And because the Z is terminal server-side, it would then refuse those very sales, putting the
 * day's real takings into quarantine behind their own close.
 *
 * ⚠ PENDING ONLY, never parked/refused: a permanently-rejected sale would block this till's close for
 * ever. It counts the `outbox` store, which is exactly "not yet accepted".
 *
 * ⚠ Per business DAY, so one stuck sale from last week cannot block every close from now on.
 */
export const pendingSalesForDay = async (day: string): Promise<number> =>
  (await queuedSales()).filter((s) => s.request?.businessDay === day).length;
export const removeQueued = (saleId: string) => tx("outbox", "readwrite", (s) => s.delete(saleId)).then(() => undefined);
export const queuedCount = (): Promise<number> => tx("outbox", "readonly", (s) => s.count());

export const parkSale = (p: ParkedSale) => tx("parked", "readwrite", (s) => s.put(p)).then(() => undefined);
export const parkedSales = (): Promise<ParkedSale[]> => tx("parked", "readonly", (s) => s.getAll() as IDBRequest<ParkedSale[]>);
export const parkedCount = (): Promise<number> => tx("parked", "readonly", (s) => s.count());
