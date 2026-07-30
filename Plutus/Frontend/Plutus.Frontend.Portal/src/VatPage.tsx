import { useEffect, useMemo, useState } from "react";
import { downloadCsv, csvUrl, fetchVat, fetchVatIntegrity, gbp, type VatBucket, type VatIntegrity } from "./api.ts";

// WP3.5 VAT: the till's VatReport UX (month/quarter/year, stat tiles, by-band table, off-band
// integrity banner) — but the NUMBERS stay on /api/v1/reports/vat (VatRollups), which honours
// financial-period locks (EffectiveDay late-post redirect); the till's summary-rich buckets by raw
// business day. Rollup bands are line-derived, so Σ band VAT == the headline VAT (no "unallocated").

const MONTHS = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
const isoDay = (d: Date) => d.toISOString().slice(0, 10);
const rateLabel = (bp: number) => (bp === 0 ? "Zero" : `${(bp / 100).toFixed(2)}%`);

export default function VatPage() {
  const now = new Date();
  const [view, setView] = useState<"month" | "quarter">("quarter");
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState(now.getMonth());
  const [quarter, setQuarter] = useState(Math.floor(now.getMonth() / 3) + 1);
  const [buckets, setBuckets] = useState<VatBucket[]>([]);
  const [totals, setTotals] = useState<{ grossPence: number; netPence: number; vatPence: number } | null>(null);
  const [integrity, setIntegrity] = useState<VatIntegrity | null>(null);
  const [showOffenders, setShowOffenders] = useState(false);
  const [error, setError] = useState("");

  const from = view === "month" ? isoDay(new Date(Date.UTC(year, month, 1))) : isoDay(new Date(Date.UTC(year, (quarter - 1) * 3, 1)));
  const to = view === "month" ? isoDay(new Date(Date.UTC(year, month + 1, 0))) : isoDay(new Date(Date.UTC(year, quarter * 3, 0)));

  useEffect(() => { fetchVatIntegrity().then(setIntegrity).catch(() => undefined); }, []);
  useEffect(() => {
    setError("");
    // granularity "year" collapses period grouping; we re-aggregate by band below regardless.
    fetchVat(from, to, "year")
      .then((r) => { setBuckets(r.buckets); setTotals(r.totals); })
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));
  }, [view, year, month, quarter]);

  // Sum the rollup buckets by VAT band (over whatever periods the range spans).
  const byBand = useMemo(() => {
    const m = new Map<number, { gross: number; net: number; vat: number }>();
    for (const b of buckets) {
      const r = m.get(b.vatRateBp) ?? { gross: 0, net: 0, vat: 0 };
      r.gross += b.grossPence; r.net += b.netPence; r.vat += b.vatPence;
      m.set(b.vatRateBp, r);
    }
    return [...m.entries()].map(([bp, v]) => ({ bp, ...v })).sort((a, b) => b.gross - a.gross);
  }, [buckets]);

  const years = Array.from({ length: now.getFullYear() - 2019 + 1 }, (_, i) => 2019 + i).reverse();

  return (
    <section className="panel">
      <div className="toolbar">
        <label>View{" "}
          <select value={view} onChange={(e) => setView(e.target.value as "month" | "quarter")}>
            <option value="month">Month</option><option value="quarter">Quarter</option>
          </select>
        </label>
        {view === "month" ? (
          <label>Month{" "}
            <select value={month} onChange={(e) => setMonth(Number(e.target.value))}>
              {MONTHS.map((m, i) => <option key={m} value={i}>{m}</option>)}
            </select>
          </label>
        ) : (
          <label>Quarter{" "}
            <select value={quarter} onChange={(e) => setQuarter(Number(e.target.value))}>
              {[1, 2, 3, 4].map((q) => <option key={q} value={q}>Q{q} ({MONTHS[(q - 1) * 3].slice(0, 3)}–{MONTHS[q * 3 - 1].slice(0, 3)})</option>)}
            </select>
          </label>
        )}
        <label>Year{" "}
          <select value={year} onChange={(e) => setYear(Number(e.target.value))}>
            {years.map((y) => <option key={y} value={y}>{y}</option>)}
          </select>
        </label>
        <span className="muted small">{from} – {to}</span>
        <span className="grow" />
        <button className="ghost btn" onClick={() => void downloadCsv(csvUrl("vat", from, to), `${from}-${to}-vat.csv`)}>Export CSV</button>
      </div>

      {integrity && integrity.offBandCount > 0 && (
        <div className="error" style={{ padding: "8px 12px" }}>
          <strong>⚠ {integrity.offBandCount} items</strong> have prices inconsistent with their VAT band — VAT figures
          involving them are unreliable. New items are validated at entry; these are legacy records awaiting repair.{" "}
          <button className="linklike small" onClick={() => setShowOffenders((v) => !v)}>{showOffenders ? "hide list" : "show list"}</button>
          {showOffenders && (
            <table className="small">
              <thead><tr><th>Barcode / id</th><th>Name</th><th>Band</th><th className="num">Price</th><th className="num">Ex VAT (stored)</th><th className="num">Price implied by band</th></tr></thead>
              <tbody>
                {integrity.offBandItems.map((i) => (
                  <tr key={i.id}>
                    <td className="mono small">{i.id}</td><td>{i.name}</td><td>{i.band}</td>
                    <td className="num">{gbp(Math.round(i.price * 100))}</td>
                    <td className="num">{gbp(Math.round(i.exPrice * 100))}</td>
                    <td className="num">{gbp(Math.round(i.expectedPrice * 100))}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}

      {error && <p className="error">{error}</p>}

      {totals && (
        <div className="stat-row">
          <div className="stat"><span className="stat-label">VAT on sales (output VAT)</span><span className="stat-value">{gbp(totals.vatPence)}</span></div>
          <div className="stat"><span className="stat-label">Net sales (ex VAT)</span><span className="stat-value">{gbp(totals.netPence)}</span></div>
          <div className="stat"><span className="stat-label">Gross sales (inc VAT)</span><span className="stat-value">{gbp(totals.grossPence)}</span></div>
        </div>
      )}

      <h3>By VAT band</h3>
      <table>
        <thead><tr><th>Band</th><th className="num">Net</th><th className="num">VAT</th><th className="num">Gross</th></tr></thead>
        <tbody>
          {byBand.map((b) => (
            <tr key={b.bp}>
              <td>{rateLabel(b.bp)}</td>
              <td className="num">{gbp(b.net)}</td>
              <td className="num">{gbp(b.vat)}</td>
              <td className="num">{gbp(b.gross)}</td>
            </tr>
          ))}
          {byBand.length === 0 && <tr><td colSpan={4} className="muted">No VAT recorded in this period.</td></tr>}
        </tbody>
      </table>
      <p className="muted small">
        Band figures come from VAT rollups (line-level, and locked once a financial period is closed).
        Rates are as recorded at the till — legacy data can show near-20% oddities like 19.81%; those
        are the source data, not a calculation error. A bookkeeping aid, not a VAT return.
      </p>
    </section>
  );
}
