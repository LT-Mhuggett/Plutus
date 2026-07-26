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

const today = () => new Date().toISOString().slice(0, 10);
const daysAgo = (n: number) => new Date(Date.now() - n * 86400_000).toISOString().slice(0, 10);

/** Hand-rolled SVG bar chart (plan discipline: no chart libraries). */
function BarChart({ buckets }: { buckets: { period: string; grossPence: number }[] }) {
  if (buckets.length === 0) return <p className="muted">No trade in this range.</p>;
  const w = 720, h = 200, pad = 4;
  const bw = Math.max(4, Math.floor(w / buckets.length) - pad);
  const max = Math.max(...buckets.map((b) => b.grossPence), 1);
  return (
    <svg className="chart" viewBox={`0 0 ${w} ${h + 18}`} role="img" aria-label="Gross by period">
      {buckets.map((b, i) => {
        const bh = Math.max(1, Math.round((b.grossPence / max) * h));
        return (
          <g key={b.period}>
            <rect x={i * (bw + pad)} y={h - bh} width={bw} height={bh} rx="2">
              <title>{`${b.period}: ${gbp(b.grossPence)}`}</title>
            </rect>
            {buckets.length <= 16 && (
              <text x={i * (bw + pad) + bw / 2} y={h + 13} textAnchor="middle" className="chart-label">
                {b.period.slice(-5)}
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
              <td>{l.qty} ×</td>
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
              <thead><tr><th>#</th><th>Qty</th><th className="num">Unit</th><th className="num">Disc</th><th className="num">Gross</th><th className="num">VAT</th></tr></thead>
              <tbody>
                {sale.lines.map((l) => (
                  <tr key={l.lineNo}>
                    <td>{l.lineNo}</td><td>{l.qty}</td>
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
          <select value={granularity} onChange={(e) => setGranularity(e.target.value as typeof granularity)}>
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
