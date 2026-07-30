import { useEffect, useState } from "react";
import { createCustomer, fetchLoyalty, gbp, type LoyaltyRow } from "./api.ts";
import { SortTh, useSort } from "./sortable.tsx";
import CustomerDialog from "./CustomerDialog.tsx";

/** Members & store-credit view — customers who are members or hold a credit balance. WP5.1: now
 *  EDITABLE where you look at it — each row opens the shared CustomerDialog (details/credit/
 *  membership), and "Add member" creates a customer then opens the dialog to set their tier. */
export default function LoyaltyPage() {
  const [search, setSearch] = useState("");
  const [rows, setRows] = useState<LoyaltyRow[]>([]);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [open, setOpen] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);
  const [form, setForm] = useState({ name: "", email: "", phone: "" });

  const refresh = () => {
    setLoading(true); setError("");
    fetchLoyalty(search || undefined)
      .then((r) => setRows(r.rows))
      .catch((e) => setError(String(e instanceof Error ? e.message : e)))
      .finally(() => setLoading(false));
  };
  useEffect(refresh, [search]); // eslint-disable-line react-hooks/exhaustive-deps

  async function submitAdd(e: React.FormEvent) {
    e.preventDefault();
    try {
      const { id } = await createCustomer({ name: form.name, email: form.email || undefined, phone: form.phone || undefined });
      setAdding(false);
      setForm({ name: "", email: "", phone: "" });
      setOpen(id); // straight into the dialog to set their membership tier
    } catch (err) {
      setError(String(err instanceof Error ? err.message : err));
    }
  }

  const members = rows.filter((r) => r.tier).length;
  const totalCredit = rows.reduce((s, r) => s + r.creditBalancePence, 0);
  const so = useSort(rows, "creditBalancePence", "desc");

  return (
    <section className="panel">
      <div className="toolbar" style={{ justifyContent: "space-between" }}>
        <h2>Loyalty &amp; store credit</h2>
        <button className="primary" onClick={() => setAdding(true)}>Add member</button>
      </div>
      <div className="toolbar">
        <label>Search <input placeholder="name / email" value={search} onChange={(e) => setSearch(e.target.value)} /></label>
      </div>
      <div className="stat-row">
        <div className="stat"><span className="stat-label">Members</span><span className="stat-value">{members}</span></div>
        <div className="stat"><span className="stat-label">Credit holders</span><span className="stat-value">{rows.length}</span></div>
        <div className="stat"><span className="stat-label">Outstanding credit</span><span className="stat-value">{gbp(totalCredit)}</span></div>
      </div>
      {error && <p className="error">{error}</p>}
      {loading ? <p className="muted">Loading…</p> : (
        <table>
          <thead><tr>
            <SortTh label="Customer" k="name" {...so} />
            <SortTh label="Tier" k="tier" {...so} />
            <SortTh label="Discount" k="autoDiscountRate" num {...so} />
            <SortTh label="Renews" k="renewalDay" {...so} />
            <SortTh label="Credit balance" k="creditBalancePence" num {...so} />
            <th />
          </tr></thead>
          <tbody>
            {so.sorted.map((r) => (
              <tr key={r.id}>
                <td>{r.name}{r.email && <span className="muted small"> · {r.email}</span>}</td>
                <td>{r.tier ?? <span className="muted">—</span>}{r.expired && <span className="error small"> (expired)</span>}</td>
                <td className="num">{r.autoDiscountRate ? `${Math.round(r.autoDiscountRate * 100)}%` : "—"}</td>
                <td>{r.renewalDay ?? "—"}</td>
                <td className="num">{gbp(r.creditBalancePence)}</td>
                <td><button className="ghost small" onClick={() => setOpen(r.id)}>Open</button></td>
              </tr>
            ))}
            {rows.length === 0 && <tr><td colSpan={6} className="muted">No members or credit holders yet.</td></tr>}
          </tbody>
        </table>
      )}

      {adding && (
        <div className="overlay" onClick={(e) => e.target === e.currentTarget && setAdding(false)}>
          <form className="dialog" onSubmit={submitAdd}>
            <h3>Add member</h3>
            <label>Name <input required value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} /></label>
            <label>Email <input type="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} /></label>
            <label>Phone <input value={form.phone} onChange={(e) => setForm({ ...form, phone: e.target.value })} /></label>
            <p className="muted small">Create the customer, then set their membership tier in the next step.</p>
            <div className="dialog-actions">
              <button type="button" className="ghost" onClick={() => setAdding(false)}>Cancel</button>
              <button className="primary" disabled={!form.name.trim()}>Create</button>
            </div>
          </form>
        </div>
      )}

      {open && <CustomerDialog id={open} onClose={() => { setOpen(null); refresh(); }} />}
    </section>
  );
}
