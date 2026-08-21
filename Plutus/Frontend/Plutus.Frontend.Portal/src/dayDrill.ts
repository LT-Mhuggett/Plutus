/**
 * **"Show me the sales on that day" — the one drill, used by both charts.**
 *
 * ⚠⚠ MATT, 2026-08-21, asking for it TWICE in one message: *"when I click on a day in the dashboard
 * it needs to take be to reporting filtered on the sales taken on THAT day, which is a custom report
 * filtered on that specific day"* and *"In reporting, I need to be able to click on a day and it
 * shows all the sales from that specific day."*
 *
 * Two entry points, ONE destination. ⚠ Building it twice is how the two would end up disagreeing
 * about where a day starts — which is not hypothetical here: the whole platform spent the same
 * morning on a timestamp bug caused by two apps each deciding that for themselves
 * (`till-design.md` C2, and `apiTime.ts`).
 *
 * ⚠ IT RIDES THE `focus` HINT THAT ALREADY EXISTS. `nav.tsx` has carried an optional string from
 * `go(tab, focus)` to the target page since the inventory drill was built — *"an optional hint the
 * target page may consume"*. A second navigation mechanism for the same job would be the same
 * mistake in a different file.
 *
 * ⚠ A BUSINESS DAY, `yyyy-MM-dd`, never an instant. The server buckets on `BusinessDay`, and a shop
 * open past midnight has a business day that is not a calendar day — turning this into a timestamp
 * anywhere on the way would put the late sales on the wrong report.
 */

const PREFIX = "day:";

/** The `focus` hint for a day drill. */
export const dayFocus = (businessDay: string): string => `${PREFIX}${businessDay}`;

/**
 * Read a day back out of a `focus` hint, or `null` if it is not one.
 *
 * ⚠ VALIDATED, NOT TRUSTED. `focus` is also set from the URL hash by other drills, so this must
 * refuse anything that is not a plain `yyyy-MM-dd` rather than hand a malformed string to a date
 * range that then quietly returns the wrong period.
 */
export function dayFromFocus(focus: string | undefined): string | null {
  if (!focus?.startsWith(PREFIX)) return null;
  const day = focus.slice(PREFIX.length);
  return /^\d{4}-\d{2}-\d{2}$/.test(day) ? day : null;
}
