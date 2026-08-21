// Where a receipt goes: the RECEIPT PRINTER, or nowhere.
//
// ⚠⚠ WHY THIS FILE EXISTS, 2026-08-21. Matt: *"if you complete a sale and print no receipt, the
// receipt is shown (do not change) but if you try to print it from the next screen, it comes out on
// the A4 printer, not the receipt printer. It should always default to the receipt printer."*
//
// The cause was `Receipt.tsx`'s Print button calling `window.print()` unconditionally — the browser's
// print path, which goes to the OS default printer, and in a shop that is the office A4. An operator
// who declined the receipt at the ask and then changed their mind sent a customer's receipt to another
// room.
//
// ⚠⚠ AND THE FIX ALREADY EXISTED, IN A DIFFERENT DIALOG. W-P6 closed the identical bug in
// `reporting/SaleDetailDialog.tsx` on 2026-08-17, whose own header says *"this dialog could already
// reprint, but only through `window.print()`"*. `TillPage` had the agent-first logic too. **Three
// copies of one rule, one of them wrong, and nothing pointing between them** — so the third copy
// survived four days after the second was fixed.
//
// This is the rule, once. `till-design.md` D6b lists its callers.
//
// ⚠ IT DECIDES ONE THING ONLY: *did paper come out of the receipt printer?* It deliberately does NOT
// own the fallback, because the three callers legitimately differ — the receipt dialog falls back to
// the browser dialog, the sale-detail dialog falls back to rendering a browser receipt, and the
// post-sale path falls back to *showing* the receipt with auto-print armed. Folding those together
// would be the wrong kind of sharing.

import { agentAvailable, printDocument } from "../hardware.ts";
import { receiptToDocument } from "./receiptDoc.ts";
import type { ReceiptData } from "./Receipt.tsx";

/** Injectable seams — the default is the real agent, and tests pass fakes. */
export interface ReceiptPrinterDeps {
  agentAvailable: typeof agentAvailable;
  printDocument: typeof printDocument;
}

const REAL: ReceiptPrinterDeps = { agentAvailable, printDocument };

/**
 * Try to put this receipt on the thermal printer.
 *
 * @returns `true` only when the agent confirmed the job. `false` means *"nothing came out"* — no
 *          agent paired, the agent refused, the printer is off, or the call threw. The caller then
 *          does whatever its own fallback is.
 *
 * ⚠⚠ NEVER THROWS. A receipt is not worth stranding somebody at a counter, and every caller reaches
 * this **after** the sale is committed — so an exception here would break a screen over a piece of
 * paper. Both agent calls are already documented as non-throwing; the `catch` is for the day one of
 * them changes.
 *
 * ⚠ `openDrawer: false` ALWAYS, and it is not this function's decision to revisit. The drawer is
 * kicked once, by `TillPage`, at the moment a cash sale commits. Every path through here is either a
 * reprint or a receipt the operator asked for after the fact, and neither moves money — a drawer that
 * opens when nothing is happening teaches operators that the drawer opening means nothing.
 *
 * ⚠ NOTHING HERE MARKS A COPY. `(COPY)` is a caller's business, and only one caller wants it:
 * `SaleDetailDialog` stamps it because this platform finds a sale to refund by the barcode on a
 * receipt, so two indistinguishable papers for one purchase is the shape of a double refund. The
 * receipt dialog must NOT stamp it — the operator declined the ask, so its paper is the first one, and
 * calling it a copy would be a lie on the document the customer keeps.
 */
export async function printOnReceiptPrinter(
  data: ReceiptData,
  deps: ReceiptPrinterDeps = REAL,
): Promise<boolean> {
  try {
    const agent = await deps.agentAvailable();
    if (!agent) return false;

    return await deps.printDocument(
      receiptToDocument(data, agent.columns ?? 42, false),
    );
  } catch {
    return false;
  }
}
