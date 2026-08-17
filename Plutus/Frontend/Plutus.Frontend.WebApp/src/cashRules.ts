// W-P5 — the cash-queue DECISIONS, separated from the IndexedDB plumbing.
//
// ⚠⚠ C2 TWIN of `Services/Sync/CashPushService.cs`. These three functions are pulled out of
// `cashOutbox.ts` deliberately: the queue itself is I/O and needs a browser, but **every way this
// feature can lose or hide money is a decision**, and a decision can be tested without one.
//
// ⚠ `CashPushService`'s header is the source for all three. If any of them is changed here, change it
// there in the same commit — a till that queued cash by one rule and closed a day by another would
// produce a variance nobody could explain from the numbers.

import type { CashEventType } from "./pipeline.ts";

/**
 * Is the day closed, given its marks in the order they happened?
 *
 * ⚠⚠ THE LATEST MARK WINS — never `some(ZClose)`. `Any(ZClose)` would refuse a reopened day for ever,
 * which is exactly why the reopen is a **compensating event rather than a deletion**: the ZClose stays
 * on the record (somebody counted and banked that drawer, and a variance was computed against it) and
 * the reopen sits after it.
 *
 * ⚠ This is the same rule `CashDay.IsClosed` states server-side, and **ties go to CLOSED**. Three
 * places ask the question — the cash guard, the sales gate and this local copy — and they must agree,
 * or a day open for a float and shut for a sale is a disagreement nobody finds until the figures stop
 * matching.
 *
 * ⚠ Enforced LOCALLY as well as server-side because the server's guard cannot be consulted with the
 * line down — which is precisely when it matters.
 */
export function lastMarkClosesDay(typesInOrder: readonly CashEventType[]): boolean {
  // ⚠ Only Z marks count. An X read counts the drawer without closing it, so it must not shift this.
  const marks = typesInOrder.filter((t) => t === "ZClose" || t === "ZReopen");
  return marks.length > 0 && marks[marks.length - 1] === "ZClose";
}

/**
 * May a queued Z close be sent yet?
 *
 * ⚠⚠ IT WAITS FOR ITS OWN DAY'S SALES. The platform's expected drawer is float + **cash takings** +
 * ins − outs, so a Z that overtakes queued sales reports a shortage equal to every sale still
 * waiting — a till that traded £400 through an outage would tell the person who counted it correctly
 * that they were £400 down.
 *
 * ⚠ And the Z is TERMINAL server-side: it refuses everything against that day afterwards. So sending
 * it early would then reject the day's real sales, putting genuine takings into quarantine behind
 * their own close.
 *
 * ⚠ PENDING sales only, never permanently-rejected ones — a parked sale would block this till's close
 * for ever. And per business DAY, so one stuck sale from last week cannot block every close from now on.
 */
export const zMayGo = (pendingSalesForThatDay: number): boolean => pendingSalesForThatDay === 0;

/**
 * Is this HTTP status a refusal that must never be retried?
 *
 * ⚠⚠ 409 AND 400 ONLY. 409 means the day is already Z-closed and 400 means the platform refused the
 * shape — retrying either cannot help, and **a till that retries them for ever looks healthy while
 * quietly never banking.** That is the failure that hides money rather than losing it, which is why
 * nobody notices it.
 *
 * ⚠ Everything else — offline, 5xx, 429, an auth hiccup — is a RETRY. Treating one of those as
 * terminal would drop a float on the floor.
 */
export const terminalRefusal = (status: number): boolean => status === 409 || status === 400;
