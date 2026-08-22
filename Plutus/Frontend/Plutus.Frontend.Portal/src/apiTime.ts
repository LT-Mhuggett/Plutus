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


// ── WP-TZ, 2026-08-22: the shop's clock ─────────────────────────────────────────────────────────

/**
 * The timezone every timestamp is RENDERED in, or null for "the reader's own device".
 *
 * ⚠⚠ THE SURFACE THIS EXISTS FOR IS THE PORTAL. A till PC sits in the shop on the shop's clock, so
 * its own rendering is already right. The portal is opened from anywhere — a manager at home, an
 * accountant in another country — and every timestamp on it renders in the BROWSER's zone: the same
 * sale reads 14:32 on the shop floor and 15:32 in Madrid, with nothing on screen saying which.
 *
 * ⚠⚠ IT CHANGES RENDERING ONLY. `apiDate` still answers the same INSTANT; only the wall clock those
 * instants are printed against moves. Nothing here can change which day a sale filed under —
 * `BusinessDay` is the till's own local clock and stays that way, deliberately.
 *
 * ⚠ NULL IS THE DEFAULT AND MUST STAY REACHABLE: a tenant that never sets a zone sees exactly what
 * it saw before this existed.
 */
let displayZone: string | null = null;

/**
 * Point every formatter at a shop timezone.
 *
 * ⚠⚠ VALIDATED HERE, ONCE, BY ASKING `Intl`. A bad `timeZone` makes `toLocaleString` throw a
 * RangeError — so an unknown id set here would not render a wrong time, it would blank every date on
 * the page. Rejecting it at the door leaves the platform rendering device-local, which is wrong by
 * an hour rather than wrong by everything.
 *
 * ⚠ Pass null or "" to clear.
 */
export function setDisplayZone(zone: string | null | undefined): boolean {
  if (!zone) { displayZone = null; return true; }

  try {
    new Intl.DateTimeFormat("en-GB", { timeZone: zone }).format(new Date());
  } catch {
    displayZone = null;
    return false;
  }

  displayZone = zone;
  return true;
}

/** What the formatters are currently rendering in, or null for the device's own zone. */
export const getDisplayZone = (): string | null => displayZone;

/** The zone this device is on — what the reader would otherwise see. */
export const deviceZone = (): string => Intl.DateTimeFormat().resolvedOptions().timeZone;

/**
 * ⚠ IS THE READER ON A DIFFERENT CLOCK FROM THE SHOP, RIGHT NOW?
 *
 * ⚠⚠ COMPARED AS RENDERED TIMES, NOT AS ZONE NAMES. `Europe/London` and `Europe/Dublin` are two
 * names for one clock all year, and warning about those would train people to ignore the banner.
 * Two zones can also agree in January and differ in July, so it is asked at a moment.
 */
export function readerZoneDiffers(at: Date = new Date()): boolean {
  if (!displayZone) return false;

  const opts: Intl.DateTimeFormatOptions = { hour: "2-digit", minute: "2-digit", hour12: false };
  const shop = new Intl.DateTimeFormat("en-GB", { ...opts, timeZone: displayZone }).format(at);
  const here = new Intl.DateTimeFormat("en-GB", opts).format(at);
  return shop !== here;
}

/** ⚠ Every formatter below passes this, so one setting moves all of them or none. */
const zoned = (extra?: Intl.DateTimeFormatOptions): Intl.DateTimeFormatOptions =>
  displayZone ? { ...extra, timeZone: displayZone } : { ...extra };
/** Date and time, as a person in this shop reads them. */
export const apiDateTime = (value: string | null | undefined): string =>
  apiDate(value)?.toLocaleString("en-GB", zoned()) ?? NOTHING;

/** Date only — for a column where the time is noise. */
export const apiDay = (value: string | null | undefined): string =>
  apiDate(value)?.toLocaleDateString("en-GB", zoned()) ?? NOTHING;

/** Time only, to the minute — for a list of today's sales, where the date is in the heading. */
export const apiTime = (value: string | null | undefined): string =>
  apiDate(value)?.toLocaleTimeString("en-GB", zoned({ hour: "2-digit", minute: "2-digit" })) ?? NOTHING;

/**
 * Time to the second — the portal Dashboard's live sale feed.
 *
 * ⚠ SECONDS ARE DELIBERATE THERE and nowhere else: that column exists to tell a manager watching the
 * dashboard whether the sale that just appeared is the one they are standing next to. Every other
 * time on this platform is to the minute.
 */
export const apiClock = (value: string | null | undefined): string =>
  apiDate(value)?.toLocaleTimeString("en-GB", zoned()) ?? NOTHING;

/**
 * ⚠⚠ TODAY AS THE SHOP RECKONS IT — `YYYY-MM-DD`, for a date input or a business-day filter.
 *
 * ⚠ NOT `new Date().toISOString().slice(0, 10)`, WHICH IS THE UTC DAY. Britain is an hour ahead of
 * UTC all summer, so from midnight until 01:00 BST that idiom answers YESTERDAY — and "Today's
 * sales" showing an empty screen during the one hour a late shop is still cashing up is precisely
 * when somebody would believe the till had lost the day's takings.
 *
 * ⚠ AND NOT THE DEVICE'S DAY EITHER, when a shop zone is set: the portal is opened from anywhere,
 * so "today" has to mean the shop's today or a manager abroad drills into the wrong date. Falls
 * back to the device when no shop zone is known, which is what every screen did before WP-TZ.
 *
 * ⚠ Assembled from `formatToParts` rather than trusting a locale to emit ISO order. `en-CA` happens
 * to give `YYYY-MM-DD` in every engine we run on, and that is exactly the kind of happens-to that
 * turns into a date parsed as month-first on someone else's machine.
 */
export function businessToday(at: Date = new Date()): string {
  const parts = new Intl.DateTimeFormat("en-CA", {
    ...zoned(),
    year: "numeric", month: "2-digit", day: "2-digit",
  }).formatToParts(at);

  const get = (type: string) => parts.find((p) => p.type === type)?.value ?? "";
  return `${get("year")}-${get("month")}-${get("day")}`;
}
