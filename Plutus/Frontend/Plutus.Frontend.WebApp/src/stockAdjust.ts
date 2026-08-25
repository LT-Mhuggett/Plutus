/**
 * "Adjust stock…" on the web till — the rule that turns a direction, an amount and a reason into
 * the movement the server will accept.
 *
 * ⚠⚠ WHY THIS FILE EXISTS AT ALL. Matt, 2026-08-25: *"I am not able to edit the stock level of an
 * item? I cannot do this in the inventory, there is no option to do this?"* — and he was right about
 * the WEB till. `till-design.md` A0 and Part B both marked this ✅ for web, but the app never called
 * `/api/v1/stock/movements`: the only mentions of that path were in the generated `api/types.gen.ts`.
 * The inventory editor offered "Initial stock" **only while creating**, and `updateItem` never
 * touched stock, so the stock of an existing item could not be changed at all.
 *
 * ⚠⚠ IT ALSO VOIDED MATT'S OWN 2026-08-11 RULING. `pos.stock.adjust` exists *specifically* so a
 * Supervisor at the counter can write off a damaged box without waiting for a manager — on this till
 * that permission reached no UI, so the person it was created for still could not.
 *
 * ⚠ THE C2 TWIN IS MAUI'S `ViewAllViewModel.ExecuteAdjustStock`. Same order (direction → how many →
 * why), same wording, same guards. The logic is here rather than in the component so it can be
 * tested without a DOM, which is the shape `mayAddCustomer`/`canAddCustomers` already set.
 *
 * ⚠ Drift between the two is caught by the SERVER, not by hope: it refuses a positive `WriteOff`, a
 * zero qty and a blank reason, each with the rule it broke. So a copy that gets the sign wrong fails
 * loudly rather than writing a wrong number — which is the only reason two copies are tolerable.
 */

/** Which way the count is moving. ⚠ The operator picks this; they never type a sign. */
export type StockDirection = "writeOff" | "add";

/** What `POST /api/v1/stock/movements` is given. ⚠ `qty` is the signed CHANGE, never a total. */
export interface StockMovementDraft {
  type: "WriteOff" | "Adjustment";
  qty: number;
  reason: string;
}

export type StockMovementResult =
  | { ok: true; movement: StockMovementDraft }
  | { ok: false; problem: string };

/**
 * ⚠⚠ STRICT DIGITS, NOT `parseInt`. `parseInt("2.5")` is `2` and `parseInt("3 apples")` is `3`, so a
 * till built on it would write off a number the operator never typed and report success. MAUI uses
 * `int.TryParse`, which **fails** on both — this regex is what keeps the two the same.
 *
 * ⚠ No sign is accepted either. A "-3" typed into *"write off how many"* would flip the direction
 * the operator just chose, which is the one mistake this whole flow is arranged to prevent.
 */
const WHOLE_NUMBER = /^\d+$/;

/** Where the amount is at all usable — exported so the dialog can enable its Save button. */
export function isUsableAmount(typed: string): boolean {
  const t = typed.trim();
  return WHOLE_NUMBER.test(t) && Number(t) > 0;
}

/**
 * Build the movement, or say what is wrong with it.
 *
 * ⚠ Both problems are returned as sentences an operator can act on, and both end with "Nothing has
 * been changed." — at a counter, the first question after a refusal is whether it half-happened.
 */
export function buildStockMovement(
  direction: StockDirection,
  typedAmount: string,
  typedReason: string,
): StockMovementResult {
  const amount = typedAmount.trim();
  if (!isUsableAmount(amount)) {
    return {
      ok: false,
      problem: "Enter how many, as a whole number more than zero. Nothing has been changed.",
    };
  }

  // ⚠ A REASON IS COMPULSORY and the server refuses without one, so asking here saves a round trip
  // to be told so. An unexplained stock correction cannot be told apart from shrinkage being hidden,
  // which is the whole reason this sits at supervisor level rather than cashier.
  const reason = typedReason.trim();
  if (reason.length === 0) {
    return { ok: false, problem: "Say why the count is changing. Nothing has been changed." };
  }

  const howMany = Number(amount);

  // ⚠⚠ THE SIGN COMES FROM THE CHOICE. A `WriteOff` must be negative or the server refuses it, and
  // asking somebody to get a minus sign right on a stock ledger at a counter is asking for the wrong
  // answer.
  return {
    ok: true,
    movement: direction === "writeOff"
      ? { type: "WriteOff", qty: -howMany, reason }
      : { type: "Adjustment", qty: howMany, reason },
  };
}

/**
 * The sentence above the amount box.
 *
 * ⚠⚠ "HOW MANY", NEVER "THE NEW TOTAL", AND THE SCREEN HAS TO SAY SO. The server does
 * `level.Quantity += qtyDelta`, so a box the operator reads as "quantity" filled in with what they
 * just counted ADDS their count to the existing one: 7 on the shelf, operator counts 7, stock becomes
 * 14, nothing errors, and nobody finds out until a stock take. Naming the current figure in the same
 * breath is what makes the difference visible.
 */
export function amountHint(name: string, currentStock: number | null): string {
  const shows = currentStock == null ? "no stock record yet" : `${currentStock} in stock`;
  return `“${name}” currently shows ${shows}. This changes the count by the number you type — it is not the new total.`;
}

/** Confirmation, after the ledger has taken it. */
export function adjustedMessage(direction: StockDirection, howMany: number, name: string): string {
  return direction === "writeOff"
    ? `Wrote off ${howMany} × “${name}”.`
    : `Added ${howMany} × “${name}”.`;
}
