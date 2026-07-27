import { useEffect, useState } from "react";
import { fetchLoyalty, type V1LoyaltyRow } from "./api.ts";
import { gbp } from "./money.ts";

/** Members & store-credit view — customers who are members or hold a balance. */
export default function LoyaltyPage() {
  const [search, setSearch] = useState("");
  const [rows, setRows] = useState<V1LoyaltyRow[]>([]);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    setLoading(true); setError("");
    fetchLoyalty(search)
      .then((r) => setRows(r.rows))
      .catch((e) => setError(String(e instanceof Error ? e.message : e)))
      .finally(() => setLoading(false));
  }, [search]);

  return (
    <section className="panel">
      <div className="panel-head"><h2>Loyalty &amp; store credit</h2></div>
      <div className="toolbar">
        <label>Search <input placeholder="name / email" value={search} onChange={(e) => setSearch(e.target.value)} /></label>
      </div>
      {error && <p className="error">{error}</p>}
      {loading ? <p className="muted">Loading…</p> : (
        <table>
          <thead><tr><th>Customer</th><th>Tier</th><th className="num">Discount</th><th>Renews</th><th className="num">Credit</th></tr></thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id}>
                <td>{r.name}{r.email && <span className="muted small block">{r.email}</span>}</td>
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
