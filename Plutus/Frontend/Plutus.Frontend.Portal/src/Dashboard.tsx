import { useEffect, useMemo, useState } from "react";
import {
  csvUrl, fetchSaleDetail, fetchSales, fetchSummary, gbp,
  type SaleDetail, type SaleRow, type Summary,
} from "./api.ts";
import Barcode39 from "./Barcode39.tsx";
import { SortTh, useSort } from "./sortable.tsx";

/** Adds the weekday to a day period (2026-07-18 → "2026-07-18 · Sat") and the month name to a
 *  month period; year unchanged. */
function periodLabel(period: string): string {
  if (/^\d{4}-\d{2}-\d{2}$/.test(period)) {
    const d = new Date(period + "T00:00:00");
    return `${period} · ${d.toLocaleDateString("en-GB", { weekday: "short" })}`;
  }
  if (/^\d{4}-\d{2}$/.test(period)) {
    const [y, m] = period.split("-").map(Number);
    return `${period} · ${new Date(y, m - 1, 1).toLocaleDateString("en-GB", { month: "short" })}`;
  }
  return period;
}

const iso = (d: Date) => d.toISOString().slice(0, 10);
const today = () => iso(new Date());
const daysAgo = (n: number) => iso(new Date(Date.now() - n * 86400_000));

/** A sensible default span per granularity, so "Month" shows a year of months, "Week" a run of
 *  weeks, "Year" all trading years — rather than only what the current 30-day range touches. */
function rangeFor(g: "day" | "week" | "month" | "year"): { from: string; to: string } {
  const to = today();
  const n = new Date();
  if (g === "day") return { from: daysAgo(30), to };
  if (g === "week") return { from: daysAgo(7 * 12), to };            // last 12 weeks
  if (g === "month") return { from: iso(new Date(n.getFullYear(), n.getMonth() - 11, 1)), to }; // last 12 months
  return { from: iso(new Date(n.getFullYear() - 7, 0, 1)), to };     // all trading years
}

/** Short x-axis label per period type (18 Jul / Jul 26 / 2026). */
function chartLabel(period: string): string {
  if (/^\d{4}-\d{2}-\d{2}$/.test(period))
    return new Date(period + "T00:00:00").toLocaleDateString("en-GB", { day: "2-digit", month: "short" });
  if (/^\d{4}-\d{2}$/.test(period)) {
    const [y, m] = period.split("-").map(Number);
    return new Date(y, m - 1, 1).toLocaleDateString("en-GB", { month: "short", year: "2-digit" });
  }
  return period;
}

/** Hand-rolled SVG bar chart with a currency Y-axis (plan discipline: no chart libraries). */
function BarChart({ buckets }: { buckets: { period: string; grossPence: number }[] }) {
  if (buckets.length === 0) return <p className="muted">No trade in this range.</p>;
  const gutter = 64, top = 8, h = 200, gap = 6, w = 780;
  const plotW = w - gutter;
  const bw = Math.max(3, Math.floor(plotW / buckets.length) - gap);
  const max = Math.max(...buckets.map((b) => b.grossPence), 1);
  const showLabels = buckets.length <= 20;
  return (
    <svg className="chart" viewBox={`0 0 ${w} ${h + top + 24}`} role="img" aria-label="Gross by period">
      {[0, 0.25, 0.5, 0.75, 1].map((f) => {
        const y = top + h - f * h;
        return (
          <g key={f}>
            <line x1={gutter} y1={y} x2={w} y2={y} className="grid-line" />
            <text x={gutter - 6} y={y + 3} textAnchor="end" className="chart-label">£{Math.round((f * max) / 100).toLocaleString("en-GB")}</text>
          </g>
        );
      })}
      {buckets.map((b, i) => {
        const bh = Math.max(1, Math.round((b.grossPence / max) * h));
        const x = gutter + i * (bw + gap);
        return (
          <g key={b.period}>
            <rect x={x} y={top + h - bh} width={bw} height={bh} rx="2">
              <title>{`${b.period}: ${gbp(b.grossPence)}`}</title>
            </rect>
            {showLabels && (
              <text x={x + bw / 2} y={top + h + 14} textAnchor="middle" className="chart-label">
                {chartLabel(b.period)}
              </text>
            )}
          </g>
        );
      })}
    </svg>
  );
}

/** WP11.2: a past sale rendered as a printable receipt with its scannable barcode. */
function ReceiptView({ id, sale }: { id: string; sale: SaleDetail }) {
  return (
    <div className="receipt-view" id="receipt-print">
      <p className="centre small">{new Date(sale.occurredAtUtc + "Z").toLocaleString("en-GB")}</p>
      <hr />
      <table className="receipt-lines">
        <tbody>
          {sale.lines.map((l) => (
            <tr key={l.lineNo}>
              <td>{l.qty} × {l.itemName ?? ""}</td>
              <td className="num">{gbp(l.unitPricePence)}</td>
              <td className="num">{l.discountPence ? `−${gbp(l.discountPence)}` : ""}</td>
              <td className="num">{gbp(l.lineGrossPence)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <hr />
      <p className="r-total"><span>Total</span><span>{gbp(sale.grossPence)}</span></p>
      <p className="small"><span>VAT</span> <span>{gbp(sale.vatPence)}</span></p>
      <hr />
      <p className="centre small">{sale.tenders.map((t) => `${t.tenderType} ${gbp(t.amountPence)}`).join(" · ")}</p>
      <div className="centre"><Barcode39 value={id} /></div>
      <p className="centre mono tiny">{id}</p>
    </div>
  );
}

function SaleDialog({ id, onClose }: { id: string; onClose: () => void }) {
  const [sale, setSale] = useState<SaleDetail | null>(null);
  const [error, setError] = useState("");
  const [asReceipt, setAsReceipt] = useState(false);
  useEffect(() => {
    fetchSaleDetail(id).then(setSale).catch((e) => setError(String(e)));
  }, [id]);
  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog">
        <div className="r-row">
          <h3 className="grow">Sale {id.slice(0, 8)}…</h3>
          {sale && <button className="ghost small" onClick={() => setAsReceipt((v) => !v)}>{asReceipt ? "Details" : "View receipt"}</button>}
        </div>
        {error && <p className="error small">{error}</p>}
        {sale && asReceipt && <ReceiptView id={id} sale={sale} />}
        {sale && !asReceipt && (
          <>
            <dl className="kv">
              <dt>When</dt><dd>{new Date(sale.occurredAtUtc + "Z").toLocaleString("en-GB")} (day {sale.businessDay})</dd>
              <dt>Channel</dt><dd>{sale.channel} · seq {sale.deviceSeq}{sale.legacyRef ? ` · legacy ${sale.legacyRef}` : ""}</dd>
              <dt>Gross / VAT</dt><dd>{gbp(sale.grossPence)} / {gbp(sale.vatPence)}{sale.vatReconstructed ? " (VAT reconstructed)" : ""}</dd>
              {sale.note && (<><dt>Note</dt><dd>{sale.note}</dd></>)}
            </dl>
            <table>
              <thead><tr><th>#</th><th>Item</th><th>Qty</th><th className="num">Unit</th><th className="num">Disc</th><th className="num">Gross</th><th className="num">VAT</th></tr></thead>
              <tbody>
                {sale.lines.map((l) => (
                  <tr key={l.lineNo}>
                    <td>{l.lineNo}</td><td>{l.itemName ?? l.itemIdOne ?? "—"}</td><td>{l.qty}</td>
                    <td className="num">{gbp(l.unitPricePence)}</td>
                    <td className="num">{l.discountPence ? gbp(l.discountPence) : "—"}</td>
                    <td className="num">{gbp(l.lineGrossPence)}</td>
                    <td className="num">{gbp(l.vatAmountPence)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            <p className="small muted">
              Paid: {sale.tenders.map((t) => `${t.tenderType} ${gbp(t.amountPence)}${t.changePence ? ` (change ${gbp(t.changePence)})` : ""}`).join(", ")}
            </p>
          </>
        )}
        <div className="dialog-actions">
          {sale && asReceipt && <button className="ghost" onClick={() => window.print()}>Print / reprint</button>}
          <button className="ghost" onClick={onClose}>Close</button>
        </div>
      </div>
    </div>
  );
}

/** Company dashboard: rollup buckets with drill — coarser granularity → click a bucket to
 *  zoom in → at day level the individual sales list → click a sale for the full record. */
export default function Dashboard() {
  const [from, setFrom] = useState(daysAgo(30));
  const [to, setTo] = useState(today());
  const [granularity, setGranularity] = useState<"day" | "week" | "month" | "year">("day");
  const [summary, setSummary] = useState<Summary | null>(null);
  const [sales, setSales] = useState<SaleRow[] | null>(null);
  const [openSale, setOpenSale] = useState<string | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    setError("");
    setSales(null);
    fetchSummary(from, to, granularity).then(setSummary).catch((e) => setError(String(e)));
  }, [from, to, granularity]);

  const drill = (period: string) => {
    // year → months of it; month → its days; week/day → that day's sales
    if (granularity === "year") {
      setFrom(`${period}-01-01`); setTo(`${period}-12-31`); setGranularity("month");
    } else if (granularity === "month") {
      const [y, m] = period.split("-").map(Number);
      const last = new Date(y, m, 0).getDate();
      setFrom(`${period}-01`); setTo(`${period}-${String(last).padStart(2, "0")}`); setGranularity("day");
    } else {
      const day = granularity === "week" ? period : period; // week key is its Monday
      fetchSales(day, day).then(setSales).catch((e) => setError(String(e)));
    }
  };

  const t = summary?.totals;
  const buckets = useMemo(() => summary?.buckets ?? [], [summary]);
  const bk = useSort(buckets, "period", "asc");
  const sl = useSort(sales ?? [], "occurredAtUtc", "asc");

  return (
    <section className="panel">
      <div className="toolbar">
        <label>From <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label>
        <label>To <input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label>
        <label>
          Granularity{" "}
          <select value={granularity} onChange={(e) => {
            const g = e.target.value as typeof granularity;
            setGranularity(g);
            const r = rangeFor(g); // widen the window to suit the level (month → 12 months, etc.)
            setFrom(r.from); setTo(r.to);
          }}>
            <option value="day">Day</option><option value="week">Week</option>
            <option value="month">Month</option><option value="year">Year</option>
          </select>
        </label>
        <a className="ghost btn" href={csvUrl("summary", from, to)}>Export CSV</a>
      </div>

      {error && <p className="error">{error}</p>}

      {t && (
        <div className="stat-row">
          <div className="stat"><span className="stat-label">Gross</span><span className="stat-value">{gbp(t.grossPence)}</span></div>
          <div className="stat"><span className="stat-label">VAT</span><span className="stat-value">{gbp(t.vatPence)}</span></div>
          <div className="stat"><span className="stat-label">Transactions</span><span className="stat-value">{t.txnCount}</span></div>
          <div className="stat"><span className="stat-label">Avg basket</span><span className="stat-value">{gbp(t.avgBasketPence)}</span></div>
        </div>
      )}

      <BarChart buckets={buckets} />

      <table>
        <thead><tr>
          <SortTh label="Period" k="period" {...bk} />
          <SortTh label="Gross" k="grossPence" num {...bk} />
          <SortTh label="VAT" k="vatPence" num {...bk} />
          <SortTh label="Txns" k="txnCount" num {...bk} />
          <SortTh label="Avg basket" k="avgBasketPence" num {...bk} />
          <th />
        </tr></thead>
        <tbody>
          {bk.sorted.map((b) => (
            <tr key={b.period}>
              <td>{periodLabel(b.period)}</td>
              <td className="num">{gbp(b.grossPence)}</td>
              <td className="num">{gbp(b.vatPence)}</td>
              <td className="num">{b.txnCount}</td>
              <td className="num">{gbp(b.avgBasketPence)}</td>
              <td><button className="ghost small" onClick={() => drill(b.period)}>{granularity === "day" || granularity === "week" ? "Sales" : "Drill"}</button></td>
            </tr>
          ))}
        </tbody>
      </table>

      {sales && (
        <>
          <h3>Sales</h3>
          {sales.length === 0 && <p className="muted">No sales that day.</p>}
          <table>
            <thead><tr>
              <SortTh label="Time" k="occurredAtUtc" {...sl} />
              <SortTh label="Channel" k="channel" {...sl} />
              <SortTh label="Gross" k="grossPence" num {...sl} />
              <SortTh label="VAT" k="vatPence" num {...sl} />
              <th />
            </tr></thead>
            <tbody>
              {sl.sorted.map((s) => (
                <tr key={s.id}>
                  <td>{new Date(s.occurredAtUtc + "Z").toLocaleTimeString("en-GB")}</td>
                  <td>{s.channel}{s.legacyRef ? " (migrated)" : ""}</td>
                  <td className="num">{gbp(s.grossPence)}</td>
                  <td className="num">{gbp(s.vatPence)}</td>
                  <td><button className="ghost small" onClick={() => setOpenSale(s.id)}>Detail</button></td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}

      {openSale && <SaleDialog id={openSale} onClose={() => setOpenSale(null)} />}
    </section>
  );
}
