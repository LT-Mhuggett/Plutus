import { useEffect } from "react";
import { businessName, getReceiptTemplateCached } from "../api.ts";
import { gbp } from "../money.ts";
import { lineDiscountPence, lineTotalPence, type BasketLine } from "./basket.ts";
import Barcode39 from "./Barcode39.tsx";

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

/** Browser-print receipt — the PDF/hardware-agent story (plan §3.5) comes later. */
export default function Receipt({ data, onClose, autoPrint }: Props) {
  useEffect(() => {
    if (autoPrint) {
      const t = setTimeout(() => window.print(), 250); // let the dialog paint first
      return () => clearTimeout(t);
    }
  }, [autoPrint]);

  // WP11.2: per-store template (header/footer lines + toggles), cached from the last catalogue
  // sync. Sensible defaults preserve today's receipt when no template is set.
  const tpl = getReceiptTemplateCached();
  const headerLines = tpl?.headerLines?.length ? tpl.headerLines : ["Thank you for shopping with us"];
  const footerLines = tpl?.footerLines ?? [];
  const showBarcode = tpl?.showBarcode !== false;

  return (
    <div className="overlay receipt-overlay">
      <div className="dialog receipt-dialog">
        <div className="receipt" id="receipt">
          <h3>{businessName()}</h3>
          {headerLines.map((l, i) => <p className="centre small" key={i}>{l}</p>)}
          <p className="centre small">{new Date(data.date).toLocaleString("en-GB")}</p>
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
              <Barcode39 value={data.saleId} />
            </div>
          )}
          <p className="centre mono tiny">{data.saleId}</p>
        </div>

        <div className="dialog-actions no-print">
          <button className="ghost" onClick={onClose}>
            Close
          </button>
          <button className="primary" onClick={() => window.print()}>
            Print
          </button>
        </div>
      </div>
    </div>
  );
}
