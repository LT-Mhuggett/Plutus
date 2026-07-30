import { useEffect, useState } from "react";
import { createCustomer, fetchLoyalty, gbp, type LoyaltyRow } from "./api.ts";
import DataTable from "./DataTable.tsx";
import CustomerDialog from "./CustomerDialog.tsx";

/** Members & store-credit view — customers who are members or hold a credit balance. WP5.1: now
 *  EDITABLE where you look at it — each row opens the shared CustomerDialog (details/credit/
 *  membership), and "Add member" creates a customer then opens the dialog to set their tier.
 *  WP1.3: the list is the standard DataTable (client sort/search/paginate). */
export default function LoyaltyPage() {
  const [rows, setRows] = useState<LoyaltyRow[]>([]);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [open, setOpen] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);
  const [form, setForm] = useState({ name: "", email: "", phone: "" });

  const refresh = () => {
    setLoading(true); setError("");
    fetchLoyalty()
      .then((r) => setRows(r.rows))
      .catch((e) => setError(String(e instanceof Error ? e.message : e)))
      .finally(() => setLoading(false));
  };
  useEffect(refresh, []); // eslint-disable-line react-hooks/exhaustive-deps

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

  return (
    <section className="panel">
      <div className="toolbar" style={{ justifyContent: "space-between" }}>
        <h2>Loyalty &amp; store credit</h2>
        <button className="primary" onClick={() => setAdding(true)}>Add member</button>
      </div>
      <div className="stat-row">
        <div className="stat"><span className="stat-label">Members</span><span className="stat-value">{members}</span></div>
        <div className="stat"><span className="stat-label">Credit holders</span><span className="stat-value">{rows.length}</span></div>
        <div className="stat"><span className="stat-label">Outstanding credit</span><span className="stat-value">{gbp(totalCredit)}</span></div>
      </div>
      {error && <p className="error">{error}</p>}
      {loading ? <p className="muted">Loading…</p> : (
        <DataTable<LoyaltyRow>
          columns={[
            { key: "name", label: "Customer", render: (r) => <>{r.name}{r.email && <span className="muted small"> · {r.email}</span>}</> },
            { key: "tier", label: "Tier", render: (r) => <>{r.tier ?? <span className="muted">—</span>}{r.expired && <span className="error small"> (expired)</span>}</> },
            { key: "autoDiscountRate", label: "Discount", numeric: true, render: (r) => (r.autoDiscountRate ? `${Math.round(r.autoDiscountRate * 100)}%` : "—") },
            { key: "renewalDay", label: "Renews", render: (r) => r.renewalDay ?? "—" },
            { key: "creditBalancePence", label: "Credit balance", numeric: true, render: (r) => gbp(r.creditBalancePence) },
          ]}
          rows={rows} getKey={(r) => r.id} initialSortKey="creditBalancePence" initialSortDir="desc"
          search={(r) => `${r.name} ${r.email ?? ""} ${r.tier ?? ""}`}
          searchPlaceholder="Search name / email / tier…"
          rowActions={(r) => <button className="ghost small" onClick={() => setOpen(r.id)}>Open</button>}
          emptyText="No members or credit holders yet."
        />
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
