import { useEffect, useState } from "react";
import {
  createCustomer, fetchLoyalty, fetchLoyaltyTiers, setMembership, updateCustomer,
  type LoyaltyTier, type V1LoyaltyRow,
} from "./api.ts";
import { gbp } from "./money.ts";
import { canManageCustomers } from "./pipeline.ts";
import DataTable from "./DataTable.tsx";

/** Members & store-credit view. WP5.2: managers (customers.manage) can add a member and edit an
 *  existing one's details + tier here. WP1.3: the list is the standard DataTable.
 *  FE1: the tier is PICKED from the tenant's catalogue (defined in the portal), not typed. */
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
            { key: "memberNo", label: "Member no.", render: (r) => r.memberNo ? <span className="mono small">{r.memberNo}</span> : <span className="muted">—</span> },
            { key: "tier", label: "Tier", render: (r) => <>{r.tier ?? <span className="muted">—</span>}{r.expired && <span className="error small"> (expired)</span>}</> },
            { key: "autoDiscountRate", label: "Discount", numeric: true, render: (r) => (r.autoDiscountRate ? `${Math.round(r.autoDiscountRate * 100)}%` : "—") },
            { key: "renewalDay", label: "Renews", render: (r) => r.renewalDay ?? "—" },
            { key: "creditBalancePence", label: "Credit", numeric: true, render: (r) => gbp(r.creditBalancePence) },
          ]}
          rows={rows} getKey={(r) => r.id} initialSortKey="creditBalancePence" initialSortDir="desc"
          search={(r) => `${r.name} ${r.email ?? ""} ${r.tier ?? ""} ${r.memberNo ?? ""}`}
          searchPlaceholder="Search name / email / tier / member no…"
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
  const [tiers, setTiers] = useState<LoyaltyTier[]>([]);
  const [tierId, setTierId] = useState(row?.tierId ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  // active tiers for the picker; tiers themselves are managed in the portal
  useEffect(() => { void fetchLoyaltyTiers().then(setTiers).catch(() => undefined); }, []);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true); setError("");
    try {
      const body = { name: name.trim(), email: email.trim() || undefined, phone: phone.trim() || undefined };
      const id = row ? (await updateCustomer(row.id, body), row.id) : (await createCustomer(body)).id;
      // assign the tier only when one is picked AND it changed (blank leaves membership untouched)
      if (tierId && tierId !== (row?.tierId ?? "")) await setMembership(id, tierId);
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
          <label>Membership tier
            <select value={tierId} onChange={(e) => setTierId(e.target.value)} disabled={busy || tiers.length === 0}>
              <option value="">{row?.tier ? `${row.tier} (leave unchanged)` : "— none —"}</option>
              {tiers.map((t) => (
                <option key={t.id} value={t.id}>{t.name} · {Math.round(t.autoDiscountRate * 1000) / 10}%</option>
              ))}
            </select>
          </label>
        </div>
        {tiers.length === 0 && (
          <p className="muted small">No loyalty tiers defined yet — a manager sets them up in the management portal (Loyalty → Manage tiers).</p>
        )}
        {error && <p className="error small">{error}</p>}
        <div className="dialog-actions">
          <button type="button" className="ghost" onClick={onClose} disabled={busy}>Cancel</button>
          <button type="submit" className="primary" disabled={busy || !name.trim()}>{busy ? "Saving…" : "Save"}</button>
        </div>
      </form>
    </div>
  );
}
