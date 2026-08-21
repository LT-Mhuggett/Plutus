import { useEffect, useState } from "react";
import { createPortal } from "react-dom";
import { businessName, getReceiptTemplateCached } from "../api.ts";
import { getSession } from "../session.ts";
import { gbp } from "../money.ts";
import { lineDiscountPence, lineTotalPence, type BasketLine } from "./basket.ts";
import Barcode39 from "./Barcode39.tsx";
import DialogX from "../DialogX.tsx";
import { printOnReceiptPrinter } from "./receiptPrint.ts";

export interface ReceiptData {
  saleId: string;
  date: string;
  lines: BasketLine[];
  totalPence: number;
  totalExTaxPence: number;
  payments: { name: string; amountPence: number; changePence: number }[];
  /** sale was taken offline and is queued for sync */
  queued?: boolean;
}

interface Props {
  data: ReceiptData;
  onClose: () => void;
  autoPrint?: boolean;
}

/**
 * The receipt itself, sans dialog — what actually prints. Also rendered inline by the
 * Settings → Printer preview, so what the operator sees there IS this markup, not a copy.
 */
export function ReceiptBody({ data }: { data: ReceiptData }) {
  // WP11.2 + NatApp receipt port: per-store EFFECTIVE template (store name, address, phone,
  // VAT, header/footer + toggles — api.ts merges the store's real details into blank fields).
  const tpl = getReceiptTemplateCached();
  const headerLines = tpl?.headerLines?.length ? tpl.headerLines : ["Thank you for shopping with us"];
  const footerLines = tpl?.footerLines ?? [];
  const addressLines = tpl?.addressLines ?? [];
  const showBarcode = tpl?.showBarcode !== false;
  const operator = tpl?.showOperator ? getSession()?.name : null;

  // (the old id="receipt" was unreferenced; dropped so the inline preview can't duplicate it)
  return (
    <div className="receipt">
          {/* NatApp header order: thank-you, shop name, phone, address, VAT no, date */}
          {headerLines.map((l, i) => <p className="centre small" key={`h${i}`}>{l}</p>)}
          <h3>{tpl?.storeName || businessName()}</h3>
          {tpl?.phone && <p className="centre small">{tpl.phone}</p>}
          {addressLines.map((l, i) => <p className="centre small" key={`a${i}`}>{l}</p>)}
          {tpl?.showVatNumber && tpl?.vatNumber && <p className="centre small">VAT No: {tpl.vatNumber}</p>}
          <p className="centre small">{new Date(data.date).toLocaleString("en-GB")}</p>
          {operator && <p className="centre small">Served by {operator}</p>}
          {data.totalPence < 0 && <p className="centre"><strong>** REFUND **</strong></p>}
          <hr />
          {data.lines.map((l) => (
            <div key={l.key}>
              <div className="r-row">
                <span className="grow">
                  {l.isReturn ? "RETURN — " : ""}
                  {l.item.name}
                </span>
              </div>
              <div className="r-row small">
                <span className="grow">
                  {l.quantity} × {gbp(l.pricePence)}
                </span>
                <span>{gbp(lineTotalPence(l))}</span>
              </div>
              {l.discount && (
                <div className="r-row small">
                  <span className="grow">{l.discount.name}</span>
                  <span>−{gbp(lineDiscountPence(l))}</span>
                </div>
              )}
            </div>
          ))}
          <hr />
          <div className="r-row small">
            <span className="grow">Subtotal (ex tax)</span>
            <span>{gbp(data.totalExTaxPence)}</span>
          </div>
          <div className="r-row small">
            <span className="grow">Tax</span>
            <span>{gbp(data.totalPence - data.totalExTaxPence)}</span>
          </div>
          <div className="r-row total">
            <span className="grow">Total</span>
            <span>{gbp(data.totalPence)}</span>
          </div>
          <hr />
          {data.payments.map((p, i) => (
            <div className="r-row small" key={i}>
              <span className="grow">{p.name}</span>
              <span>{gbp(p.amountPence)}</span>
            </div>
          ))}
          {data.payments.some((p) => p.changePence > 0) && (
            <div className="r-row small">
              <span className="grow">Change</span>
              <span>{gbp(data.payments.reduce((c, p) => c + p.changePence, 0))}</span>
            </div>
          )}
          <hr />
          {footerLines.map((l, i) => <p className="centre small" key={i}>{l}</p>)}
          {data.queued && <p className="centre small">* taken offline — will sync automatically *</p>}
          {showBarcode && !data.saleId.startsWith("test-print") && (
            <div className="centre receipt-barcode">
              {/* fit: a 36-char UUID at natural size is ~1200px wide — scale to the receipt.
                  The human-readable id is printed just below, so no in-barcode text. */}
              <Barcode39 value={data.saleId} fit showText={false} />
            </div>
          )}
          <p className="centre mono tiny">{data.saleId}</p>
    </div>
  );
}

/** Browser-print receipt — the PDF/hardware-agent story (plan §3.5) comes later. */
export default function Receipt({ data, onClose, autoPrint }: Props) {
  /** "idle" = nothing tried yet · "busy" = the agent is being asked · then what happened.
   *  ⚠ `fellback` is shown, not hidden: an operator who thinks paper is coming out of the till and
   *  finds it in the office needs to be told which happened. */
  const [printState, setPrintState] = useState<"idle" | "busy" | "printed" | "fellback">("idle");

  /**
   * ⚠⚠ THE RECEIPT PRINTER FIRST, THE BROWSER ONLY AS A FALLBACK.
   *
   * Matt, 2026-08-21: *"if you complete a sale and print no receipt, the receipt is shown (do not
   * change) but if you try to print it from the next screen, it comes out on the A4 printer, not the
   * receipt printer. It should always default to the receipt printer."*
   *
   * ⚠ THE BUG WAS THIS BUTTON CALLING `window.print()` UNCONDITIONALLY. The browser's print path goes
   * to the OS default printer, which in a shop is the office A4 — so an operator who declined the
   * receipt at the ask and then changed their mind got a customer's receipt on A4 in another room.
   * `TillPage` had always sent the post-sale receipt through the agent; this dialog never learned to.
   *
   * ⚠⚠ AND THE SAME FAULT WAS FOUND AND FIXED ONCE ALREADY, IN THE OTHER DIALOG. W-P6's
   * `SaleDetailDialog.printCopy` carries the note *"this dialog could already reprint, but only
   * through `window.print()`"* — the identical bug, in a file three directories away, closed on
   * 2026-08-17, while `TillPage` had the agent-first logic too. **Three copies of one rule, one of
   * them wrong, and nothing pointing between them.** The rule now lives once, in `receiptPrint.ts`,
   * and `till-design.md` **D6b** lists its callers — so the next surface that needs it cannot quietly
   * grow a fourth copy.
   *
   * ⚠ NOT MARKED `(COPY)`, deliberately — unlike `SaleDetailDialog`. The operator declined the ask, so
   * **no paper exists yet**: this is the first receipt, not a duplicate, and stamping it a copy would
   * be a lie on the one document the customer keeps. ⚠ Pressing Print twice does produce two identical
   * papers, which is the double-refund shape C1 worries about — but that is pre-existing on this dialog
   * (the browser dialog could always be run twice) and the platform already caps a refund per tender at
   * what the original sale took (finding Y). **Recorded, not silently changed.**
   *
   * ⚠ The fallback stays the browser dialog, so a shop with no agent behaves exactly as before.
   */
  async function print() {
    if (printState === "busy") return;   // ⚠ the agent call has a 15s timeout — a double-tap prints twice
    setPrintState("busy");

    if (await printOnReceiptPrinter(data)) {
      setPrintState("printed");
      return;
    }

    setPrintState("fellback");
    window.print();
  }

  useEffect(() => {
    if (autoPrint) {
      // ⚠ `window.print()` is CORRECT here. `autoPrint` is set only when the operator asked for a
      // receipt AND the agent already failed to produce one (`TillPage`: `wantsReceipt &&
      // !printedOnPaper`), so the agent has been tried and this is the fallback, not the default.
      const t = setTimeout(() => window.print(), 250); // let the dialog paint first
      return () => clearTimeout(t);
    }
  }, [autoPrint]);

  // ⚠ ESCAPE CLOSES IT — till-design D4 rule 2. Until 2026-08-19 this dialog had NO exit but the
  // Close button: no ✕, no Escape, and no overlay click (see below). One button is not a trap, but it
  // is the only dialog on this till where a single element failing to render would leave an operator
  // stuck behind a receipt with a queue in front of them.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") onClose(); };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  // Portalled to <body>, NOT rendered inside #root: print CSS removes the whole app
  // (#root) while a receipt is open, so the printed document is only as tall as the
  // receipt. Rendered inline, the page inherited the app's full height and a roll
  // printer fed a long blank tail after the receipt.
  // ⚠ NO OVERLAY-CLICK DISMISSAL HERE, and that is a DELIBERATE D4 rule-3 exemption — the one dialog
  // on this till that keeps it. A stray tap beside the receipt would throw it away before it has been
  // printed, and the operator's next move is to hunt for a reprint. D4 allows a deliberately-modal
  // dialog provided rules 1 and 2 ARE met, which is why the ✕ and Escape were added at the same time:
  // three explicit exits, no accidental one.
  return createPortal(
    <div className="overlay receipt-overlay">
      <div className="dialog receipt-dialog">
        {/* ⚠ OUTSIDE `.receipt`, WHICH IS WHAT KEEPS IT OFF THE PAPER. Print CSS hides everything
            (`body * { visibility: hidden }`) and only `.receipt`/`.member-card` subtrees opt back in,
            so a ✕ placed here cannot appear on a customer's receipt. Nesting it inside `ReceiptBody`
            would print it. */}
        <DialogX onClose={onClose} />
        <ReceiptBody data={data} />

        {/* ⚠ `no-print` — the print CSS hides everything outside `.receipt`, but this sits inside the
            dialog rather than the receipt subtree, so it is belt and braces on the one document a
            customer keeps. */}
        {printState === "printed" && (
          <p className="small centre no-print">✅ Printed on the receipt printer.</p>
        )}
        {printState === "fellback" && (
          <p className="small centre no-print">
            ⚠ No receipt printer answered — this went to the browser's printer instead. Check the
            hardware agent on Settings.
          </p>
        )}

        <div className="dialog-actions no-print">
          <button className="ghost" onClick={onClose}>
            Close
          </button>
          <button className="primary" onClick={() => void print()} disabled={printState === "busy"}>
            {printState === "busy" ? "Printing…" : "Print"}
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
