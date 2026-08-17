import { useEffect, useState } from "react";
import { type CashEventResult, type CashEventType } from "./pipeline.ts";
import { gbp, parsePence } from "./money.ts";
import { localDayIsClosed, postOrQueue, queuedCashCount } from "./cashOutbox.ts";
import { can, POS_CASH_REOPEN } from "./permissions.ts";
import { cachedRoster } from "./roster.ts";
import { getSession } from "./session.ts";

// WP7.2 till Cash drawer: open float, paid-in/out (reasoned), X snapshot, Z close.
// X/Z show the server's expected drawer and the variance vs the counted amount.

export default function CashPage() {
  const [amount, setAmount] = useState("");
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [last, setLast] = useState<CashEventResult | null>(null);

  // ── W-P5 ──────────────────────────────────────────────────────────────────
  /** How many cash events are still waiting to reach the platform. */
  const [waiting, setWaiting] = useState(0);
  /** Whether TODAY is Z-closed according to this till's own record. */
  const [closed, setClosed] = useState(false);
  /** ⚠ Whether this operator may reopen a day — Supervisor and up hold `pos.cash.reopen`. */
  const [mayReopen, setMayReopen] = useState(false);
  /** Set when the last event was QUEUED rather than sent, so the operator is told it is safe. */
  const [queuedNotice, setQueuedNotice] = useState("");

  async function refreshState() {
    setWaiting(await queuedCashCount());
    setClosed(await localDayIsClosed());
  }

  useEffect(() => {
    void refreshState();
    void (async () => {
      // ⚠ From the W-P2 CACHED roster, so the gate works with the line down — the same reasoning as
      // the discount ceiling. A supervisor must be able to reopen a day during an outage.
      const roster = await cachedRoster();
      const me = getSession()?.employeeId ?? "";
      const grants =
        roster?.operators.find((o) => (o.userId ?? "").toLowerCase() === me.toLowerCase())?.grants ?? [];
      setMayReopen(can(grants, POS_CASH_REOPEN, new Date(), null));
    })();
  }, []);

  async function submit(type: CashEventType, needsAmount: boolean, counts: boolean) {
    setError("");
    setQueuedNotice("");
    const pence = amount.trim() ? parsePence(amount) : 0;
    if ((needsAmount || counts) && (pence === null || pence < 0)) {
      setError("Enter a valid amount.");
      return;
    }
    if ((type === "PaidIn" || type === "PaidOut") && !reason.trim()) {
      setError("A reason is required for paid-in / paid-out.");
      return;
    }
    // ⚠⚠ A REOPEN NEEDS A REASON TOO, and refusing an empty one is the point: "no reason given" in an
    // audit trail is worse than no trail at all, because it looks like a record.
    if (type === "ZReopen" && !reason.trim()) {
      setError("Reopening a closed day needs a reason.");
      return;
    }
    setBusy(true);
    try {
      const outcome = await postOrQueue({
        type,
        amountPence: counts ? 0 : pence ?? 0,
        countedPence: counts ? pence ?? 0 : undefined,
        reason: reason.trim() || undefined,
      });

      if (outcome.kind === "refused") {
        setError(outcome.detail ?? "The platform refused it.");
      } else {
        if (outcome.kind === "queued") {
          // ⚠ NOT an error. The money IS recorded — it is waiting to be sent, and telling somebody it
          // failed would have them record it again.
          setQueuedNotice("Recorded on this till and waiting to send — it will reach Plutus when the connection is back.");
        }
        // ⚠ `expectedPence` only exists when the PLATFORM answered. Offline there is no expected
        // drawer, because only the platform can see the sales half — the till must never compute it.
        setLast(
          outcome.kind === "sent"
            ? { type, amountPence: counts ? 0 : pence ?? 0, countedPence: counts ? pence ?? 0 : null,
                expectedPence: outcome.expectedPence ?? null, variancePence: outcome.variancePence ?? null }
            : { type, amountPence: counts ? 0 : pence ?? 0, countedPence: counts ? pence ?? 0 : null },
        );
        setAmount("");
        setReason("");
      }
    } catch (e) {
      setError(String(e instanceof Error ? e.message : e));
    } finally {
      setBusy(false);
      await refreshState();
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
        <button className="primary" disabled={busy || closed} onClick={() => submit("ZClose", false, true)}>Z close (count)</button>
      </div>

      {/* ── W-P5: reopen a day closed by mistake ──────────────────────────────────────────────
          ⚠ Shown only when the day IS closed and this operator holds `pos.cash.reopen` — Supervisor
          and up, never Cashier: the person who counted the drawer must not be the only one who can
          quietly un-count it.
          ⚠⚠ A COMPENSATING EVENT, NOT A DELETION. The ZClose stays, and so does the variance computed
          against it — "closed 17:32, reopened 17:41, by X, because Y". Deleting it would erase that
          somebody counted and banked the drawer.
          ⚠ ONLINE ONLY, deliberately: this is an audited supervisor action, not drawer money, so it is
          not queued. If it cannot reach the platform it has not happened. */}
      {closed && (
        <div className="setting-row" style={{ flexDirection: "column", alignItems: "stretch", gap: 6 }}>
          <span className="muted small">
            This day is <strong>closed</strong>. Nothing else can be recorded against it until it is reopened.
          </span>
          {mayReopen ? (
            <button className="ghost" disabled={busy} onClick={() => submit("ZReopen", false, false)}>
              Reopen this day… (needs a reason)
            </button>
          ) : (
            <span className="muted small">A supervisor can reopen it.</span>
          )}
        </div>
      )}

      {waiting > 0 && (
        <p className="muted small">
          ⚠ {waiting} cash event{waiting === 1 ? "" : "s"} waiting to send. They are recorded on this till
          and will reach Plutus when the connection is back.
        </p>
      )}

      {error && <p className="error">{error}</p>}
      {queuedNotice && <p className="muted small">{queuedNotice}</p>}

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
