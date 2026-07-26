import { useEffect, useState } from "react";
import { fetchLoyalty, gbp, type LoyaltyRow } from "./api.ts";

/** Members & store-credit view — customers who are members or hold a credit balance. */
export default function LoyaltyPage() {
  const [search, setSearch] = useState("");
  const [rows, setRows] = useState<LoyaltyRow[]>([]);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    setLoading(true);
    setError("");
    fetchLoyalty(search || undefined)
      .then((r) => setRows(r.rows))
      .catch((e) => setError(String(e instanceof Error ? e.message : e)))
      .finally(() => setLoading(false));
  }, [search]);

  const members = rows.filter((r) => r.tier).length;
  const totalCredit = rows.reduce((s, r) => s + r.creditBalancePence, 0);

  return (
    <section className="panel">
      <h2>Loyalty &amp; store credit</h2>
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
          <thead><tr><th>Customer</th><th>Tier</th><th className="num">Discount</th><th>Renews</th><th className="num">Credit balance</th></tr></thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id}>
                <td>{r.name}{r.email && <span className="muted small"> · {r.email}</span>}</td>
                <td>{r.tier ?? <span className="muted">—</span>}{r.expired && <span className="error small"> (expired)</span>}</td>
                <td className="num">{r.autoDiscountRate ? `${Math.round(r.autoDiscountRate * 100)}%` : "—"}</td>
                <td>{r.renewalDay ?? "—"}</td>
                <td className="num">{gbp(r.creditBalancePence)}</td>
              </tr>
            ))}
            {rows.length === 0 && <tr><td colSpan={5} className="muted">No members or credit holders yet.</td></tr>}
          </tbody>
        </table>
      )}
    </section>
  );
}
