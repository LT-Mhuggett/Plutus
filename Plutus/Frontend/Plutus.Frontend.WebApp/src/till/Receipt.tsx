import { useEffect } from "react";
import { businessName, getReceiptTemplateCached } from "../api.ts";
import { getSession } from "../session.ts";
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
  useEffect(() => {
    if (autoPrint) {
      const t = setTimeout(() => window.print(), 250); // let the dialog paint first
      return () => clearTimeout(t);
    }
  }, [autoPrint]);

  return (
    <div className="overlay receipt-overlay">
      <div className="dialog receipt-dialog">
        <ReceiptBody data={data} />

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
