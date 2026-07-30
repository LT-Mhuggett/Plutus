import { useEffect, useState } from "react";
import { ApiError, gbp } from "./api.ts";
import { accessToken } from "./auth.ts";

// The full customer editor (details / store credit / membership), extracted from CustomersPage
// (WP5.1) so BOTH the Customers tab and the Loyalty tab open the same dialog — loyalty is now
// editable where you look at it, not only on the Customers page. Writes are gated server-side on
// customers.manage; a 403 is turned into a friendly message.

interface CustomerDetail {
  id: string; name: string; email: string | null; phone: string | null;
  creditAccountId: string | null; creditBalancePence: number;
  membership: { tier: string; autoDiscountRate: number; renewalDay: string; expired: boolean } | null;
}
interface CreditView {
  balancePence: number;
  entries: { type: string; amountPence: number; reason: string | null; saleId: string | null; createdAtUtc: string }[];
}

async function j<T>(method: string, url: string, body?: unknown): Promise<T> {
  const token = accessToken();
  const res = await fetch(url, {
    method,
    headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(body !== undefined ? { "Content-Type": "application/json" } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!res.ok) {
    if (res.status === 403)
      throw new ApiError(403, "You need the ‘customers.manage’ permission to do this — ask an administrator.");
    let detail = `${res.status}`;
    try { detail = (await res.json())?.detail ?? detail; } catch { /* keep */ }
    throw new ApiError(res.status, detail);
  }
  return res.status === 204 ? (undefined as T) : res.json();
}

const toPence = (s: string) => Math.round(parseFloat(s) * 100);

export default function CustomerDialog({ id, onClose }: { id: string; onClose: () => void }) {
  const [detail, setDetail] = useState<CustomerDetail | null>(null);
  const [credit, setCredit] = useState<CreditView | null>(null);
  const [error, setError] = useState("");
  const [issue, setIssue] = useState("");
  const [issueReason, setIssueReason] = useState("");
  const [tier, setTier] = useState("Club");
  const [rate, setRate] = useState("10");
  const [edit, setEdit] = useState<{ name: string; email: string; phone: string } | null>(null);

  const refresh = () =>
    Promise.all([j<CustomerDetail>("GET", `/api/v1/customers/${id}`), j<CreditView>("GET", `/api/v1/customers/${id}/credit`)])
      .then(([d, c]) => { setDetail(d); setCredit(c); setError(""); })
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));
  useEffect(() => { void refresh(); }, [id]); // eslint-disable-line react-hooks/exhaustive-deps

  const run = (p: Promise<unknown>) => void p.then(refresh).catch((e) => setError(String(e instanceof Error ? e.message : e)));

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog">
        {error && <p className="error small">{error}</p>}
        {detail && (
          <>
            <div className="toolbar" style={{ justifyContent: "space-between" }}>
              <h3>{detail.name}</h3>
              {!edit && <button className="ghost small"
                onClick={() => setEdit({ name: detail.name, email: detail.email ?? "", phone: detail.phone ?? "" })}>
                Edit details
              </button>}
            </div>

            {edit ? (
              <form className="stack" onSubmit={(e) => {
                e.preventDefault();
                run(j("PUT", `/api/v1/customers/${id}`,
                  { name: edit.name, email: edit.email || undefined, phone: edit.phone || undefined })
                  .then(() => setEdit(null)));
              }}>
                <label>Name <input required value={edit.name} onChange={(e) => setEdit({ ...edit, name: e.target.value })} /></label>
                <label>Email <input type="email" value={edit.email} onChange={(e) => setEdit({ ...edit, email: e.target.value })} /></label>
                <label>Phone <input value={edit.phone} onChange={(e) => setEdit({ ...edit, phone: e.target.value })} /></label>
                <div className="dialog-actions">
                  <button type="button" className="ghost" onClick={() => setEdit(null)}>Cancel</button>
                  <button className="primary" disabled={!edit.name.trim()}>Save details</button>
                </div>
              </form>
            ) : (
              <dl className="kv">
                <dt>Email</dt><dd>{detail.email ?? "—"}</dd>
                <dt>Phone</dt><dd>{detail.phone ?? "—"}</dd>
                <dt>Store credit</dt><dd><strong>{gbp(detail.creditBalancePence)}</strong></dd>
                <dt>Membership</dt>
                <dd>{detail.membership
                  ? `${detail.membership.tier} · ${(detail.membership.autoDiscountRate * 100).toFixed(0)}% · renews ${detail.membership.renewalDay}${detail.membership.expired ? " (EXPIRED)" : ""}`
                  : "—"}</dd>
              </dl>
            )}

            <h4>Grant credit</h4>
            <div className="toolbar">
              <label>Amount £ <input className="short" inputMode="decimal" value={issue} onChange={(e) => setIssue(e.target.value)} /></label>
              <label>Reason <input value={issueReason} onChange={(e) => setIssueReason(e.target.value)} /></label>
              <button className="primary small" disabled={!issue}
                onClick={() => run(j("POST", `/api/v1/customers/${id}/credit/issue`, { amountPence: toPence(issue), reason: issueReason || "goodwill grant" }))}>
                Issue
              </button>
            </div>

            <h4>Membership</h4>
            <div className="toolbar">
              <label>Tier <input className="short" value={tier} onChange={(e) => setTier(e.target.value)} /></label>
              <label>Discount % <input className="short" inputMode="numeric" value={rate} onChange={(e) => setRate(e.target.value)} /></label>
              <button className="ghost small" disabled={!tier}
                onClick={() => run(j("POST", `/api/v1/customers/${id}/membership`, { tier, autoDiscountRate: (parseFloat(rate) || 0) / 100 }))}>
                Set membership
              </button>
            </div>

            <h4>Credit history</h4>
            <table>
              <thead><tr><th>When</th><th>Type</th><th className="num">Δ</th><th>Reason</th></tr></thead>
              <tbody>
                {credit?.entries.map((e, i) => (
                  <tr key={i}>
                    <td>{new Date(e.createdAtUtc + "Z").toLocaleString("en-GB")}</td>
                    <td>{e.type}</td>
                    <td className="num">{e.amountPence >= 0 ? "+" : ""}{gbp(e.amountPence)}</td>
                    <td className="small">{e.reason}</td>
                  </tr>
                ))}
                {(!credit || credit.entries.length === 0) && <tr><td colSpan={4} className="muted">No credit activity.</td></tr>}
              </tbody>
            </table>
          </>
        )}
        <div className="dialog-actions"><button className="ghost" onClick={onClose}>Close</button></div>
      </div>
    </div>
  );
}
