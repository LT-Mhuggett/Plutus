import { useEffect, useMemo, useState } from "react";
import { checkout, fetchPayMethods, type PayMethod } from "../api.ts";
import { gbp, parsePence, toPounds } from "../money.ts";
import type { BasketLine } from "./basket.ts";
import type { ReceiptData } from "./Receipt.tsx";

interface Props {
  lines: BasketLine[];
  totals: { totalPence: number; totalExTaxPence: number };
  onClose: () => void;
  onComplete: (receipt: ReceiptData) => void;
}

export default function CheckoutDialog({ lines, totals, onClose, onComplete }: Props) {
  const [methods, setMethods] = useState<PayMethod[] | null>(null);
  const [amounts, setAmounts] = useState<Record<number, string>>({});
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    fetchPayMethods().then(setMethods).catch((e) => setError(String(e)));
  }, []);

  const parsed = useMemo(() => {
    const perMethod = new Map<number, number>();
    for (const [id, raw] of Object.entries(amounts)) {
      if (!raw.trim()) continue;
      const pence = parsePence(raw);
      if (pence === null) return { valid: false as const };
      if (pence > 0) perMethod.set(Number(id), pence);
    }
    const paid = [...perMethod.values()].reduce((a, b) => a + b, 0);
    return { valid: true as const, perMethod, paid };
  }, [amounts]);

  if (!methods) {
    return (
      <div className="overlay">
        <div className="dialog">{error ? <p className="error">{error}</p> : <p className="muted">Loading payment methods…</p>}</div>
      </div>
    );
  }

  const paid = parsed.valid ? parsed.paid : 0;
  const remaining = Math.max(0, totals.totalPence - paid);
  const overpay = Math.max(0, paid - totals.totalPence);
  // change can only be given from a changeable method (cash)
  const changeablePaid = parsed.valid
    ? [...parsed.perMethod.entries()].filter(([id]) => methods.find((m) => m.id === id)?.isChangeable).reduce((a, [, v]) => a + v, 0)
    : 0;
  const changeOk = overpay === 0 || overpay <= changeablePaid;
  const canComplete = parsed.valid && remaining === 0 && changeOk && !busy;

  function quickFill(id: number) {
    setAmounts((a) => ({ ...a, [id]: (remaining / 100).toFixed(2) }));
  }

  async function complete() {
    if (!canComplete || !parsed.valid) return;
    setBusy(true);
    setError("");
    try {
      // change is attributed to the changeable method(s), proportionally to their share
      const payments = [...parsed.perMethod.entries()].map(([payId, pence]) => {
        const m = methods!.find((x) => x.id === payId)!;
        const change = m.isChangeable && changeablePaid > 0 ? Math.round((overpay * pence) / changeablePaid) : 0;
        return { payId, amount: toPounds(pence), change: toPounds(change), name: m.name, pence };
      });
      const sale = await checkout(
        lines.map((l) => ({
          itemId: l.item.idOne,
          quantity: l.quantity,
          unitPrice: toPounds(l.pricePence),
          unitExPrice: toPounds(l.exPricePence),
          adjusted: l.adjusted ? { price: toPounds(l.pricePence), exPrice: toPounds(l.exPricePence) } : undefined,
          discount: l.discount
            ? {
                discountId: l.discount.discountId,
                // historic data stores the fraction for % discounts and the £ amount for fixed ones
                discountRate: l.discount.amount,
              }
            : undefined,
          isReturn: l.isReturn,
          originSaleId: l.originSaleId,
        })),
        payments.map(({ payId, amount, change }) => ({ payId, amount, change })),
        { total: toPounds(totals.totalPence), totalExTax: toPounds(totals.totalExTaxPence) },
      );
      onComplete({
        saleId: sale.saleId,
        date: new Date().toISOString(),
        lines,
        totalPence: totals.totalPence,
        totalExTaxPence: totals.totalExTaxPence,
        payments: payments.map((p) => ({ name: p.name, amountPence: p.pence, changePence: Math.round(p.change * 100) })),
        queued: sale.queued,
      });
    } catch (e) {
      setError(String(e));
      setBusy(false);
    }
  }

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && !busy && onClose()}>
      <div className="dialog">
        <h2>Checkout — {gbp(totals.totalPence)}</h2>

        <div className="pay-methods">
          {methods.map((m) => (
            <label key={m.id} className="pay-row">
              <span className="grow">
                {m.name}
                {m.isChangeable && <span className="muted small"> (gives change)</span>}
              </span>
              <button className="ghost small" tabIndex={-1} onClick={(e) => { e.preventDefault(); quickFill(m.id); }}>
                rest
              </button>
              <input
                inputMode="decimal"
                placeholder="0.00"
                value={amounts[m.id] ?? ""}
                onChange={(e) => setAmounts((a) => ({ ...a, [m.id]: e.target.value }))}
              />
            </label>
          ))}
        </div>

        <div className="totals">
          <div className="row muted">
            <span>Paid</span>
            <span>{parsed.valid ? gbp(paid) : "—"}</span>
          </div>
          {remaining > 0 && (
            <div className="row">
              <span>Remaining</span>
              <span>{gbp(remaining)}</span>
            </div>
          )}
          {overpay > 0 && (
            <div className={changeOk ? "row grand" : "row error"}>
              <span>{changeOk ? "Change" : "Change (not available on chosen methods)"}</span>
              <span>{gbp(overpay)}</span>
            </div>
          )}
        </div>

        {error && <p className="error small">{error}</p>}

        <div className="dialog-actions">
          <button className="ghost" disabled={busy} onClick={onClose}>
            Cancel
          </button>
          <button className="primary" disabled={!canComplete} onClick={complete}>
            {busy ? "Completing…" : "Complete sale"}
          </button>
        </div>
      </div>
    </div>
  );
}
