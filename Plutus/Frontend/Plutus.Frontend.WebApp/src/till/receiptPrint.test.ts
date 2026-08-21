import { describe, expect, it, vi } from "vitest";
import { printOnReceiptPrinter, type ReceiptPrinterDeps } from "./receiptPrint.ts";
import type { ReceiptData } from "./Receipt.tsx";

/**
 * ⚠⚠ THE BUG THESE EXIST FOR. Matt, 2026-08-21: *"if you complete a sale and print no receipt, the
 * receipt is shown (do not change) but if you try to print it from the next screen, it comes out on
 * the A4 printer, not the receipt printer. It should always default to the receipt printer."*
 *
 * `Receipt.tsx`'s Print button called `window.print()` unconditionally — the browser path, which goes
 * to the OS default printer. In a shop that is the office A4.
 *
 * ⚠⚠ AND THE SAME FAULT HAD ALREADY BEEN FOUND AND FIXED IN ANOTHER DIALOG FOUR DAYS EARLIER (W-P6,
 * `SaleDetailDialog`). Three inline copies of one rule, one of them wrong, nothing pointing between
 * them. **The rule now exists once and is tested once** — which is the actual fix; the button was
 * only the symptom.
 *
 * ⚠ These do not and cannot test the BUTTON — this project has no jsdom and no testing-library, so a
 * React component cannot be rendered here. What they pin is the decision the button now delegates.
 * The button itself is `§G72`, by hand.
 */
const DATA: ReceiptData = {
  saleId: "0199-abc",
  date: "2026-08-21T09:00:00Z",
  lines: [],
  totalPence: 1000,
  totalExTaxPence: 833,
  payments: [{ name: "Cash", amountPence: 1000, changePence: 0 }],
};

const deps = (over: Partial<ReceiptPrinterDeps>): ReceiptPrinterDeps => ({
  agentAvailable: vi.fn().mockResolvedValue({ columns: 42 }),
  printDocument: vi.fn().mockResolvedValue(true),
  ...over,
} as ReceiptPrinterDeps);

describe("printOnReceiptPrinter", () => {
  it("sends the receipt to the agent and reports that paper came out", async () => {
    const printDocument = vi.fn().mockResolvedValue(true);

    expect(await printOnReceiptPrinter(DATA, deps({ printDocument }))).toBe(true);
    expect(printDocument).toHaveBeenCalledOnce();
  });

  /**
   * ⚠⚠ THE ONE THAT PINS THE REPORTED BUG. No agent means **false** — *"nothing came out"* — so the
   * caller falls back deliberately. It must never report success and leave the operator believing a
   * receipt exists.
   */
  it("no agent means nothing came out, and the document is never built", async () => {
    const printDocument = vi.fn();
    const d = deps({ agentAvailable: vi.fn().mockResolvedValue(null), printDocument });

    expect(await printOnReceiptPrinter(DATA, d)).toBe(false);
    expect(printDocument).not.toHaveBeenCalled();
  });

  /** ⚠ A paired agent that refuses the job — printer off, out of paper, wrong token. */
  it("an agent that refuses the job means nothing came out", async () => {
    expect(await printOnReceiptPrinter(DATA, deps({
      printDocument: vi.fn().mockResolvedValue(false),
    }))).toBe(false);
  });

  /**
   * ⚠⚠ NEVER THROWS, and this is the load-bearing one. Every caller reaches this **after the sale is
   * committed**, so an exception escaping here would break a screen over a piece of paper — with the
   * money already taken and a customer at the counter.
   */
  it("never throws, whichever half fails", async () => {
    expect(await printOnReceiptPrinter(DATA, deps({
      agentAvailable: vi.fn().mockRejectedValue(new Error("agent socket died")),
    }))).toBe(false);

    expect(await printOnReceiptPrinter(DATA, deps({
      printDocument: vi.fn().mockRejectedValue(new Error("print threw")),
    }))).toBe(false);
  });

  /**
   * ⚠⚠ THE DRAWER MUST NOT KICK. `receiptToDocument`'s third argument is `openDrawer`, and every path
   * through here is either a reprint or a receipt asked for after the fact — the drawer was already
   * kicked once by `TillPage` when a cash sale committed. A drawer that opens when nothing is
   * happening teaches operators that the drawer opening means nothing.
   *
   * Checked on the built document rather than on the call arguments, because the document is what
   * reaches the agent — `openDrawer` is a top-level flag on `PrintDocument`, not an op in `ops`.
   */
  it("never asks the agent to open the drawer", async () => {
    let sent: unknown;
    await printOnReceiptPrinter(DATA, deps({
      printDocument: vi.fn().mockImplementation((doc: unknown) => { sent = doc; return Promise.resolve(true); }),
    }));

    expect((sent as { openDrawer: boolean }).openDrawer).toBe(false);
  });

  /** ⚠ The agent reports its own paper width; a hardcoded 42 would wrap wrongly on a 32-col printer. */
  it("honours the width the agent reports, and defaults to 42 when it says nothing", async () => {
    const widths: number[] = [];
    const capture = vi.fn().mockImplementation((doc: unknown) => {
      widths.push((doc as { columns: number }).columns);
      return Promise.resolve(true);
    });

    await printOnReceiptPrinter(DATA, deps({
      agentAvailable: vi.fn().mockResolvedValue({ columns: 32 }), printDocument: capture,
    }));
    await printOnReceiptPrinter(DATA, deps({
      agentAvailable: vi.fn().mockResolvedValue({}), printDocument: capture,
    }));

    expect(widths).toEqual([32, 42]);
  });
});
