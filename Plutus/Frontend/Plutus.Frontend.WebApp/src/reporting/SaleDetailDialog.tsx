import { useEffect, useState } from "react";
import DialogX from "../DialogX.tsx";
import { fetchSaleDetail, type SaleDetail } from "../api.ts";
import { gbp } from "../money.ts";
import Receipt, { type ReceiptData } from "../till/Receipt.tsx";
import { printOnReceiptPrinter } from "../till/receiptPrint.ts";
import { apiDateTime } from "../apiTime.ts";

const p = (pounds: number) => Math.round(pounds * 100);

/** Recall view: everything recorded against one sale, with a copy-receipt reprint. */
export default function SaleDetailDialog({ saleId, onClose }: { saleId: string; onClose: () => void }) {
  const [detail, setDetail] = useState<SaleDetail | null>(null);
  const [error, setError] = useState("");
  const [reprint, setReprint] = useState(false);
  /** W-P6: set once the copy has gone to the thermal printer, so the browser dialog is not also raised. */
  const [notice, setNotice] = useState("");
  const [printing, setPrinting] = useState(false);

  /**
   * W-P6 — send the copy to the RECEIPT PRINTER, falling back to the browser dialog.
   *
   * ⚠⚠ WHY THIS WAS THE GAP. This dialog could already reprint, but only through `window.print()` —
   * the browser's own print path. MAUI sends a reprint to the thermal printer via the agent (till
   * 1.34.0), so "reprint a past receipt" was a capability the web till half had: a shop with a
   * receipt printer could not hand a customer their paper again from the browser till.
   *
   * ⚠⚠ THE DRAWER MUST NOT KICK — `openDrawer: false`, the third argument. No money is moving. A
   * reprint that opened the drawer would teach operators that the drawer opening means nothing, which
   * is worse than it not opening at all. Same rule MAUI's `ReceiptReprint` states.
   *
   * ⚠ THE COPY IS MARKED — `receiptData` puts `(COPY)` on the sale id, and that is a MONEY rule, not a
   * courtesy: this platform's refund flow finds a sale by the barcode on a receipt, so two
   * indistinguishable papers for one purchase is the shape of a double refund.
   *
   * ⚠ Any failure falls through to the browser receipt, exactly as a sale receipt does — a reprint must
   * never be held up by hardware.
   */
  async function printCopy(d: SaleDetail) {
    setPrinting(true);
    setNotice("");
    try {
      // ⚠ `printOnReceiptPrinter` — the ONE rule (`till/receiptPrint.ts`, `till-design.md` D6b). This
      // was the FIRST of three inline copies, and its own header above records finding the same fault
      // here on 2026-08-17. The copy in `Receipt.tsx` kept sending receipts to the A4 printer for four
      // more days, because nothing pointed from this fix to the others.
      if (await printOnReceiptPrinter(receiptData(d))) {
        setNotice("Copy receipt printed.");
        return;
      }
      // No agent, or it refused — the browser dialog is the fallback, as before.
      setReprint(true);
    } finally {
      setPrinting(false);
    }
  }

  useEffect(() => {
    fetchSaleDetail(saleId)
      .then(setDetail)
      .catch((e) => setError(String(e)));
  }, [saleId]);

  function receiptData(d: SaleDetail): ReceiptData {
    return {
      saleId: `${d.id} (COPY)`,
      date: d.dateOfSale,
      lines: d.lines.map((l, i) => ({
        key: i + 1,
        item: { idOne: l.itemId, name: l.name, brand: "-", desc: "", cost: 0, exPrice: l.unitExPrice, price: l.unitPrice, taxId: 0, catId: "" },
        quantity: l.quantity,
        pricePence: p(l.unitPrice),
        exPricePence: p(l.unitExPrice),
        adjusted: l.priceAdjusted,
        discount: l.discounts[0]
          ? { discountId: 0, name: l.discounts[0].name, type: 1, amount: l.discounts[0].rate }
          : undefined,
      })),
      totalPence: p(d.total),
      totalExTaxPence: p(d.totalExTax),
      payments: d.payments.map((pay) => ({ name: pay.method, amountPence: p(pay.amount), changePence: p(pay.change) })),
    };
  }

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog wide">
        <h2>Sale detail</h2>
        <DialogX onClose={onClose} />
        {error && <p className="error small">{error}</p>}
        {notice && <p className="muted small">{notice}</p>}
        {!detail && !error && <p className="muted">Loading…</p>}

        {detail && (
          <>
            <dl className="env-info">
              <dt>Date</dt>
              <dd>{apiDateTime(detail.dateOfSale)}</dd>
              <dt>Served by</dt>
              <dd>{detail.employee ?? "—"}</dd>
              <dt>Sale id</dt>
              <dd className="mono small">{detail.id}</dd>
            </dl>

            <h3 className="settings-h">Items</h3>
            <table>
              <thead>
                <tr>
                  <th>Item</th>
                  <th className="num">Qty</th>
                  <th className="num">Unit</th>
                  <th className="num">Line</th>
                </tr>
              </thead>
              <tbody>
                {detail.lines.map((l, i) => (
                  <tr key={i}>
                    <td>
                      {l.name}
                      <span className="mono muted small barcode">{l.itemId}</span>
                      {l.priceAdjusted && <span className="adj-mark" title="price adjusted at the till"> *</span>}
                      {l.discounts.map((d) => (
                        <div key={d.name} className="small discount-note">
                          {d.name}
                          {d.rate > 0 ? ` (${Math.round(d.rate * 100)}%)` : ""}
                        </div>
                      ))}
                    </td>
                    <td className="num">{l.quantity}</td>
                    <td className="num">{gbp(p(l.unitPrice))}</td>
                    <td className="num">{gbp(p(l.unitPrice * l.quantity))}</td>
                  </tr>
                ))}
              </tbody>
            </table>

            {detail.refunds.length > 0 && (
              <>
                <h3 className="settings-h">Returns in this sale</h3>
                <table>
                  <tbody>
                    {detail.refunds.map((r, i) => (
                      <tr key={i}>
                        <td>
                          <span className="return-tag">RETURN</span> {r.name} ×{r.quantity}
                          <div className="muted small">
                            {r.reason} — original sale <span className="mono">{r.originalSaleId}</span>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </>
            )}

            <h3 className="settings-h">Payment</h3>
            <table>
              <tbody>
                {detail.payments.map((pay, i) => (
                  <tr key={i}>
                    <td>{pay.method}</td>
                    <td className="num">{gbp(p(pay.amount))}</td>
                    <td className="num muted">{pay.change > 0 ? `change ${gbp(p(pay.change))}` : ""}</td>
                  </tr>
                ))}
                <tr>
                  <td>
                    <strong>Total</strong> <span className="muted small">(ex VAT {gbp(p(detail.totalExTax))})</span>
                  </td>
                  <td className="num">
                    <strong>{gbp(p(detail.total))}</strong>
                  </td>
                  <td />
                </tr>
              </tbody>
            </table>

            {detail.notes.length > 0 && (
              <>
                <h3 className="settings-h">Notes</h3>
                {detail.notes.map((n, i) => (
                  <p key={i} className="small">
                    {n}
                  </p>
                ))}
              </>
            )}
          </>
        )}

        <div className="dialog-actions">
          <button className="ghost" onClick={onClose}>
            Close
          </button>
          {detail && (
            <button className="primary" disabled={printing} onClick={() => void printCopy(detail)}>
              {printing ? "Printing…" : "Print copy receipt"}
            </button>
          )}
        </div>
      </div>
      {reprint && detail && <Receipt data={receiptData(detail)} onClose={() => setReprint(false)} />}
    </div>
  );
}
