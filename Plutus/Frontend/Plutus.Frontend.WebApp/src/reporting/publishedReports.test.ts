import { beforeEach, describe, expect, it } from "vitest";
import { ALL_REPORT_KEYS, cachedPublished, fetchPublished, publishedOr } from "./publishedReports.ts";

/**
 * ⚠⚠ THE C2 VECTORS for ruling 5b(a). The same cases run against
 * `SharedKernel.ReportCatalogue.PublishedOr` in `ReportCatalogueTests`, because the server, this till and
 * the MAUI till all answer "what does silence mean" and two tills that disagree show different menus for
 * the same shop. **Add a case here and add it there, in the same commit.**
 */

/**
 * ⚠ AN IN-MEMORY `localStorage`, because this repo's vitest runs in the NODE environment, not jsdom —
 * every other test file here covers pure functions and never needed one. Node 22+ warns
 * "localStorage is not available because --localstorage-file was not provided" and the global is
 * missing, so the twelve cases below all failed on their first run.
 *
 * ⚠ Stubbed HERE rather than by switching the suite to jsdom: that would slow every other file down and
 * change the environment the tendering and permission vectors have always run in, to fix one module.
 */
function installLocalStorage(): void {
  const store = new Map<string, string>();
  Object.defineProperty(globalThis, "localStorage", {
    configurable: true,
    value: {
      getItem: (k: string) => (store.has(k) ? store.get(k)! : null),
      setItem: (k: string, v: string) => { store.set(k, String(v)); },
      removeItem: (k: string) => { store.delete(k); },
      clear: () => store.clear(),
      key: (i: number) => [...store.keys()][i] ?? null,
      get length() { return store.size; },
    },
  });
}

describe("published reports — the C2 vectors", () => {
  beforeEach(() => installLocalStorage());

  // ⚠⚠ THE MOST IMPORTANT CASE. A tenant who has never opened the portal screen must see the menu they
  // saw yesterday; defaulting to "nothing" would empty the Reports tab in every shop on deploy.
  it("nobody having chosen means EVERY report, not none", () => {
    expect(publishedOr(null)).toEqual([...ALL_REPORT_KEYS]);
    expect(publishedOr(undefined)).toEqual([...ALL_REPORT_KEYS]);
  });

  // ⚠ But an empty list is a real decision and must be honoured — which is why null and [] cannot be
  // collapsed into one value.
  it("an empty published list is a deliberate choice and is honoured", () => {
    expect(publishedOr([])).toEqual([]);
  });

  it("only the published reports come back", () => {
    expect(publishedOr(["vat", "summary"])).toEqual(["summary", "vat"]);
  });

  // ⚠ Catalogue order, not stored order — otherwise the tabs reshuffle depending on the order somebody
  // happened to tick the boxes.
  it("the menu order is the catalogue's, not the stored order", () => {
    expect(publishedOr([...ALL_REPORT_KEYS].reverse())).toEqual([...ALL_REPORT_KEYS]);
  });

  it("an unknown stored key is dropped rather than passed through", () => {
    expect(publishedOr(["vat", "a-report-from-the-future"])).toEqual(["vat"]);
  });

  it("a key stored twice appears once", () => {
    expect(publishedOr(["vat", "vat", "vat"])).toEqual(["vat"]);
  });

  // ── The fallback chain: server → cache → everything ─────────────────────────

  it("uses what the server said, and caches it", async () => {
    const keys = await fetchPublished(async () => ({ keys: ["vat"] }));

    expect(keys).toEqual(["vat"]);
    expect(cachedPublished()).toEqual(["vat"]);
  });

  // ⚠⚠ A network failure must NOT empty the Reports tab. It falls back to the last good answer.
  it("falls back to the cached answer when the server cannot be reached", async () => {
    await fetchPublished(async () => ({ keys: ["vat"] }));

    const keys = await fetchPublished(async () => { throw new Error("offline"); });

    expect(keys).toEqual(["vat"]);
  });

  // ⚠ And with no cache at all, to EVERYTHING — never to nothing.
  it("falls back to every report when it has never had an answer", async () => {
    const keys = await fetchPublished(async () => { throw new Error("offline"); });

    expect(keys).toEqual([...ALL_REPORT_KEYS]);
  });

  // ⚠⚠ A malformed answer is "could not ask", not "nothing published", and must not poison the cache —
  // otherwise one bad response empties the tab until somebody clears their browser storage.
  it("a malformed answer neither empties the menu nor is cached", async () => {
    await fetchPublished(async () => ({ keys: ["vat"] }));

    const keys = await fetchPublished(async () => ({ notKeys: true }));

    expect(keys).toEqual(["vat"]);
    expect(cachedPublished()).toEqual(["vat"]);
  });

  it("passes the till id when it has one", async () => {
    const seen: string[] = [];
    await fetchPublished(async (url) => { seen.push(url); return { keys: [] }; }, "abc-123");

    expect(seen[0]).toContain("tillId=abc-123");
  });

  // ⚠ The catalogue itself is pinned because these keys are stored in the database — a rename silently
  // unpublishes the report for every tenant that had it ticked. Must match SharedKernel exactly.
  it("the catalogue matches the shipped keys, in order", () => {
    expect([...ALL_REPORT_KEYS]).toEqual([
      "summary", "vat", "items-sold", "category-sales",
      "best-sellers", "stock", "negative-stock", "sales",
    ]);
  });
});
