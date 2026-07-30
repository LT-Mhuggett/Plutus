import { useEffect, useState } from "react";
import { fetchSales, gbp, type SaleRow } from "./api.ts";
import SaleDialog from "./SaleDialog.tsx";

// WP3.2 Custom report (portal, net-new): a date-range sales listing with per-sale drill-in (the
// shared SaleDialog) and a client-side CSV export — the portal equivalent of the till's Statistics
// page. Reads the v1 sales list (SalesV2); amounts are pence.

const iso = (d: Date) => d.toISOString().slice(0, 10);

export default function CustomReport() {
  const [from, setFrom] = useState(() => iso(new Date(Date.now() - 30 * 86400_000)));
  const [to, setTo] = useState(() => iso(new Date()));
  const [sales, setSales] = useState<SaleRow[] | null>(null);
  const [state, setState] = useState<"idle" | "loading" | "error">("idle");
  const [error, setError] = useState("");
  const [openSale, setOpenSale] = useState<string | null>(null);

  const load = () => {
    setState("loading"); setError("");
    fetchSales(from, to)
      .then((data) => { setSales(data); setState("idle"); })
      .catch((e) => { setError(String(e instanceof Error ? e.message : e)); setState("error"); });
  };
  useEffect(load, []); // initial 30-day window

  const totalPence = sales?.reduce((t, s) => t + s.grossPence, 0) ?? 0;
  const vatPence = sales?.reduce((t, s) => t + s.vatPence, 0) ?? 0;
  const exTaxPence = totalPence - vatPence;
  const shown = sales ? [...sales].sort((a, b) => b.occurredAtUtc.localeCompare(a.occurredAtUtc)).slice(0, 100) : [];

  const downloadCsv = () => {
    if (!sales) return;
    const rows = [["sale_id", "date", "net_ex_vat", "vat", "total_inc_vat"]];
    for (const s of [...sales].sort((a, b) => b.occurredAtUtc.localeCompare(a.occurredAtUtc)))
      rows.push([s.id, s.occurredAtUtc, ((s.grossPence - s.vatPence) / 100).toFixed(2), (s.vatPence / 100).toFixed(2), (s.grossPence / 100).toFixed(2)]);
    const csv = rows.map((r) => r.map((c) => (c.includes(",") ? `"${c}"` : c)).join(",")).join("\n");
    const url = URL.createObjectURL(new Blob([csv], { type: "text/csv" }));
    const a = document.createElement("a");
    a.href = url; a.download = `${from}-${to}-sales.csv`; a.click();
    URL.revokeObjectURL(url);
  };

  return (
    <section className="panel">
      <div className="toolbar">
        <label>From <input type="date" value={from} max={to} onChange={(e) => setFrom(e.target.value)} /></label>
        <label>To <input type="date" value={to} min={from} onChange={(e) => setTo(e.target.value)} /></label>
        <button className="ghost small" onClick={load} disabled={state === "loading"}>Load</button>
        <span className="grow" />
        <button className="ghost btn" onClick={downloadCsv} disabled={!sales || sales.length === 0}>Export CSV</button>
      </div>

      {error && <p className="error">{error}</p>}
      {state === "loading" && <p className="muted">Loading…</p>}

      {sales && state === "idle" && (
        <>
          <div className="stat-row">
            <div className="stat"><span className="stat-label">Sales</span><span className="stat-value">{sales.length}</span></div>
            <div className="stat"><span className="stat-label">Net (ex VAT)</span><span className="stat-value">{gbp(exTaxPence)}</span></div>
            <div className="stat"><span className="stat-label">VAT</span><span className="stat-value">{gbp(vatPence)}</span></div>
            <div className="stat"><span className="stat-label">Total (inc VAT)</span><span className="stat-value">{gbp(totalPence)}</span></div>
            <div className="stat"><span className="stat-label">Average sale</span><span className="stat-value">{sales.length ? gbp(Math.round(totalPence / sales.length)) : "—"}</span></div>
          </div>

          <table>
            <thead><tr><th>Date</th><th>Sale id</th><th>Channel</th><th className="num">Net (ex VAT)</th><th className="num">VAT</th><th className="num">Total</th></tr></thead>
            <tbody>
              {shown.map((s) => (
                <tr key={s.id} className="clickable" title="Open sale detail" onClick={() => setOpenSale(s.id)} style={{ cursor: "pointer" }}>
                  <td>{new Date(s.occurredAtUtc + "Z").toLocaleString("en-GB")}</td>
                  <td className="mono small">{s.id.slice(0, 8)}…</td>
                  <td>{s.channel}{s.legacyRef ? " (migrated)" : ""}</td>
                  <td className="num">{gbp(s.grossPence - s.vatPence)}</td>
                  <td className="num">{gbp(s.vatPence)}</td>
                  <td className="num">{gbp(s.grossPence)}</td>
                </tr>
              ))}
              {sales.length === 0 && <tr><td colSpan={6} className="muted">No sales in this range.</td></tr>}
            </tbody>
          </table>
          {sales.length > 100 && <p className="muted small">Showing the latest 100 of {sales.length} — the CSV export contains everything loaded.</p>}
          <p className="muted small">Click a sale to see its items, payments and notes — and reprint a copy receipt.</p>
        </>
      )}

      {openSale && <SaleDialog id={openSale} onClose={() => setOpenSale(null)} />}
    </section>
  );
}
