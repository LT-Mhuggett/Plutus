/**
 * The carrier bags this shop sells, as the portal set them — ruling 2026-08-19.
 *
 * ⚠⚠ Matt: *"That creates the 5p and 20p bags at the back and that pushes down to the tills… This
 * would be cleaner than creating a bag at each till."* Before this the till stored ONE bag barcode as
 * a local device preference (`prefs.bagBarcode`), so a five-till shop configured it five times and the
 * tills could disagree — and it is how this till came to hold `"001"`, a barcode no item has.
 *
 * ⚠⚠ C2 TWIN of `SharedKernel.CarrierBags` (the id/price rule) and MAUI's `Services/Sales/CarrierBags`.
 * All three answer the same question — what makes an id a bag, and what does silence mean — and two
 * tills that disagree offer different bags for the same shop. Vectors: `carrierBags.test.ts` here,
 * `CarrierBagTests` in C#.
 *
 * ⚠⚠ **THE FALLBACK DIRECTION IS THE OPPOSITE OF `publishedReports.ts`, ON PURPOSE.** That module
 * fails towards MORE reports, because a till that loses its Reports tab looks broken. This one fails
 * towards NO BAG BUTTONS, because the alternative is inventing a price and charging a customer money
 * the shop never set. A missing button is a cashier keying an item; a wrong price is a refund and a
 * complaint. Cached-last-known-good first, then nothing.
 */

/** A bag, exactly as the server sends it. */
export interface CarrierBag {
  /** `BAG-<pence>` — a real catalogue barcode. ⚠ The price IS the identity. */
  idOne: string;
  /** What the customer sees on the receipt. */
  name: string;
  pricePence: number;
}

/**
 * ⚠⚠ MUST MATCH `SharedKernel.CarrierBags.IdPrefix`. Renaming it on one side means this till stops
 * recognising bags the server still sends — see C2.
 */
export const BAG_ID_PREFIX = "BAG-";

/** Versioned, so a future change of shape cannot be read as a valid old value. */
const CACHE_KEY = "plutus.carrierbags.v1";

/**
 * Is this a carrier-bag barcode?
 *
 * ⚠ C2 twin of `CarrierBags.IsBagId`. The prefix must match exactly and the rest must be a positive
 * whole number of pence, so a real product called `BAGGY-10` is not mistaken for a bag and `BAG-0`
 * — a free bag, which is not a line on a receipt — is not one either.
 */
export function isBagId(id: string | null | undefined): boolean {
  if (typeof id !== "string" || !id.startsWith(BAG_ID_PREFIX)) return false;
  const rest = id.slice(BAG_ID_PREFIX.length);
  return /^[0-9]+$/.test(rest) && Number(rest) > 0;
}

/**
 * Clean a server answer into a bag list, cheapest first.
 *
 * ⚠⚠ A ROW THAT IS NOT USABLE IS DROPPED, NOT REPAIRED. A bag whose price does not match its own id,
 * or whose price is missing, would put an unexplainable line on a receipt — so it does not become a
 * button. Refusing to sell something is recoverable; selling it at a made-up price is not.
 *
 * ⚠ Sorted cheapest first here rather than trusting the server, so the button order cannot be
 * reshuffled by however the list happened to be serialised. The single-use bag is asked for many
 * times a day and the bag for life occasionally, so the common one sits under the operator's thumb.
 */
export function bagsFrom(raw: unknown): CarrierBag[] {
  if (!Array.isArray(raw)) return [];

  const clean: CarrierBag[] = [];
  for (const row of raw) {
    if (row === null || typeof row !== "object") continue;
    const { idOne, name, pricePence } = row as Partial<CarrierBag>;

    if (typeof idOne !== "string" || !isBagId(idOne)) continue;
    if (typeof pricePence !== "number" || !Number.isFinite(pricePence) || pricePence <= 0) continue;

    // ⚠ The id encodes the price, so the two can be cross-checked — and a disagreement means the
    // item was edited behind the portal's back. Do not guess which one is right.
    if (Number(idOne.slice(BAG_ID_PREFIX.length)) !== pricePence) continue;

    clean.push({
      idOne,
      // ⚠ A blank name would render a nameless button; the price is the one thing always true.
      name: typeof name === "string" && name.trim() !== "" ? name : `Bag (${(pricePence / 100).toFixed(2)})`,
      pricePence,
    });
  }

  return clean.sort((a, b) => a.pricePence - b.pricePence);
}

/**
 * The last list this browser had, or an empty list if it has never had one.
 *
 * ⚠ A corrupt cache reads as "no bags", which hides the buttons. Failing towards no button is the
 * right direction here — see the file header.
 */
export function cachedBags(): CarrierBag[] {
  try {
    const raw = localStorage.getItem(CACHE_KEY);
    return raw === null ? [] : bagsFrom(JSON.parse(raw));
  } catch {
    return [];
  }
}

export function cacheBags(bags: readonly CarrierBag[]): void {
  try {
    localStorage.setItem(CACHE_KEY, JSON.stringify(bags));
  } catch {
    // ⚠ A full or blocked localStorage must not stop the till rendering.
  }
}

/**
 * Ask the server, cache the answer, and return the bags to offer.
 *
 * ⚠ Never throws. An offline till keeps yesterday's bags, which are almost certainly still right; a
 * till that has never been online offers none.
 *
 * ⚠⚠ AN EMPTY LIST FROM THE SERVER IS A REAL ANSWER and replaces the cache: a shop that stopped
 * selling bags must stop offering them, and treating "none" as "could not ask" would leave the
 * buttons up for ever. A malformed answer is "could not ask" and does NOT overwrite the cache.
 */
export async function fetchCarrierBags(get: (url: string) => Promise<unknown>): Promise<CarrierBag[]> {
  try {
    const answer = await get("/api/v1/carrier-bags");

    // ⚠ Not an array = the request did not really succeed. Keep what we had.
    if (!Array.isArray(answer)) return cachedBags();

    const bags = bagsFrom(answer);
    cacheBags(bags);
    return bags;
  } catch {
    return cachedBags();
  }
}
