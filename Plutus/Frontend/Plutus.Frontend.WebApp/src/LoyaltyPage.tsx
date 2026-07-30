import { useEffect, useState } from "react";
import { createCustomer, fetchLoyalty, setMembership, updateCustomer, type V1LoyaltyRow } from "./api.ts";
import { gbp } from "./money.ts";
import { canManageCustomers } from "./pipeline.ts";

/** Members & store-credit view. WP5.2: managers (customers.manage) can add a member and edit an
 *  existing one's details + tier here, not only mid-sale on the till page. */
export default function LoyaltyPage() {
  const [search, setSearch] = useState("");
  const [rows, setRows] = useState<V1LoyaltyRow[]>([]);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [editing, setEditing] = useState<V1LoyaltyRow | "new" | null>(null);
  const canManage = canManageCustomers();

  const refresh = () => {
    setLoading(true); setError("");
    fetchLoyalty(search)
      .then((r) => setRows(r.rows))
      .catch((e) => setError(String(e instanceof Error ? e.message : e)))
      .finally(() => setLoading(false));
  };
  useEffect(refresh, [search]); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <section className="panel">
      <div className="panel-head">
        <h2>Loyalty &amp; store credit</h2>
        {canManage && <button className="ghost" onClick={() => setEditing("new")}>Add member</button>}
      </div>
      <div className="toolbar">
        <label>Search <input placeholder="name / email" value={search} onChange={(e) => setSearch(e.target.value)} /></label>
      </div>
      {error && <p className="error">{error}</p>}
      {loading ? <p className="muted">Loading…</p> : (
        <table>
          <thead><tr><th>Customer</th><th>Tier</th><th className="num">Discount</th><th>Renews</th><th className="num">Credit</th>{canManage && <th />}</tr></thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id}>
                <td>{r.name}{r.email && <span className="muted small block">{r.email}</span>}</td>
                <td>{r.tier ?? <span className="muted">—</span>}{r.expired && <span className="error small"> (expired)</span>}</td>
                <td className="num">{r.autoDiscountRate ? `${Math.round(r.autoDiscountRate * 100)}%` : "—"}</td>
                <td>{r.renewalDay ?? "—"}</td>
                <td className="num">{gbp(r.creditBalancePence)}</td>
                {canManage && <td><button className="ghost small" onClick={() => setEditing(r)}>Edit</button></td>}
              </tr>
            ))}
            {rows.length === 0 && <tr><td colSpan={canManage ? 6 : 5} className="muted">No members or credit holders yet.</td></tr>}
          </tbody>
        </table>
      )}

      {editing && (
        <MemberDialog
          row={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onDone={() => { setEditing(null); refresh(); }}
        />
      )}
    </section>
  );
}

function MemberDialog({ row, onClose, onDone }: { row: V1LoyaltyRow | null; onClose: () => void; onDone: () => void }) {
  const [name, setName] = useState(row?.name ?? "");
  const [email, setEmail] = useState(row?.email ?? "");
  const [phone, setPhone] = useState(row?.phone ?? "");
  const [tier, setTier] = useState(row?.tier ?? "");
  const [rate, setRate] = useState(row?.autoDiscountRate != null ? String(Math.round(row.autoDiscountRate * 100)) : "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true); setError("");
    try {
      const body = { name: name.trim(), email: email.trim() || undefined, phone: phone.trim() || undefined };
      const id = row ? (await updateCustomer(row.id, body), row.id) : (await createCustomer(body)).id;
      // set the tier only when one is entered (blank leaves membership untouched)
      if (tier.trim()) await setMembership(id, tier.trim(), (parseFloat(rate) || 0) / 100);
      onDone();
    } catch (err) {
      setError(String(err instanceof Error ? err.message : err));
      setBusy(false);
    }
  }

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && !busy && onClose()}>
      <form className="dialog" onSubmit={submit}>
        <h2>{row ? "Edit member" : "Add member"}</h2>
        <div className="form-grid">
          <label>Name <input value={name} onChange={(e) => setName(e.target.value)} required disabled={busy} /></label>
          <label>Email <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} disabled={busy} /></label>
          <label>Phone <input value={phone} onChange={(e) => setPhone(e.target.value)} disabled={busy} /></label>
          <label>Membership tier <input value={tier} onChange={(e) => setTier(e.target.value)} placeholder="e.g. Club (blank = none)" disabled={busy} /></label>
          <label>Discount % <input inputMode="numeric" value={rate} onChange={(e) => setRate(e.target.value)} disabled={busy || !tier.trim()} /></label>
        </div>
        {error && <p className="error small">{error}</p>}
        <div className="dialog-actions">
          <button type="button" className="ghost" onClick={onClose} disabled={busy}>Cancel</button>
          <button type="submit" className="primary" disabled={busy || !name.trim()}>{busy ? "Saving…" : "Save"}</button>
        </div>
      </form>
    </div>
  );
}
