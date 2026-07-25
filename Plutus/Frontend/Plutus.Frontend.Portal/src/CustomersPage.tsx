import { useEffect, useState } from "react";
import { ApiError, gbp } from "./api.ts";

// Phase 8 portal: customers, store-credit ledger (issue/view), membership.

interface CustomerRow { id: string; name: string; email: string | null; phone: string | null }
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
  const s = JSON.parse(localStorage.getItem("plutus.portal.session") ?? "null");
  const res = await fetch(url, {
    method,
    headers: { ...(s ? { Authorization: `Bearer ${s.token}` } : {}), ...(body !== undefined ? { "Content-Type": "application/json" } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!res.ok) {
    let detail = `${res.status}`;
    try { detail = (await res.json())?.detail ?? detail; } catch { /* keep */ }
    throw new ApiError(res.status, detail);
  }
  return res.status === 204 ? (undefined as T) : res.json();
}

const toPence = (s: string) => Math.round(parseFloat(s) * 100);

export default function CustomersPage() {
  const [search, setSearch] = useState("");
  const [rows, setRows] = useState<CustomerRow[]>([]);
  const [open, setOpen] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [form, setForm] = useState({ name: "", email: "", phone: "" });
  const [error, setError] = useState("");

  const refresh = () =>
    j<CustomerRow[]>("GET", `/api/v1/customers?take=100${search ? `&search=${encodeURIComponent(search)}` : ""}`)
      .then((r) => { setRows(r); setError(""); })
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));
  useEffect(() => { void refresh(); }, [search]); // eslint-disable-line react-hooks/exhaustive-deps

  async function submitCreate(e: React.FormEvent) {
    e.preventDefault();
    try {
      await j("POST", `/api/v1/customers`, { name: form.name, email: form.email || undefined, phone: form.phone || undefined });
      setCreating(false);
      setForm({ name: "", email: "", phone: "" });
      await refresh();
    } catch (err) {
      setError(String(err instanceof Error ? err.message : err));
    }
  }

  return (
    <section className="panel">
      <div className="toolbar">
        <label>Search <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="name / email / phone" /></label>
        <button className="primary" onClick={() => setCreating(true)}>Add customer</button>
      </div>
      {error && <p className="error">{error}</p>}

      <table>
        <thead><tr><th>Name</th><th>Email</th><th>Phone</th><th /></tr></thead>
        <tbody>
          {rows.map((c) => (
            <tr key={c.id}>
              <td>{c.name}</td><td>{c.email}</td><td>{c.phone}</td>
              <td><button className="ghost small" onClick={() => setOpen(c.id)}>Open</button></td>
            </tr>
          ))}
          {rows.length === 0 && <tr><td colSpan={4} className="muted">No customers.</td></tr>}
        </tbody>
      </table>

      {creating && (
        <div className="overlay" onClick={(e) => e.target === e.currentTarget && setCreating(false)}>
          <form className="dialog" onSubmit={submitCreate}>
            <h3>Add customer</h3>
            <label>Name <input required value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} /></label>
            <label>Email <input type="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} /></label>
            <label>Phone <input value={form.phone} onChange={(e) => setForm({ ...form, phone: e.target.value })} /></label>
            <div className="dialog-actions">
              <button type="button" className="ghost" onClick={() => setCreating(false)}>Cancel</button>
              <button className="primary">Create</button>
            </div>
          </form>
        </div>
      )}

      {open && <CustomerDialog id={open} onClose={() => { setOpen(null); void refresh(); }} />}
    </section>
  );
}

function CustomerDialog({ id, onClose }: { id: string; onClose: () => void }) {
  const [detail, setDetail] = useState<CustomerDetail | null>(null);
  const [credit, setCredit] = useState<CreditView | null>(null);
  const [error, setError] = useState("");
  const [issue, setIssue] = useState("");
  const [issueReason, setIssueReason] = useState("");
  const [tier, setTier] = useState("Club");
  const [rate, setRate] = useState("10");

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
            <h3>{detail.name}</h3>
            <dl className="kv">
              <dt>Email</dt><dd>{detail.email ?? "—"}</dd>
              <dt>Phone</dt><dd>{detail.phone ?? "—"}</dd>
              <dt>Store credit</dt><dd><strong>{gbp(detail.creditBalancePence)}</strong></dd>
              <dt>Membership</dt>
              <dd>{detail.membership
                ? `${detail.membership.tier} · ${(detail.membership.autoDiscountRate * 100).toFixed(0)}% · renews ${detail.membership.renewalDay}${detail.membership.expired ? " (EXPIRED)" : ""}`
                : "—"}</dd>
            </dl>

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
