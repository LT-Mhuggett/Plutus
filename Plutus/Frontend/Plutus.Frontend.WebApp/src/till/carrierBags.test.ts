import { describe, expect, it, vi } from "vitest";
import { bagsFrom, fetchCarrierBags, isBagId } from "./carrierBags.ts";

/**
 * ⚠ These are the TypeScript half of the C2 pair; `CarrierBagTests.cs` runs the same vectors in .NET.
 * Two tills that disagree about what a bag is offer different bags for the same shop.
 *
 * ⚠ vitest runs in NODE here, not jsdom, so `localStorage` does not exist. Every test that touches the
 * cache stubs it — and the module is written to survive its absence, which these prove.
 */
describe("what counts as a carrier bag", () => {
  it("recognises BAG-<pence> and nothing else", () => {
    expect(isBagId("BAG-10")).toBe(true);
    expect(isBagId("BAG-20")).toBe(true);

    expect(isBagId("BAG-0")).toBe(false);        // a free bag is not a line on a receipt
    expect(isBagId("BAG--5")).toBe(false);
    expect(isBagId("BAG-")).toBe(false);
    expect(isBagId("BAG-abc")).toBe(false);
    expect(isBagId("BAGGY-10")).toBe(false);     // the prefix must be exact
    expect(isBagId("GIFT-CARD")).toBe(false);
    expect(isBagId("045778022960")).toBe(false); // a bag PRODUCT is not a carrier bag
    expect(isBagId(null)).toBe(false);
    expect(isBagId(undefined)).toBe(false);
  });
});

describe("cleaning the server's answer", () => {
  it("keeps good rows, cheapest first", () => {
    const bags = bagsFrom([
      { idOne: "BAG-20", name: "Bag for life", pricePence: 20 },
      { idOne: "BAG-10", name: "Single-use carrier bag", pricePence: 10 },
    ]);

    expect(bags.map((b) => b.idOne)).toEqual(["BAG-10", "BAG-20"]);
  });

  /**
   * ⚠⚠ THE ONE THAT WOULD COST MONEY. A row whose price contradicts its own id means the item was
   * edited behind the portal's back, and there is no way to tell which figure is right — so it does
   * not become a button. A wrong price is a refund and a complaint; a missing button is a keystroke.
   */
  it("drops a row whose price does not match its id", () => {
    expect(bagsFrom([{ idOne: "BAG-10", name: "Bag", pricePence: 25 }])).toEqual([]);
  });

  it("drops rows that could not be sold", () => {
    expect(bagsFrom([
      { idOne: "BAG-10", name: "Bag" },                       // no price
      { idOne: "BAG-10", name: "Bag", pricePence: 0 },
      { idOne: "045778022960", name: "A real product", pricePence: 100 },
      { idOne: "BAG-10", name: "Bag", pricePence: "10" },      // price as a string
      null,
      "BAG-10",
    ])).toEqual([]);
  });

  /** ⚠ A nameless button is worse than a plainly-named one; the price is the one thing always true. */
  it("names a bag whose name is blank", () => {
    expect(bagsFrom([{ idOne: "BAG-10", name: "  ", pricePence: 10 }])[0].name).toContain("0.10");
  });

  it("treats anything that is not a list as no bags", () => {
    expect(bagsFrom(null)).toEqual([]);
    expect(bagsFrom({ bags: [] })).toEqual([]);
    expect(bagsFrom(undefined)).toEqual([]);
  });
});

describe("fetching", () => {
  const withStore = () => {
    const store = new Map<string, string>();
    vi.stubGlobal("localStorage", {
      getItem: (k: string) => store.get(k) ?? null,
      setItem: (k: string, v: string) => { store.set(k, v); },
      removeItem: (k: string) => { store.delete(k); },
    });
    return store;
  };

  it("returns what the server said and caches it", async () => {
    withStore();
    const bags = await fetchCarrierBags(async () => [{ idOne: "BAG-10", name: "Bag", pricePence: 10 }]);
    expect(bags).toHaveLength(1);

    // ⚠ Offline next time → the same bags, from the cache.
    const again = await fetchCarrierBags(async () => { throw new Error("offline"); });
    expect(again).toEqual(bags);
  });

  /**
   * ⚠⚠ THE FALLBACK DIRECTION, PINNED. `publishedReports` fails towards MORE reports; this fails
   * towards NO BAGS, because the alternative is inventing a price and charging a customer for it.
   */
  it("offers no bags when it has never reached the server", async () => {
    withStore();
    expect(await fetchCarrierBags(async () => { throw new Error("offline"); })).toEqual([]);
  });

  /** ⚠ An empty list is a real decision — the shop stopped selling bags — and replaces the cache. */
  it("honours an empty list from the server", async () => {
    withStore();
    await fetchCarrierBags(async () => [{ idOne: "BAG-10", name: "Bag", pricePence: 10 }]);

    expect(await fetchCarrierBags(async () => [])).toEqual([]);
    expect(await fetchCarrierBags(async () => { throw new Error("offline"); })).toEqual([]);
  });

  /** ⚠ A malformed answer is "could not ask", NOT "no bags" — it must not wipe a good cache. */
  it("keeps the cache when the answer is not a list", async () => {
    withStore();
    const good = await fetchCarrierBags(async () => [{ idOne: "BAG-10", name: "Bag", pricePence: 10 }]);

    expect(await fetchCarrierBags(async () => ({ bags: [] }))).toEqual(good);
  });

  /** ⚠ No localStorage at all (a locked-down browser) must still sell. */
  it("works with no localStorage", async () => {
    vi.stubGlobal("localStorage", undefined);
    expect(await fetchCarrierBags(async () => [{ idOne: "BAG-10", name: "Bag", pricePence: 10 }]))
      .toHaveLength(1);
    expect(await fetchCarrierBags(async () => { throw new Error("offline"); })).toEqual([]);
  });
});
