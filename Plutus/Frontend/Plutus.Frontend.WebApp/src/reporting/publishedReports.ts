/**
 * Which reports the portal has published to this till — ruling 5b(a), 2026-08-19.
 *
 * ⚠⚠ Matt: *"Portal shows which reports a till can show."* The catalogue is now the SUPERSET; this
 * decides which of it an operator is offered.
 *
 * ⚠⚠ C2 TWIN of `SharedKernel/ReportCatalogue.PublishedOr` + MAUI's `Services/Reporting/
 * PublishedReports`. All three answer the same two questions — what does silence mean, and what
 * happens to a key we do not recognise — and two tills that disagree show different menus for the same
 * shop. The vectors run on both sides: `publishedReports.test.ts` here, `ReportCatalogueTests` in C#.
 *
 * ⚠⚠ **THE PUBLISH DECIDES THE MENU, THE PERMISSION DECIDES THE DOOR.** This module knows nothing about
 * permissions and must not learn: `ReportingPage` applies both, and a report published but unreadable is
 * not listed at all — never listed-and-refused, because a greyed tab leaks what other roles can see.
 */

/**
 * The full catalogue, in menu order.
 *
 * ⚠⚠ MUST MATCH `SharedKernel.ReportCatalogue.Keys` — order included, because it IS the menu order on
 * both tills. The C# side pins its list in a test precisely because these keys are stored in the
 * database; renaming one silently unpublishes it for every tenant that had it ticked.
 */
export const ALL_REPORT_KEYS = [
  "summary", "vat", "items-sold", "category-sales",
  "best-sellers", "stock", "negative-stock", "sales",
] as const;

/** Versioned, so a future change of shape cannot be read as a valid old value. */
const CACHE_KEY = "plutus.reports.published.v1";

/**
 * What the till may offer, given what the server said.
 *
 * ⚠⚠ NULL MEANS "NOBODY HAS CHOSEN" AND RESOLVES TO EVERY REPORT. A tenant who has never opened the
 * portal screen must see the menu they saw yesterday — defaulting to "nothing" would empty the Reports
 * tab in every shop the moment this deployed. **Absence of a choice is not a choice.**
 *
 * ⚠ An EMPTY array is a real decision — "this till shows no reports" — and is honoured. That is why the
 * never-chosen case is `null` and not `[]`: collapsing them would make one impossible to express.
 *
 * ⚠ Unknown keys are DROPPED, and the result is in CATALOGUE order rather than stored order, so the
 * menu cannot be reshuffled by however the portal happened to serialise the list.
 */
export function publishedOr(storedKeys: readonly string[] | null | undefined): string[] {
  if (storedKeys === null || storedKeys === undefined) return [...ALL_REPORT_KEYS];

  const stored = new Set(storedKeys);
  return ALL_REPORT_KEYS.filter((k) => stored.has(k));
}

/**
 * The last answer this browser had, or null if it has never had one.
 *
 * ⚠ A corrupt cache reads as "never asked", which shows every report. Failing towards MORE reports is
 * the right direction for a menu; failing towards none is a till that looks broken.
 */
export function cachedPublished(): string[] | null {
  try {
    const raw = localStorage.getItem(CACHE_KEY);
    if (raw === null) return null;
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed.filter((k) => typeof k === "string") : null;
  } catch {
    return null;
  }
}

export function cachePublished(keys: readonly string[]): void {
  try {
    localStorage.setItem(CACHE_KEY, JSON.stringify(keys));
  } catch {
    // ⚠ A full or blocked localStorage must not stop the Reports tab rendering.
  }
}

/**
 * Ask the server, cache the answer, and return the effective list.
 *
 * ⚠⚠ THREE FALLBACKS, IN ORDER, AND THE LAST IS "EVERYTHING": what the server just said, then the
 * cached last-known-good, then the full catalogue. A till that loses its Reports tab because the
 * network blinked is worse than one showing a report an owner meant to hide.
 *
 * ⚠ Never throws, and never returns an empty list *because of a failure* — only because a shop
 * deliberately published nothing.
 */
export async function fetchPublished(
  get: (url: string) => Promise<unknown>,
  tillId?: string | null,
): Promise<string[]> {
  try {
    const url = tillId
      ? `/api/v1/reports/published?tillId=${encodeURIComponent(tillId)}`
      : "/api/v1/reports/published";

    const answer = (await get(url)) as { keys?: unknown } | null;
    const keys = answer?.keys;

    // ⚠ A missing/!array `keys` is "could not ask", NOT "nothing published" — do not cache it.
    if (!Array.isArray(keys)) return publishedOr(cachedPublished());

    const effective = publishedOr(keys.filter((k) => typeof k === "string"));
    cachePublished(effective);
    return effective;
  } catch {
    return publishedOr(cachedPublished());
  }
}
