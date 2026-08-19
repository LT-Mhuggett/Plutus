import { useEffect, useState } from "react";
import { createCustomer, fetchLoyalty, gbp, type LoyaltyRow } from "./api.ts";
import DataTable from "./DataTable.tsx";
import CustomerDialog from "./CustomerDialog.tsx";
import TierManagerDialog from "./TierManagerDialog.tsx";

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
  const [tiers, setTiers] = useState(false); // FE1: the tier catalogue manager
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

  /**
   * ⚠⚠ BOTH OF THESE WERE WRONG, AND WRONG PLAUSIBLY — Matt, 2026-08-19: *"In the portal in 'Loyalty'
   * is says '3 members' when there are 8."*
   *
   * They were right when written and were falsified by a fix somewhere else. On 2026-08-18
   * `GET /api/v1/loyalty` was widened to return **every active customer** — because
   * `MemberNoAllocator.NextAsync` runs on every create, so *"they ARE a member, and the tier is an
   * upgrade on top"*. That fix corrected the LIST and left these two counters reading the old shape:
   *
   *   • **Members** counted rows with a TIER (3 of 8) — it was measuring the upgrade, not membership;
   *   • **Credit holders** was `rows.length` (8) — every customer, when exactly one held credit.
   *
   * ⚠ So the screen said 3 members and 8 credit holders when the truth is 8 members and 1 credit
   * holder, with £12.50 outstanding — and the £12.50 sitting beside "8" is what makes it read as
   * plausible rather than obviously broken.
   *
   * ⚠ The tier count is KEPT as its own stat rather than deleted: it was the one genuinely useful
   * number here, just under the wrong label.
   */
  const members = rows.filter((r) => r.memberNo).length;
  const onATier = rows.filter((r) => r.tier).length;
  const creditHolders = rows.filter((r) => r.creditBalancePence !== 0).length;
  const totalCredit = rows.reduce((s, r) => s + r.creditBalancePence, 0);

  return (
    <section className="panel">
      <div className="toolbar" style={{ justifyContent: "space-between" }}>
        <h2>Loyalty &amp; store credit</h2>
        <span>
          <button className="ghost" onClick={() => setTiers(true)}>Manage tiers</button>{" "}
          <button className="primary" onClick={() => setAdding(true)}>Add member</button>
        </span>
      </div>
      <div className="stat-row">
        <div className="stat"><span className="stat-label">Members</span><span className="stat-value">{members}</span></div>
        <div className="stat"><span className="stat-label">On a tier</span><span className="stat-value">{onATier}</span></div>
        <div className="stat"><span className="stat-label">Credit holders</span><span className="stat-value">{creditHolders}</span></div>
        <div className="stat"><span className="stat-label">Outstanding credit</span><span className="stat-value">{gbp(totalCredit)}</span></div>
      </div>
      {error && <p className="error">{error}</p>}
      {loading ? <p className="muted">Loading…</p> : (
        <DataTable<LoyaltyRow>
          columns={[
            // ⚠ Matt, 2026-08-19: *"Can the email also be split out into a separate column"*. It used
            // to ride under the name as `Jo Bloggs · jo@example.com`, which cannot be SORTED and cannot
            // be scanned down — and a column of addresses is exactly what somebody chasing a customer
            // reads. ⚠ Its own column also makes the missing ones visible: four of these rows have no
            // email at all, which the inline form hid behind an absent separator.
            { key: "name", label: "Customer" },
            { key: "email", label: "Email", render: (r) => r.email ?? <span className="muted">—</span> },
            { key: "memberNo", label: "Member no.", render: (r) => r.memberNo ? <span className="mono small">{r.memberNo}</span> : <span className="muted">—</span> },
            { key: "tier", label: "Tier", render: (r) => <>{r.tier ?? <span className="muted">—</span>}{r.expired && <span className="error small"> (expired)</span>}</> },
            { key: "autoDiscountRate", label: "Discount", numeric: true, render: (r) => (r.autoDiscountRate ? `${Math.round(r.autoDiscountRate * 100)}%` : "—") },
            { key: "renewalDay", label: "Renews", render: (r) => r.renewalDay ?? "—" },
            { key: "creditBalancePence", label: "Credit balance", numeric: true, render: (r) => gbp(r.creditBalancePence) },
          ]}
          rows={rows} getKey={(r) => r.id} initialSortKey="creditBalancePence" initialSortDir="desc"
          search={(r) => `${r.name} ${r.email ?? ""} ${r.tier ?? ""} ${r.memberNo ?? ""}`}
          searchPlaceholder="Search name / email / tier / member no…"
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

      {tiers && <TierManagerDialog onClose={() => { setTiers(false); refresh(); }} />}
      {open && <CustomerDialog id={open} onClose={() => { setOpen(null); refresh(); }} />}
    </section>
  );
}
