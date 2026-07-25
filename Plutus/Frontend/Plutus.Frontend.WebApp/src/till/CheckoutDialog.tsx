import { useEffect, useMemo, useState } from "react";
import { checkout, fetchPayMethods, type CustomerDetail, type PayMethod } from "../api.ts";
import { gbp, parsePence } from "../money.ts";
import type { BasketLine } from "./basket.ts";
import type { ReceiptData } from "./Receipt.tsx";

// Synthetic pay-method id for the store-credit tender (Phase 8 retrofit). Real PayMethod ids
// are positive; −1 never collides. Maps to a Credit tender server-side (name contains "credit").
const CREDIT_PAYID = -1;

interface Props {
  lines: BasketLine[];
  totals: { totalPence: number; totalExTaxPence: number };
  customer?: CustomerDetail | null;
  onClose: () => void;
  onComplete: (receipt: ReceiptData) => void;
}

export default function CheckoutDialog({ lines, totals, customer, onClose, onComplete }: Props) {
  const [methods, setMethods] = useState<PayMethod[] | null>(null);
  const [amounts, setAmounts] = useState<Record<number, string>>({});
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    fetchPayMethods()
      .then((real) => {
        // Store credit is offered only with a customer attached, a positive balance, AND
        // online (the redeem needs a live balance check — it can't queue offline).
        const eligible = customer && customer.creditBalancePence > 0 && navigator.onLine;
        setMethods(
          eligible
            ? [...real, { id: CREDIT_PAYID, name: "Store credit", charge: 0, minimumCharge: 0, isChangeable: false, isCashBackable: false }]
            : real,
        );
      })
      .catch((e) => setError(String(e)));
  }, [customer]);

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
  const creditRedeem = parsed.valid ? parsed.perMethod.get(CREDIT_PAYID) ?? 0 : 0;
  const creditOverBalance = creditRedeem > (customer?.creditBalancePence ?? 0);
  const canComplete = parsed.valid && remaining === 0 && changeOk && !creditOverBalance && !busy;

  function quickFill(id: number) {
    setAmounts((a) => ({ ...a, [id]: (remaining / 100).toFixed(2) }));
  }

  async function complete() {
    if (!canComplete || !parsed.valid) return;
    setBusy(true);
    setError("");
    try {
      // Change is attributed to the changeable method(s) proportionally, with the LAST
      // changeable payment absorbing the rounding remainder — Σchange must equal the
      // overpay to the penny (the v1 pipeline enforces net tender == gross).
      const entries = [...parsed.perMethod.entries()];
      const changeable = entries.filter(([id]) => methods!.find((m) => m.id === id)?.isChangeable);
      const changeByPayId = new Map<number, number>();
      let allocated = 0;
      changeable.forEach(([payId, pence], i) => {
        const change =
          i === changeable.length - 1 ? overpay - allocated : Math.round((overpay * pence) / changeablePaid);
        allocated += change;
        changeByPayId.set(payId, change);
      });
      const payments = entries.map(([payId, pence]) => ({
        payId,
        name: methods!.find((x) => x.id === payId)!.name,
        amountPence: pence,
        changePence: changeByPayId.get(payId) ?? 0,
      }));

      const sale = await checkout(
        lines,
        payments,
        totals,
        creditRedeem > 0 && customer ? { customerId: customer.id, creditRedeemPence: creditRedeem } : undefined,
      );
      onComplete({
        saleId: sale.saleId,
        date: new Date().toISOString(),
        lines,
        totalPence: totals.totalPence,
        totalExTaxPence: totals.totalExTaxPence,
        payments: payments.map((p) => ({ name: p.name, amountPence: p.amountPence, changePence: p.changePence })),
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

        {creditOverBalance && (
          <p className="error small">Store credit exceeds the customer's balance ({gbp(customer?.creditBalancePence ?? 0)}).</p>
        )}
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
