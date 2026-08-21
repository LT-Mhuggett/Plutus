import { useEffect, useState } from "react";
import DialogX from "./DialogX.tsx";
import DataTable from "./DataTable.tsx";
import Barcode39 from "./till/Barcode39.tsx";
import {
  fetchCustomerHistory, issueCredit,
  type CustomerHistoryRow, type V1LoyaltyRow,
} from "./api.ts";
import { gbp } from "./money.ts";
import { canManageCustomers } from "./pipeline.ts";

/**
 * One customer, everything about them — WP-L1b, the web till's half.
 *
 * ⚠⚠ Matt, 2026-08-18, with a screenshot of the portal: *"I need to be able to see all the
 * information you see in the portal on both MAUI and the webtill … Need to be able to print the card
 * from the till … I also need the 'Credit History' to be ALL history."*
 *
 * ⚠ MAUI got this first (till 1.92.0) and this is the matching screen, so the two tills show the same
 * facts in the same order. The history table is the standard `DataTable`, which is what makes it
 * scroll, sort and search without any of that being written twice.
 *
 * ⚠ THE CARD IS CR80 (85.6 × 54 mm) and prints from an ordinary printer, exactly as the portal's
 * does — a browser can do that. ⚠⚠ MAUI cannot: its printer is the thermal receipt printer on the
 * counter, so it prints a scannable **slip** instead. Same Code 39, same `C`-prefixed payload, so
 * either scans as a member. **Parity is in what the customer can do with it**, not in the object.
 */
export default function CustomerDetail(
  { row, onClose, onChanged }: { row: V1LoyaltyRow; onClose: () => void; onChanged: () => void },
) {
  const [history, setHistory] = useState<CustomerHistoryRow[] | null>(null);
  const [total, setTotal] = useState(0);
  const [failed, setFailed] = useState(false);
  const [printing, setPrinting] = useState(false);
  const canManage = canManageCustomers();

  useEffect(() => {
    fetchCustomerHistory(row.id)
      .then((r) => { setHistory(r.rows); setTotal(r.total); })
      // ⚠ NULL IS NOT EMPTY. A history that could not be read must say so — "nothing has happened"
      // for a customer who has traded for years is a confident wrong statement, and an operator
      // would then grant credit believing none had ever been given.
      .catch(() => setFailed(true));
  }, [row.id]);

  const membership = row.tier
    ? `${row.tier}${row.autoDiscountRate ? ` · ${Math.round(row.autoDiscountRate * 100)}%` : ""}`
      + (row.renewalDay ? (row.expired ? ` · EXPIRED ${row.renewalDay}` : ` · renews ${row.renewalDay}`) : "")
    : "—";

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && !printing && onClose()}>
      <div className="dialog wide">
        <h2 className="no-print">{row.name}</h2>
        <DialogX onClose={onClose} />

        {/* ⚠ THE ONLY ELEMENT THAT REACHES THE PRINTER — `.member-card` carries the print rules, the
            same class and the same CR80 geometry the portal uses. Everything else is `no-print`. */}
        {printing && row.memberNo && (
          <div className="member-card" id="member-card">
            <div className="mc-head"><span className="mc-company">Membership</span>
              {row.tier && <span className="mc-tier">{row.tier}</span>}</div>
            <div className="mc-name">{row.name}</div>
            <div className="mc-barcode">
              {/* ⚠ The "C" prefix is what makes a scan resolve to a MEMBER rather than a product —
                  `LooksLikeMemberScan` requires it AND a valid check character. */}
              <Barcode39 value={`C${row.memberNo}`} height={38} fit showText={false} />
            </div>
            <div className="mc-foot">
              <span className="mc-no">{row.memberNo}</span>
              {row.renewalDay && <span className="mc-renew">valid to {row.renewalDay}</span>}
            </div>
          </div>
        )}

        <div className="no-print">
          <dl className="facts">
            <dt>Member no.</dt><dd className="mono">{row.memberNo ?? "—"}</dd>
            <dt>Email</dt><dd>{row.email ?? "—"}</dd>
            <dt>Phone</dt><dd>{row.phone ?? "—"}</dd>
            <dt>Store credit</dt><dd><strong>{row.creditBalancePence ? gbp(row.creditBalancePence) : "—"}</strong></dd>
            <dt>Membership</dt><dd>{membership}</dd>
            <dt>Created</dt><dd>{row.createdAtUtc ?? "—"}</dd>
          </dl>

          <div className="toolbar">
            {/* ⚠ PRINT CARD IS NOT GATED ON `customers.manage` — handing somebody their own card is
                counter work, and a cashier is who is standing there. Hidden with no membership
                number, because there would be nothing to put in the barcode.

                ⚠⚠ `window.print()` IS CORRECT HERE — DO NOT "FIX" IT TO THE RECEIPT PRINTER. This
                prints a **CR80 card**, 85.6 × 54 mm, to an ordinary page printer, deliberately
                (§5d WP-L1; MAUI prints a thermal slip instead because no page printer sits behind
                it). `till-design.md` **D6b** makes the agent the default for a *receipt* and names
                this as the documented exception — sending card stock to a receipt roll would be the
                same class of mistake in the opposite direction. */}
            {row.memberNo && (
              <button className="ghost" onClick={() => { setPrinting(true); setTimeout(() => { window.print(); setPrinting(false); }, 50); }}>
                Print card
              </button>
            )}
            {canManage && <GrantCredit id={row.id} name={row.name} onDone={onChanged} />}
          </div>

          <h4>History</h4>
          {failed ? (
            <p className="muted small">
              This customer's history couldn't be read. You may not have permission to see it, or the
              connection is down.
            </p>
          ) : history === null ? (
            <p className="muted small">Loading…</p>
          ) : (
            <>
              {/* ⚠ SAY THE CAP when it bites — `total` is what matched, `rows` is what fitted. */}
              {total > history.length && (
                <p className="muted small">
                  Showing the most recent {history.length.toLocaleString()} of {total.toLocaleString()} entries.
                </p>
              )}
              <DataTable<CustomerHistoryRow>
                columns={[
                  { key: "atUtc", label: "When", render: (r) => new Date(r.atUtc + "Z").toLocaleString("en-GB") },
                  { key: "type", label: "What", render: (r) => r.type },
                  { key: "detail", label: "Detail", render: (r) => <span className="small">{r.detail}</span> },
                  {
                    key: "amountPence", label: "Amount", numeric: true,
                    // ⚠ "—" NOT £0.00 for a rename: a change of name is not a zero-pound transaction,
                    // and showing one puts money on the record that never moved.
                    render: (r) => (r.amountPence === null ? "—" : gbp(r.amountPence)),
                  },
                ]}
                rows={history}
                getKey={(r) => `${r.atUtc}-${r.type}-${r.detail}`}
                initialSortKey="atUtc" initialSortDir="desc"
                search={(r) => `${r.type} ${r.detail}`}
                searchPlaceholder="Search this customer's history…"
                emptyText="Nothing has happened to this customer yet."
              />
            </>
          )}
        </div>

        <div className="dialog-actions no-print">
          <button className="ghost" onClick={onClose}>Close</button>
        </div>
      </div>
    </div>
  );
}

/**
 * Grant credit — **Supervisor and above**, and that was already the rule rather than one imposed
 * here: the endpoint is gated `customers.manage`, which `RbacSeeder` gives a Supervisor and does not
 * give a Cashier.
 *
 * ⚠⚠ THE REASON IS MANDATORY AND IS NEVER SUBSTITUTED. The button is disabled without one and the
 * server refuses a blank. Credit added with a reason nobody typed shows a plausible word in the
 * history that means nothing — worse than a blank, because it reads as an audit trail.
 */
function GrantCredit({ id, name, onDone }: { id: string; name: string; onDone: () => void }) {
  const [open, setOpen] = useState(false);
  const [amount, setAmount] = useState("");
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  if (!open) return <button className="ghost" onClick={() => setOpen(true)}>Grant credit</button>;

  const pence = Math.round(parseFloat(amount || "0") * 100);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true); setError("");
    try {
      await issueCredit(id, pence, reason);
      setOpen(false); setAmount(""); setReason("");
      onDone();
    } catch (err) {
      setError(String(err instanceof Error ? err.message : err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="toolbar" onSubmit={submit}>
      <label>Amount £ <input className="short" inputMode="decimal" value={amount}
        onChange={(e) => setAmount(e.target.value)} disabled={busy} /></label>
      <label>Reason * <input value={reason} onChange={(e) => setReason(e.target.value)} disabled={busy} /></label>
      {/* ⚠ Not more than nothing is not a grant — taking credit back is a refund. */}
      <button type="submit" className="primary small" disabled={busy || pence <= 0 || !reason.trim()}>
        {busy ? "Adding…" : `Grant to ${name.split(" ")[0]}`}
      </button>
      <button type="button" className="ghost small" onClick={() => setOpen(false)} disabled={busy}>Cancel</button>
      {error && <span className="error small">{error}</span>}
    </form>
  );
}
