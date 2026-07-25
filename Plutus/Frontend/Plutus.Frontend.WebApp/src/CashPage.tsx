import { useState } from "react";
import { postCashEvent, type CashEventResult, type CashEventType } from "./pipeline.ts";
import { gbp, parsePence } from "./money.ts";

// WP7.2 till Cash drawer: open float, paid-in/out (reasoned), X snapshot, Z close.
// X/Z show the server's expected drawer and the variance vs the counted amount.

export default function CashPage() {
  const [amount, setAmount] = useState("");
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [last, setLast] = useState<CashEventResult | null>(null);

  async function submit(type: CashEventType, needsAmount: boolean, counts: boolean) {
    setError("");
    const pence = amount.trim() ? parsePence(amount) : 0;
    if ((needsAmount || counts) && (pence === null || pence < 0)) {
      setError("Enter a valid amount.");
      return;
    }
    if ((type === "PaidIn" || type === "PaidOut") && !reason.trim()) {
      setError("A reason is required for paid-in / paid-out.");
      return;
    }
    setBusy(true);
    try {
      const result = await postCashEvent({
        type,
        amountPence: counts ? 0 : pence ?? 0,
        countedPence: counts ? pence ?? 0 : undefined,
        reason: reason.trim() || undefined,
      });
      setLast(result);
      setAmount("");
      setReason("");
    } catch (e) {
      setError(String(e instanceof Error ? e.message : e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="panel">
      <h2>Cash drawer</h2>
      <p className="muted small">
        Records against today's session for this till. Money is counted in pounds (e.g. 50 or 49.95). One Z-close per day.
      </p>

      <div className="cash-form">
        <label className="setting-row">
          <span className="grow">Amount / count</span>
          <input inputMode="decimal" placeholder="0.00" value={amount} onChange={(e) => setAmount(e.target.value)} />
        </label>
        <label className="setting-row">
          <span className="grow">Reason (for paid-in / paid-out)</span>
          <input placeholder="e.g. window cleaner" value={reason} onChange={(e) => setReason(e.target.value)} />
        </label>
      </div>

      <div className="cash-actions">
        <button className="ghost" disabled={busy} onClick={() => submit("OpenFloat", true, false)}>Open float</button>
        <button className="ghost" disabled={busy} onClick={() => submit("PaidIn", true, false)}>Paid in</button>
        <button className="ghost" disabled={busy} onClick={() => submit("PaidOut", true, false)}>Paid out</button>
        <button className="ghost" disabled={busy} onClick={() => submit("XSnapshot", false, true)}>X report (count)</button>
        <button className="primary" disabled={busy} onClick={() => submit("ZClose", false, true)}>Z close (count)</button>
      </div>

      {error && <p className="error">{error}</p>}

      {last && (
        <div className="cash-result">
          <h3>{last.type}</h3>
          {last.type === "XSnapshot" || last.type === "ZClose" ? (
            <dl className="env-info">
              <dt>Counted</dt><dd>{gbp(last.countedPence ?? 0)}</dd>
              <dt>Expected</dt><dd>{gbp(last.expectedPence ?? 0)}</dd>
              <dt>Variance</dt>
              <dd className={(last.variancePence ?? 0) === 0 ? "" : "error"}>
                {(last.variancePence ?? 0) >= 0 ? "+" : ""}{gbp(last.variancePence ?? 0)}
                {last.type === "ZClose" ? " · drawer closed for today" : ""}
              </dd>
            </dl>
          ) : (
            <p>Recorded {gbp(last.amountPence)}.</p>
          )}
        </div>
      )}
    </section>
  );
}
