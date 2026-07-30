import { useEffect, useState } from "react";
import { fetchSaleDetail, gbp, type SaleDetail } from "./api.ts";
import Barcode39 from "./Barcode39.tsx";

// One sale, fully expanded: details view + a printable copy-receipt with its scannable barcode.
// Extracted from Dashboard.tsx (WP3.2) so both the Dashboard drill-down and the Reporting→Custom
// report open the same dialog.

/** A past sale rendered as a printable receipt with its scannable barcode. */
function ReceiptView({ id, sale }: { id: string; sale: SaleDetail }) {
  return (
    <div className="receipt-view" id="receipt-print">
      <p className="centre small">{new Date(sale.occurredAtUtc + "Z").toLocaleString("en-GB")}</p>
      <hr />
      <table className="receipt-lines">
        <tbody>
          {sale.lines.map((l) => (
            <tr key={l.lineNo}>
              <td>{l.qty} × {l.itemName ?? ""}</td>
              <td className="num">{gbp(l.unitPricePence)}</td>
              <td className="num">{l.discountPence ? `−${gbp(l.discountPence)}` : ""}</td>
              <td className="num">{gbp(l.lineGrossPence)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <hr />
      <p className="r-total"><span>Total</span><span>{gbp(sale.grossPence)}</span></p>
      <p className="small"><span>VAT</span> <span>{gbp(sale.vatPence)}</span></p>
      <hr />
      <p className="centre small">{sale.tenders.map((t) => `${t.tenderType} ${gbp(t.amountPence)}`).join(" · ")}</p>
      <div className="centre"><Barcode39 value={id} fit showText={false} /></div>
      <p className="centre mono tiny">{id}</p>
    </div>
  );
}

export default function SaleDialog({ id, onClose }: { id: string; onClose: () => void }) {
  const [sale, setSale] = useState<SaleDetail | null>(null);
  const [error, setError] = useState("");
  const [asReceipt, setAsReceipt] = useState(false);
  useEffect(() => {
    fetchSaleDetail(id).then(setSale).catch((e) => setError(String(e)));
  }, [id]);
  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog">
        <div className="r-row">
          <h3 className="grow">Sale {id.slice(0, 8)}…</h3>
          {sale && <button className="ghost small" onClick={() => setAsReceipt((v) => !v)}>{asReceipt ? "Details" : "View receipt"}</button>}
        </div>
        {error && <p className="error small">{error}</p>}
        {sale && asReceipt && <ReceiptView id={id} sale={sale} />}
        {sale && !asReceipt && (
          <>
            <dl className="kv">
              <dt>When</dt><dd>{new Date(sale.occurredAtUtc + "Z").toLocaleString("en-GB")} (day {sale.businessDay})</dd>
              <dt>Channel</dt><dd>{sale.channel} · seq {sale.deviceSeq}{sale.legacyRef ? ` · legacy ${sale.legacyRef}` : ""}</dd>
              <dt>Gross / VAT</dt><dd>{gbp(sale.grossPence)} / {gbp(sale.vatPence)}{sale.vatReconstructed ? " (VAT reconstructed)" : ""}</dd>
              {sale.note && (<><dt>Note</dt><dd>{sale.note}</dd></>)}
            </dl>
            <table>
              <thead><tr><th>#</th><th>Item</th><th>Qty</th><th className="num">Unit</th><th className="num">Disc</th><th className="num">Gross</th><th className="num">VAT</th></tr></thead>
              <tbody>
                {sale.lines.map((l) => (
                  <tr key={l.lineNo}>
                    <td>{l.lineNo}</td><td>{l.itemName ?? l.itemIdOne ?? "—"}</td><td>{l.qty}</td>
                    <td className="num">{gbp(l.unitPricePence)}</td>
                    <td className="num">{l.discountPence ? gbp(l.discountPence) : "—"}</td>
                    <td className="num">{gbp(l.lineGrossPence)}</td>
                    <td className="num">{gbp(l.vatAmountPence)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            <p className="small muted">
              Paid: {sale.tenders.map((t) => `${t.tenderType} ${gbp(t.amountPence)}${t.changePence ? ` (change ${gbp(t.changePence)})` : ""}`).join(", ")}
            </p>
          </>
        )}
        <div className="dialog-actions">
          {sale && asReceipt && <button className="ghost" onClick={() => window.print()}>Print / reprint</button>}
          <button className="ghost" onClick={onClose}>Close</button>
        </div>
      </div>
    </div>
  );
}
