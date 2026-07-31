import { businessName, getReceiptTemplateCached } from "../api.ts";
import { getSession } from "../session.ts";
import { gbp } from "../money.ts";
import { lineDiscountPence, lineTotalPence } from "./basket.ts";
import type { ReceiptData } from "./Receipt.tsx";

// ─────────────────────────────────────────────────────────────────────────────
// FE3: ReceiptData → the agent's print-op document.
//
// ⚠ The layout lives HERE, not in the agent, and deliberately mirrors Receipt.tsx line for line.
// The per-store template (header/footer lines, address, VAT number, barcode toggle) is cached in
// the till; re-implementing it inside the agent would mean two copies to keep in step, and the
// first template change would print the old receipt on paper and the new one on PDF.
//
// The op vocabulary matches CommonPOSLibrary.PrinterBaseOperations, which the Xamarin/MAUI tills
// already speak — see tools/Plutus.TillAgent.Core/PrintOps.cs.
// ─────────────────────────────────────────────────────────────────────────────

export const OP_TEXT = 0;
export const OP_RULE = 1;
export const OP_BARCODE = 2;
export const OP_CUT = 3;
export const OP_DRAWER = 4;

export const ALIGN_LEFT = 0;
export const ALIGN_CENTRE = 1;
export const ALIGN_RIGHT = 2;

export interface PrintOp {
  kind: number;
  text?: string;
  align?: number;
  bold?: boolean;
  underline?: boolean;
  large?: boolean;
  height?: number;
}

export interface PrintDocument {
  ops: PrintOp[];
  columns: number;
  openDrawer: boolean;
}

const text = (t = "", opts: Partial<PrintOp> = {}): PrintOp => ({ kind: OP_TEXT, text: t, ...opts });
const centre = (t: string, opts: Partial<PrintOp> = {}) => text(t, { align: ALIGN_CENTRE, ...opts });

/** "Item name .......... £4.99" — the printer can't do two columns, so pad here. Truncates the
 *  NAME when the line is too long, never the money. Mirrors EscPos.TwoColumn on the agent. */
export function twoColumn(left: string, right: string, columns: number): string {
  let l = left ?? "";
  const r = right ?? "";
  let gap = columns - l.length - r.length;
  if (gap < 1) {
    const room = Math.max(0, columns - r.length - 1);
    l = l.length > room ? l.slice(0, room) : l;
    gap = Math.max(1, columns - l.length - r.length);
  }
  return l + " ".repeat(gap) + r;
}

/**
 * Build the print document for a completed sale.
 * @param openDrawer kick the drawer with the same job (a cash sale) — one round trip, and the
 *   drawer opens at the same moment the receipt starts printing, as on the native till.
 */
export function receiptToDocument(data: ReceiptData, columns = 42, openDrawer = false): PrintDocument {
  const tpl = getReceiptTemplateCached();
  const ops: PrintOp[] = [];

  // header — same order as the on-screen receipt (NatApp order: thank-you, shop, phone, address,
  // VAT number, date, operator)
  for (const l of tpl?.headerLines?.length ? tpl.headerLines : ["Thank you for shopping with us"]) ops.push(centre(l));
  ops.push(centre(tpl?.storeName || businessName(), { bold: true, large: true }));
  if (tpl?.phone) ops.push(centre(tpl.phone));
  for (const l of tpl?.addressLines ?? []) ops.push(centre(l));
  if (tpl?.showVatNumber && tpl?.vatNumber) ops.push(centre(`VAT No: ${tpl.vatNumber}`));
  ops.push(centre(new Date(data.date).toLocaleString("en-GB")));
  const operator = tpl?.showOperator ? getSession()?.name : null;
  if (operator) ops.push(centre(`Served by ${operator}`));
  ops.push({ kind: OP_RULE });

  // lines
  for (const l of data.lines) {
    ops.push(text((l.isReturn ? "RETURN — " : "") + l.item.name));
    ops.push(text(twoColumn(`  ${l.quantity} x ${gbp(l.pricePence)}`, gbp(lineTotalPence(l)), columns)));
    if (l.discount) {
      ops.push(text(twoColumn(`  ${l.discount.name}`, `-${gbp(lineDiscountPence(l))}`, columns)));
    }
    // FE7: say on the customer's copy that a gift card was sold and when its VAT falls due
    if (l.giftCardCode) {
      ops.push(text(l.exPricePence === l.pricePence
        ? "  gift card — no VAT (VAT applies when spent)"
        : "  gift card — VAT charged on this sale"));
    }
  }

  ops.push({ kind: OP_RULE });
  ops.push(text(twoColumn("Subtotal (ex tax)", gbp(data.totalExTaxPence), columns)));
  ops.push(text(twoColumn("Tax", gbp(data.totalPence - data.totalExTaxPence), columns)));
  ops.push(text(twoColumn("TOTAL", gbp(data.totalPence), columns), { bold: true }));
  ops.push({ kind: OP_RULE });

  for (const p of data.payments) ops.push(text(twoColumn(p.name, gbp(p.amountPence), columns)));
  const change = data.payments.reduce((c, p) => c + p.changePence, 0);
  if (change > 0) ops.push(text(twoColumn("Change", gbp(change), columns)));

  ops.push({ kind: OP_RULE });
  for (const l of tpl?.footerLines ?? []) ops.push(centre(l));
  if (data.queued) ops.push(centre("* taken offline — will sync automatically *"));

  // The sale-id barcode is what a return is looked up by, so it matters more than it looks.
  if (tpl?.showBarcode !== false && !data.saleId.startsWith("test-print")) {
    ops.push({ kind: OP_BARCODE, text: data.saleId.toUpperCase(), align: ALIGN_CENTRE, height: 80 });
  }
  ops.push(centre(data.saleId));
  ops.push({ kind: OP_CUT });

  return { ops, columns, openDrawer };
}
