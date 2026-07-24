import { useEffect, useState } from "react";
import { fetchSaleDetail, type SaleDetail } from "../api.ts";
import { gbp } from "../money.ts";
import Receipt, { type ReceiptData } from "../till/Receipt.tsx";

const p = (pounds: number) => Math.round(pounds * 100);

/** Recall view: everything recorded against one sale, with a copy-receipt reprint. */
export default function SaleDetailDialog({ saleId, onClose }: { saleId: string; onClose: () => void }) {
  const [detail, setDetail] = useState<SaleDetail | null>(null);
  const [error, setError] = useState("");
  const [reprint, setReprint] = useState(false);

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
        {error && <p className="error small">{error}</p>}
        {!detail && !error && <p className="muted">Loading…</p>}

        {detail && (
          <>
            <dl className="env-info">
              <dt>Date</dt>
              <dd>{new Date(detail.dateOfSale).toLocaleString("en-GB")}</dd>
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
            <button className="primary" onClick={() => setReprint(true)}>
              Print copy receipt
            </button>
          )}
        </div>
      </div>
      {reprint && detail && <Receipt data={receiptData(detail)} onClose={() => setReprint(false)} />}
    </div>
  );
}
