import { useEffect, useState } from "react";
import { createCustomer, fetchLoyalty, setMembership, updateCustomer, type V1LoyaltyRow } from "./api.ts";
import { gbp } from "./money.ts";
import { canManageCustomers } from "./pipeline.ts";
import DataTable from "./DataTable.tsx";

/** Members & store-credit view. WP5.2: managers (customers.manage) can add a member and edit an
 *  existing one's details + tier here. WP1.3: the list is the standard DataTable. */
export default function LoyaltyPage() {
  const [rows, setRows] = useState<V1LoyaltyRow[]>([]);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [editing, setEditing] = useState<V1LoyaltyRow | "new" | null>(null);
  const canManage = canManageCustomers();

  const refresh = () => {
    setLoading(true); setError("");
    fetchLoyalty()
      .then((r) => setRows(r.rows))
      .catch((e) => setError(String(e instanceof Error ? e.message : e)))
      .finally(() => setLoading(false));
  };
  useEffect(refresh, []); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <section className="panel">
      <div className="panel-head">
        <h2>Loyalty &amp; store credit</h2>
        {canManage && <button className="ghost" onClick={() => setEditing("new")}>Add member</button>}
      </div>
      {error && <p className="error">{error}</p>}
      {loading ? <p className="muted">Loading…</p> : (
        <DataTable<V1LoyaltyRow>
          columns={[
            { key: "name", label: "Customer", render: (r) => <>{r.name}{r.email && <span className="muted small block">{r.email}</span>}</> },
            { key: "tier", label: "Tier", render: (r) => <>{r.tier ?? <span className="muted">—</span>}{r.expired && <span className="error small"> (expired)</span>}</> },
            { key: "autoDiscountRate", label: "Discount", numeric: true, render: (r) => (r.autoDiscountRate ? `${Math.round(r.autoDiscountRate * 100)}%` : "—") },
            { key: "renewalDay", label: "Renews", render: (r) => r.renewalDay ?? "—" },
            { key: "creditBalancePence", label: "Credit", numeric: true, render: (r) => gbp(r.creditBalancePence) },
          ]}
          rows={rows} getKey={(r) => r.id} initialSortKey="creditBalancePence" initialSortDir="desc"
          search={(r) => `${r.name} ${r.email ?? ""} ${r.tier ?? ""}`}
          searchPlaceholder="Search name / email / tier…"
          rowActions={canManage ? (r) => <button className="ghost small" onClick={() => setEditing(r)}>Edit</button> : undefined}
          emptyText="No members or credit holders yet."
        />
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
