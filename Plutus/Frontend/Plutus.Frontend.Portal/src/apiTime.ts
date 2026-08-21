/**
 * **Every timestamp this API sends, turned into an instant — the ONE rule.**
 *
 * ⚠⚠ TWINNED FILE — the web till and the portal each hold a byte-identical copy, pinned by
 * `FrontendTwinTests`. They are separate npm apps and cannot share a package. **Edit one, copy it to
 * the other, same commit.**
 *
 * ⚠⚠ MATT, 2026-08-21: *"Why are the sales a correct time on the portal and an hour earlier on the
 * webtill?"* Because the portal wrote `new Date(value + "Z")` at each of its ~40 call sites and the
 * till wrote `new Date(value)` at its four. During BST that is exactly one hour, and in winter the
 * two tills would have agreed and the bug would have gone back into hiding until March.
 *
 * ⚠⚠ **A BARE TIMESTAMP IS STILL UTC.** The API's stamps are all named `…Utc`, and the ones that come
 * straight off a MySQL column arrive as `DateTimeKind.Unspecified`, which System.Text.Json writes
 * WITHOUT a suffix: `"2026-08-21T14:30:00"`. `new Date()` reads a bare date-time as **local**, so it
 * never converts, and the clock reads an hour early wherever the browser is not on UTC.
 *
 * ⚠⚠ **AND `+ "Z"` IS NOT THE FIX** — it shipped its own bug the same day it fixed one. Portal,
 * 2026-08-12, with a screenshot: every till read **"Invalid Date"**, because a value that came from
 * `DateTime.UtcNow` (Kind=Utc) already ends in `Z` and `…ZZ` is not a date. Both formats are on this
 * wire, today, from the same API. Anything that assumes one of them is wrong half the time.
 *
 * So: pass through a value that already carries a zone, and treat a bare one as UTC. That is the
 * whole rule, it is idempotent, and it is why this file exists rather than a forty-fifth call site
 * getting it right on its own.
 *
 * ⚠ **`till-design.md` C2 pins this to MAUI's `Plutus.SharedKernel.ApiTime`** as well as to the other
 * copy of this file. Three implementations of one rule in three languages — and the .NET side had the
 * IDENTICAL bug at the same moment (`.ToLocalTime()` on an `Unspecified` `DateTime` does nothing),
 * found only because this one was.
 */

/** Does this string already say what zone it is in? `Z`, `z`, `+01:00`, `-0500`. */
const HAS_ZONE = /(?:Z|[+-]\d{2}:?\d{2})$/i;

/**
 * A server timestamp as a `Date`.
 *
 * ⚠ RETURNS `null` FOR NOTHING, rather than the epoch or an Invalid Date. `new Date(null)` is
 * 1 January 1970 and `new Date(undefined)` is Invalid Date, and both of those have been rendered to
 * an operator by this platform — "01/01/1970" reads as data, which is worse than a dash.
 */
export function apiDate(value: string | null | undefined): Date | null {
  if (!value) return null;
  const d = new Date(HAS_ZONE.test(value) ? value : value + "Z");
  return Number.isNaN(d.getTime()) ? null : d;
}

/** Milliseconds since the epoch, or `NaN` — for arithmetic and comparisons. */
export function apiMs(value: string | null | undefined): number {
  return apiDate(value)?.getTime() ?? Number.NaN;
}

/**
 * ⚠ THE FALLBACK IS A DASH, NOT A BLANK. A column that is sometimes empty and sometimes a date reads
 * as a rendering fault; a dash reads as "nothing recorded", which is what it means.
 */
const NOTHING = "—";

/** Date and time, as a person in this shop reads them. */
export const apiDateTime = (value: string | null | undefined): string =>
  apiDate(value)?.toLocaleString("en-GB") ?? NOTHING;

/** Date only — for a column where the time is noise. */
export const apiDay = (value: string | null | undefined): string =>
  apiDate(value)?.toLocaleDateString("en-GB") ?? NOTHING;

/** Time only, to the minute — for a list of today's sales, where the date is in the heading. */
export const apiTime = (value: string | null | undefined): string =>
  apiDate(value)?.toLocaleTimeString("en-GB", { hour: "2-digit", minute: "2-digit" }) ?? NOTHING;

/**
 * Time to the second — the portal Dashboard's live sale feed.
 *
 * ⚠ SECONDS ARE DELIBERATE THERE and nowhere else: that column exists to tell a manager watching the
 * dashboard whether the sale that just appeared is the one they are standing next to. Every other
 * time on this platform is to the minute.
 */
export const apiClock = (value: string | null | undefined): string =>
  apiDate(value)?.toLocaleTimeString("en-GB") ?? NOTHING;
