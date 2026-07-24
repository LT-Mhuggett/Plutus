import { useEffect, useState } from "react";
import { downloadSalesReport, fetchSales, type Sale } from "./api.ts";
import { gbp } from "./money.ts";
import SaleDetailDialog from "./reporting/SaleDetailDialog.tsx";

const dateInput = (d: Date) => d.toISOString().slice(0, 10);

export default function StatisticsPage() {
  const [from, setFrom] = useState(() => {
    const d = new Date();
    d.setDate(d.getDate() - 30);
    return dateInput(d);
  });
  const [to, setTo] = useState(() => dateInput(new Date()));
  const [sales, setSales] = useState<Sale[] | null>(null);
  const [state, setState] = useState<"idle" | "loading" | "error">("idle");
  const [error, setError] = useState("");
  const [downloading, setDownloading] = useState(false);
  const [openSale, setOpenSale] = useState<string | null>(null);

  function load() {
    setState("loading");
    setError("");
    fetchSales(new Date(from), new Date(to))
      .then((data) => {
        setSales(data);
        setState("idle");
      })
      .catch((e) => {
        setError(String(e));
        setState("error");
      });
  }

  useEffect(load, []); // initial 30-day window

  async function download() {
    setDownloading(true);
    setError("");
    try {
      await downloadSalesReport(new Date(from), new Date(to));
    } catch (e) {
      setError(String(e));
    } finally {
      setDownloading(false);
    }
  }

  const totalPence = sales?.reduce((t, s) => t + Math.round(s.total * 100), 0) ?? 0;
  const exTaxPence = sales?.reduce((t, s) => t + Math.round(s.totalExTax * 100), 0) ?? 0;
  const shown = sales ? [...sales].sort((a, b) => b.dateOfSale.localeCompare(a.dateOfSale)).slice(0, 100) : [];

  // Content-only: rendered inside ReportingPage's panel as the "Custom" sub-tab.
  return (
    <div>
      <div className="range-row">
        <input type="date" value={from} max={to} onChange={(e) => setFrom(e.target.value)} />
        <span className="muted">to</span>
        <input type="date" value={to} min={from} onChange={(e) => setTo(e.target.value)} />
        <button className="ghost" onClick={load} disabled={state === "loading"}>
          Load
        </button>
        <button className="ghost" onClick={download} disabled={downloading}>
          {downloading ? "Preparing…" : "Download Excel report"}
        </button>
      </div>

      {error && <p className="error small">{error}</p>}
      {state === "loading" && <p className="muted">Loading…</p>}

      {sales && state === "idle" && (
        <>
          <div className="stat-tiles">
            <div className="stat-tile">
              <span className="stat-value">{sales.length}</span>
              <span className="stat-label">sales</span>
            </div>
            <div className="stat-tile">
              <span className="stat-value">{gbp(exTaxPence)}</span>
              <span className="stat-label">ex tax</span>
            </div>
            <div className="stat-tile">
              <span className="stat-value">{gbp(totalPence - exTaxPence)}</span>
              <span className="stat-label">tax</span>
            </div>
            <div className="stat-tile">
              <span className="stat-value">{gbp(totalPence)}</span>
              <span className="stat-label">total (inc tax)</span>
            </div>
            <div className="stat-tile">
              <span className="stat-value">{sales.length ? gbp(Math.round(totalPence / sales.length)) : "—"}</span>
              <span className="stat-label">average sale</span>
            </div>
          </div>

          <table>
            <thead>
              <tr>
                <th>Date</th>
                <th>Sale id</th>
                <th className="num">Net (ex VAT)</th>
                <th className="num">VAT</th>
                <th className="num">Total</th>
              </tr>
            </thead>
            <tbody>
              {shown.map((s) => (
                <tr key={s.id} className="clickable" title="Open sale detail" onClick={() => setOpenSale(s.id)}>
                  <td>{new Date(s.dateOfSale).toLocaleString("en-GB")}</td>
                  <td className="mono small">{s.id}</td>
                  <td className="num">{gbp(Math.round(s.totalExTax * 100))}</td>
                  <td className="num">{gbp(Math.round((s.total - s.totalExTax) * 100))}</td>
                  <td className="num">{gbp(Math.round(s.total * 100))}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {sales.length > 100 && <p className="muted small">Showing latest 100 of {sales.length} — the Excel report contains everything.</p>}
          <p className="muted small">Click a sale to see its items, payments and notes — and reprint a copy receipt.</p>
        </>
      )}
      {openSale && <SaleDetailDialog saleId={openSale} onClose={() => setOpenSale(null)} />}
    </div>
  );
}
