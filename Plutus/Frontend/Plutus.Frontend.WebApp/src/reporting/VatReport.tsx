import { useEffect, useState } from "react";
import { fetchSalesSummary, fetchVatIntegrity, type SalesSummary, type VatIntegrity } from "../api.ts";
import { gbp } from "../money.ts";

const p = (pounds: number) => Math.round(pounds * 100);
const MONTHS = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

/** "Calculate my VAT" — month or quarter view of output VAT on sales. */
export default function VatReport() {
  const now = new Date();
  const [view, setView] = useState<"month" | "quarter">("quarter");
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState(now.getMonth()); // 0-based
  const [quarter, setQuarter] = useState(Math.floor(now.getMonth() / 3) + 1);
  const [data, setData] = useState<SalesSummary | null>(null);
  const [state, setState] = useState<"loading" | "ready" | "error">("loading");
  const [error, setError] = useState("");
  const [integrity, setIntegrity] = useState<VatIntegrity | null>(null);
  const [showOffenders, setShowOffenders] = useState(false);

  // Guardrail (VAT plan §5.4): surface catalogue price/band inconsistencies loudly.
  useEffect(() => {
    fetchVatIntegrity().then(setIntegrity).catch(() => undefined);
  }, []);

  const from = view === "month" ? new Date(Date.UTC(year, month, 1)) : new Date(Date.UTC(year, (quarter - 1) * 3, 1));
  const to = view === "month" ? new Date(Date.UTC(year, month + 1, 0)) : new Date(Date.UTC(year, quarter * 3, 0));

  useEffect(() => {
    setState("loading");
    fetchSalesSummary(from, to)
      .then((d) => {
        setData(d);
        setState("ready");
      })
      .catch((e) => {
        setError(String(e));
        setState("error");
      });
    // eslint-style deps: recompute when the period changes
  }, [view, year, month, quarter]);

  const years = Array.from({ length: now.getFullYear() - 2019 + 1 }, (_, i) => 2019 + i).reverse();
  const vat = data ? data.totalSales - data.totalSalesExTax : 0;
  const rateVat = data?.byTaxRate.reduce((t, r) => t + r.vat, 0) ?? 0;
  const unallocated = vat - rateVat;

  return (
    <div>
      <div className="range-row">
        <select value={view} onChange={(e) => setView(e.target.value as "month" | "quarter")}>
          <option value="month">Month</option>
          <option value="quarter">Quarter</option>
        </select>
        {view === "month" ? (
          <select value={month} onChange={(e) => setMonth(Number(e.target.value))}>
            {MONTHS.map((m, i) => (
              <option key={m} value={i}>
                {m}
              </option>
            ))}
          </select>
        ) : (
          <select value={quarter} onChange={(e) => setQuarter(Number(e.target.value))}>
            {[1, 2, 3, 4].map((q) => (
              <option key={q} value={q}>
                Q{q} (
                {MONTHS[(q - 1) * 3].slice(0, 3)}–{MONTHS[q * 3 - 1].slice(0, 3)})
              </option>
            ))}
          </select>
        )}
        <select value={year} onChange={(e) => setYear(Number(e.target.value))}>
          {years.map((y) => (
            <option key={y} value={y}>
              {y}
            </option>
          ))}
        </select>
        <span className="muted small">
          {from.toLocaleDateString("en-GB")} – {to.toLocaleDateString("en-GB")}
        </span>
      </div>

      {integrity && integrity.offBandCount > 0 && (
        <div className="integrity-banner">
          <strong>⚠ {integrity.offBandCount} items</strong> have prices inconsistent with their VAT band — VAT figures
          involving them are unreliable. New items are now validated at entry; these are legacy records awaiting repair.
          <button className="linklike small" onClick={() => setShowOffenders((v) => !v)}>
            {showOffenders ? "hide list" : "show list"}
          </button>
          {showOffenders && (
            <table className="small">
              <thead>
                <tr>
                  <th>Barcode / id</th>
                  <th>Name</th>
                  <th>Band</th>
                  <th className="num">Price</th>
                  <th className="num">Ex VAT (stored)</th>
                  <th className="num">Price implied by band</th>
                </tr>
              </thead>
              <tbody>
                {integrity.offBandItems.map((i) => (
                  <tr key={i.id}>
                    <td className="mono">{i.id}</td>
                    <td>{i.name}</td>
                    <td>{i.band}</td>
                    <td className="num">{gbp(p(i.price))}</td>
                    <td className="num">{gbp(p(i.exPrice))}</td>
                    <td className="num">{gbp(p(i.expectedPrice))}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}

      {state === "error" && <p className="error small">{error}</p>}
      {state === "loading" && <p className="muted">Loading…</p>}

      {state === "ready" && data && (
        <>
          <div className="stat-tiles">
            <div className="stat-tile">
              <span className="stat-value">{gbp(p(vat))}</span>
              <span className="stat-label">VAT on sales (output VAT)</span>
            </div>
            <div className="stat-tile">
              <span className="stat-value">{gbp(p(data.totalSalesExTax))}</span>
              <span className="stat-label">net sales (ex VAT)</span>
            </div>
            <div className="stat-tile">
              <span className="stat-value">{gbp(p(data.totalSales))}</span>
              <span className="stat-label">gross sales (inc VAT)</span>
            </div>
          </div>

          <div className="info-card">
            <h3>By VAT band</h3>
            <table>
              <thead>
                <tr>
                  <th>Band</th>
                  <th className="num">Net</th>
                  <th className="num">VAT</th>
                  <th className="num">Gross</th>
                </tr>
              </thead>
              <tbody>
                {data.byTaxRate.map((r) => (
                  <tr key={r.tax}>
                    <td>{r.tax}</td>
                    <td className="num">{gbp(p(r.net))}</td>
                    <td className="num">{gbp(p(r.vat))}</td>
                    <td className="num">{gbp(p(r.gross))}</td>
                  </tr>
                ))}
                {Math.abs(unallocated) >= 0.01 && (
                  <tr>
                    <td className="muted">Discounts / adjustments not allocated to a band</td>
                    <td className="num muted">—</td>
                    <td className="num muted">{gbp(p(unallocated))}</td>
                    <td className="num muted">—</td>
                  </tr>
                )}
              </tbody>
            </table>
            <p className="muted small">
              Band rows are calculated from transaction line values; the headline VAT figure comes from sale totals
              (which include discounts) — any difference is shown as unallocated. Figures cover sales through this till
              only and are a bookkeeping aid, not a VAT return.
            </p>
          </div>
        </>
      )}
    </div>
  );
}
