import { useEffect, useState } from "react";
import { ApiError, fetchLoyaltyTiers, gbp, type LoyaltyTier } from "./api.ts";
import { accessToken } from "./auth.ts";
import Barcode39 from "./Barcode39.tsx";
import MemberCard from "./MemberCard.tsx";
import DialogX from "./DialogX.tsx";
import { apiDateTime, apiDay } from "./apiTime.ts";

// The full customer editor (details / store credit / membership), extracted from CustomersPage
// (WP5.1) so BOTH the Customers tab and the Loyalty tab open the same dialog — loyalty is now
// editable where you look at it, not only on the Customers page. Writes are gated server-side on
// customers.manage; a 403 is turned into a friendly message.
// FE1: membership is ASSIGNED from the tier catalogue (Loyalty → Manage tiers) — the tier owns the
// discount and renewal length, so there is no free-typed tier name or rate here any more.

interface CustomerDetail {
  id: string; name: string; email: string | null; phone: string | null;
  memberNo: string | null; memberBarcode: string | null;   // FE2
  creditAccountId: string | null; creditBalancePence: number;
  membership: { tierId: string | null; tier: string; autoDiscountRate: number; renewalDay: string; expired: boolean } | null;
  externalRefs: { provider: string; externalId: string; email: string | null; lastSeenAtUtc: string }[];
}

const providerLabel = (p: string) => (p === "woo" ? "WooCommerce" : p);
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
  const [tiers, setTiers] = useState<LoyaltyTier[]>([]);
  const [tierId, setTierId] = useState("");
  const [edit, setEdit] = useState<{ name: string; email: string; phone: string } | null>(null);
  const [card, setCard] = useState(false); // FE2 printable membership card

  const refresh = () =>
    Promise.all([j<CustomerDetail>("GET", `/api/v1/customers/${id}`), j<CreditView>("GET", `/api/v1/customers/${id}/credit`)])
      .then(([d, c]) => { setDetail(d); setCredit(c); setError(""); setTierId(d.membership?.tierId ?? ""); })
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));
  useEffect(() => { void refresh(); }, [id]); // eslint-disable-line react-hooks/exhaustive-deps
  // active tiers only — a retired tier can't be assigned (the server rejects it too)
  useEffect(() => { void fetchLoyaltyTiers().then(setTiers).catch(() => undefined); }, []);

  const run = (p: Promise<unknown>) => void p.then(refresh).catch((e) => setError(String(e instanceof Error ? e.message : e)));

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog">
        <DialogX onClose={onClose} />
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
                <dt>Member no.</dt>
                <dd>
                  {detail.memberNo ? (
                    <>
                      <span className="mono">{detail.memberNo}</span>{" "}
                      <button className="ghost small" onClick={() => setCard(true)}>Print card</button>
                      {detail.memberBarcode && (
                        <div><Barcode39 value={detail.memberBarcode} height={30} showText={false} /></div>
                      )}
                    </>
                  ) : <span className="muted">—</span>}
                </dd>
                <dt>Email</dt><dd>{detail.email ?? "—"}</dd>
                <dt>Phone</dt><dd>{detail.phone ?? "—"}</dd>
                <dt>Store credit</dt><dd><strong>{gbp(detail.creditBalancePence)}</strong></dd>
                <dt>Membership</dt>
                <dd>{detail.membership
                  ? `${detail.membership.tier} · ${(detail.membership.autoDiscountRate * 100).toFixed(0)}% · renews ${detail.membership.renewalDay}${detail.membership.expired ? " (EXPIRED)" : ""}`
                  : "—"}</dd>
              </dl>
            )}

            {detail.externalRefs.length > 0 && (
              <>
                <h4>Linked accounts</h4>
                <ul className="small">
                  {detail.externalRefs.map((r) => (
                    <li key={`${r.provider}-${r.externalId}`}>
                      {providerLabel(r.provider)} <span className="mono">{r.externalId}</span>
                      {r.email ? ` · ${r.email}` : ""} <span className="muted">· last seen {apiDay(r.lastSeenAtUtc)}</span>
                    </li>
                  ))}
                </ul>
              </>
            )}

            {/*
              ⚠⚠ A REASON IS MANDATORY — Matt, 2026-08-18: "Adding credit needs to have a reason and be
              viewable in the customers history."

              This used to send `issueReason || "goodwill grant"`, and the endpoint itself defaulted a
              missing reason to "grant". So credit could be put on somebody's account with NO reason
              anybody typed, and the Credit history below would show a plausible-looking word that means
              nothing — worse than a blank, because it reads as an audit trail.

              ⚠ The button is gated on BOTH fields and the field is marked, rather than the reason being
              silently supplied. The endpoint refuses a blank one too (that is the real guard, since the
              portal is not the only possible caller); this is so the operator finds out before the
              round trip instead of after it.
            */}
            <h4>Grant credit</h4>
            <div className="toolbar">
              <label>Amount £ <input className="short" inputMode="decimal" value={issue} onChange={(e) => setIssue(e.target.value)} /></label>
              <label>Reason * <input value={issueReason} onChange={(e) => setIssueReason(e.target.value)} /></label>
              <button className="primary small" disabled={!issue || !issueReason.trim()}
                onClick={() => run(j("POST", `/api/v1/customers/${id}/credit/issue`, { amountPence: toPence(issue), reason: issueReason.trim() }))}>
                Issue
              </button>
            </div>
            <p className="muted small">
              A reason is required, and it is kept on the credit history below — it is what a manager
              reads months later to know why the shop owes this money.
            </p>

            <h4>Membership</h4>
            <div className="toolbar">
              <label>Tier
                <select className="short" value={tierId} onChange={(e) => setTierId(e.target.value)}>
                  <option value="">— pick a tier —</option>
                  {tiers.map((t) => (
                    <option key={t.id} value={t.id}>{t.name} · {Math.round(t.autoDiscountRate * 1000) / 10}%</option>
                  ))}
                </select>
              </label>
              <button className="ghost small" disabled={!tierId}
                onClick={() => run(j("POST", `/api/v1/customers/${id}/membership`, { tierId }))}>
                Set membership
              </button>
              {tiers.length === 0 && (
                <span className="muted small">No tiers defined yet — set them up in Loyalty → Manage tiers.</span>
              )}
            </div>

            <h4>Credit history</h4>
            <table>
              <thead><tr><th>When</th><th>Type</th><th className="num">Δ</th><th>Reason</th></tr></thead>
              <tbody>
                {credit?.entries.map((e, i) => (
                  <tr key={i}>
                    <td>{apiDateTime(e.createdAtUtc)}</td>
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
        {card && detail?.memberNo && detail.memberBarcode && (
          <MemberCard
            data={{
              name: detail.name, memberNo: detail.memberNo, memberBarcode: detail.memberBarcode,
              tier: detail.membership?.tier, renewalDay: detail.membership?.renewalDay,
            }}
            onClose={() => setCard(false)}
          />
        )}
      </div>
    </div>
  );
}
