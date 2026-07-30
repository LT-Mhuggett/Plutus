// Offline support (plan §3.6 + WP2.1): IndexedDB working-set cache + checkout outbox.
// Scope is deliberately "till checkout only" — admin/reports require connectivity.
// v2 (WP2.1, 2026-07-24): the outbox stores the ready-to-send v1 IngestSaleRequest,
// gains a "parked" store for permanently-rejected sales (the client-side dead-letter),
// and hosts the per-device monotonic sale sequence (deviceSeq) — an IndexedDB
// transaction makes the increment atomic even across tabs.

import type { Item, PayMethod, Discount } from "./api.ts";
import type { IngestSaleRequest } from "./pipeline.ts";

const DB_NAME = "plutus-till";
const DB_VERSION = 2;

function openDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, DB_VERSION);
    req.onupgradeneeded = () => {
      const db = req.result;
      if (!db.objectStoreNames.contains("items")) db.createObjectStore("items", { keyPath: "idOne" });
      if (!db.objectStoreNames.contains("meta")) db.createObjectStore("meta");
      if (!db.objectStoreNames.contains("outbox")) db.createObjectStore("outbox", { keyPath: "saleId" });
      if (!db.objectStoreNames.contains("parked")) db.createObjectStore("parked", { keyPath: "saleId" });
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

export async function cachedItemSearch(term: string, limit = 8, matchAllWords = false): Promise<Item[]> {
  const all = await tx("items", "readonly", (s) => s.getAll() as IDBRequest<Item[]>);
  // mirror the server's ItemParameters: match-each-word when the pref is on, whole phrase otherwise
  const words = matchAllWords ? term.toLowerCase().split(/\s+/).filter(Boolean) : [term.toLowerCase()];
  return all
    .filter((i) => words.every((q) => i.name.toLowerCase().includes(q) || i.idOne.toLowerCase().includes(q) || i.brand.toLowerCase().includes(q)))
    .slice(0, limit);
}

export const cachedItemCount = (): Promise<number> => tx("items", "readonly", (s) => s.count());

// ── reference data (pay methods, discounts) ─────────────────────────────────

export const cacheMeta = (key: "payMethods" | "discounts", value: PayMethod[] | Discount[]) =>
  tx("meta", "readwrite", (s) => s.put(value, key)).then(() => undefined);

export const cachedMeta = <T>(key: "payMethods" | "discounts"): Promise<T | undefined> =>
  tx("meta", "readonly", (s) => s.get(key) as IDBRequest<T | undefined>);

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
export const removeQueued = (saleId: string) => tx("outbox", "readwrite", (s) => s.delete(saleId)).then(() => undefined);
export const queuedCount = (): Promise<number> => tx("outbox", "readonly", (s) => s.count());

export const parkSale = (p: ParkedSale) => tx("parked", "readwrite", (s) => s.put(p)).then(() => undefined);
export const parkedSales = (): Promise<ParkedSale[]> => tx("parked", "readonly", (s) => s.getAll() as IDBRequest<ParkedSale[]>);
export const parkedCount = (): Promise<number> => tx("parked", "readonly", (s) => s.count());
