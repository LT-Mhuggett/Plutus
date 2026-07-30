import { useEffect, useMemo, useState } from "react";
import {
  csvUrl, fetchSales, fetchSummary, fetchDashboard, fetchGiftCardLiability, gbp,
  type SaleRow, type Summary, type SummaryBucket, type DashboardKpis, type GiftCardLiability,
} from "./api.ts";
import DataTable from "./DataTable.tsx";
import { useNav } from "./nav.tsx";
import SaleDialog from "./SaleDialog.tsx";

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
  // Always label, thinned to ~10 ticks — the old "only if <=20 bars" hid every label on the
  // default 30-day day view.
  const labelEvery = Math.max(1, Math.ceil(buckets.length / 10));
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
            {i % labelEvery === 0 && (
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

/** WP2.2 dashboard pills: today/this-week sales + active counts, each clickable to the relevant
 *  tab. One `/reports/dashboard` call. Shown only on the Dashboard tab (variant="dashboard"). */
function Pills() {
  const { go } = useNav();
  const [k, setK] = useState<DashboardKpis | null>(null);
  // FE7.5: gift-card liability sits next to the trading numbers because it is the one figure on this
  // page that is money OWED rather than money earned. Fetched separately (it needs
  // portal.financials.view) and simply absent for a user who can't see financials.
  const [cards, setCards] = useState<GiftCardLiability | null>(null);
  useEffect(() => {
    fetchDashboard().then(setK).catch(() => undefined);
    fetchGiftCardLiability().then(setCards).catch(() => undefined);
  }, []);
  if (!k) return null;
  const wc = k.weekStart ? new Date(k.weekStart + "T00:00:00").toLocaleDateString("en-GB", { day: "numeric", month: "short" }) : "";
  const pill = (label: string, value: string, onClick: () => void) => (
    <button className="stat" style={{ cursor: "pointer", textAlign: "left", border: "none" }} onClick={onClick} title="Open">
      <span className="stat-label">{label}</span><span className="stat-value">{value}</span>
    </button>
  );
  return (
    <div className="stat-row">
      {pill("Sales today", gbp(k.salesTodayPence), () => go("Reporting"))}
      {pill(`Sales this week${wc ? ` (w/c ${wc})` : ""}`, gbp(k.salesWeekPence), () => go("Reporting"))}
      {pill("Active users", String(k.activeUsers), () => go("Users & Roles"))}
      {pill("Active tills", String(k.activeTills), () => go("Locations", "stores"))}
      {pill("Active stores", String(k.activeStores), () => go("Locations", "stores"))}
      {pill("Active warehouses", String(k.activeWarehouses), () => go("Locations", "warehouses"))}
      {pill("Active webstores", String(k.activeWebstores), () => go("Webstore"))}
      {cards && cards.outstandingPence > 0 &&
        pill("Gift cards outstanding", gbp(cards.outstandingPence), () => go("Gift cards"))}
    </div>
  );
}

/** Company dashboard: rollup buckets with drill — coarser granularity → click a bucket to
 *  zoom in → at day level the individual sales list → click a sale for the full record.
 *  variant="dashboard" (the Dashboard tab) shows the KPI pills and hides the per-period + sales
 *  tables ("remove the table below the graph"); variant="report" keeps the full drill-down. */
export default function Dashboard({ variant = "report" }: { variant?: "report" | "dashboard" }) {
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

  return (
    <section className="panel">
      {variant === "dashboard" && <Pills />}
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

      {variant === "report" && (<>
      <DataTable<SummaryBucket>
        columns={[
          { key: "period", label: "Period", render: (b) => periodLabel(b.period) },
          { key: "grossPence", label: "Gross", numeric: true, render: (b) => gbp(b.grossPence) },
          { key: "vatPence", label: "VAT", numeric: true, render: (b) => gbp(b.vatPence) },
          { key: "txnCount", label: "Txns", numeric: true },
          { key: "avgBasketPence", label: "Avg basket", numeric: true, render: (b) => gbp(b.avgBasketPence) },
        ]}
        rows={buckets} getKey={(b) => b.period} initialSortKey="period"
        search={(b) => periodLabel(b.period)}
        searchPlaceholder="Search period…"
        rowActions={(b) => <button className="ghost small" onClick={() => drill(b.period)}>{granularity === "day" || granularity === "week" ? "Sales" : "Drill"}</button>}
        emptyText="No trade in this range."
      />

      {sales && (
        <>
          <h3>Sales</h3>
          <DataTable<SaleRow>
            columns={[
              { key: "occurredAtUtc", label: "Time", render: (s) => new Date(s.occurredAtUtc + "Z").toLocaleTimeString("en-GB") },
              { key: "channel", label: "Channel", render: (s) => `${s.channel}${s.legacyRef ? " (migrated)" : ""}` },
              { key: "grossPence", label: "Gross", numeric: true, render: (s) => gbp(s.grossPence) },
              { key: "vatPence", label: "VAT", numeric: true, render: (s) => gbp(s.vatPence) },
            ]}
            rows={sales} getKey={(s) => s.id} initialSortKey="occurredAtUtc"
            search={(s) => `${s.id} ${s.channel}`}
            searchPlaceholder="Search sale id / channel…"
            rowActions={(s) => <button className="ghost small" onClick={() => setOpenSale(s.id)}>Detail</button>}
            emptyText="No sales that day."
          />
        </>
      )}
      </>)}

      {openSale && <SaleDialog id={openSale} onClose={() => setOpenSale(null)} />}
    </section>
  );
}
