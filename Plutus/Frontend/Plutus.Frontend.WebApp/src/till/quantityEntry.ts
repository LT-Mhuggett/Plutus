/**
 * Typing a quantity straight into the box, instead of pressing + or − until it is right.
 *
 * ⚠⚠ WHY THIS IS A RULE AND NOT AN `onChange`. Matt, 2026-08-25: *"can you just overwrite the
 * number? … This needs to be monitored so that if change the number the till is aware, or if you
 * click out of the box."* Quantity **multiplies price**, so this is a money path: the box is what the
 * operator tenders against. Three things follow, and none of them are obvious from the UI code:
 *
 *  1. **Nothing is committed while they are typing.** A basket that re-prices on every keystroke
 *     passes through states the operator never meant — clearing "10" to type "2" momentarily means
 *     an empty box, and an empty box read as a number is 0, which on the line reducer **deletes the
 *     line**. So the draft is local text, and only `commitQuantity` reaches the basket.
 *  2. **Committed on blur AND on Enter**, which is the "monitored" half of the request. Clicking away
 *     is as much a decision as pressing Enter, and a till where the number you typed silently did not
 *     apply is worse than one that made you press a button.
 *  3. **Strict digits.** `parseInt("2.5")` is `2` and `parseInt("3x")` is `3` — the same trap that
 *     `stockAdjust.ts` exists to avoid, and it matters more here because the wrong answer is charged
 *     to a customer rather than written to a ledger.
 */

/** What the caller should do once the operator has finished with the box. */
export type QuantityCommit =
  /** A real, positive quantity — apply it. */
  | { action: "set"; quantity: number }
  /** Explicitly zero. ⚠ The line goes, exactly as pressing − at 1 already does. */
  | { action: "remove" }
  /** Empty, or not a whole number — put the old value back and change nothing. */
  | { action: "revert" };

/**
 * ⚠ Digits only, and no sign. A return line carries a POSITIVE quantity and gets its sign from
 * `isReturn` at display and total time, so a typed "-2" is meaningless here rather than clever —
 * and accepting it would silently turn a sale line into something the totals treat as a refund.
 */
const WHOLE_NUMBER = /^\d+$/;

/**
 * Decide what a finished edit means.
 *
 * @param typed what is in the box
 * @param allowRemove whether 0 may delete the line. `true` for a basket line (⚠ mirrors the − button
 *   at quantity 1, which already removes it), `false` for the pending-scan box, whose floor is 1
 *   because there is no line to delete yet.
 */
export function commitQuantity(typed: string, allowRemove: boolean): QuantityCommit {
  const t = typed.trim();
  if (!WHOLE_NUMBER.test(t)) return { action: "revert" };

  const n = Number(t);

  // ⚠ `Number.isSafeInteger` as well as the regex: a box full of digits can still overflow into
  // floating point ("99999999999999999999"), and a quantity that cannot be represented exactly
  // would multiply a price into nonsense.
  if (!Number.isSafeInteger(n)) return { action: "revert" };

  if (n === 0) return allowRemove ? { action: "remove" } : { action: "revert" };
  return { action: "set", quantity: n };
}

/**
 * ⚠ Keystroke filter, deliberately permissive: it only stops characters that can never be part of a
 * quantity, so the operator can still clear the box and retype. Validation proper happens at commit —
 * rejecting as they type is how a box becomes impossible to correct.
 */
export function isQuantityDraft(typed: string): boolean {
  return typed === "" || WHOLE_NUMBER.test(typed.trim());
}
