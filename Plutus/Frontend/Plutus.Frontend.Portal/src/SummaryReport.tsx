import { useEffect, useMemo, useState } from "react";
import { fetchSalesSummary, gbp, type SalesSummary } from "./api.ts";
import { useNav } from "./nav.tsx";
import { dayFocus } from "./dayDrill.ts";

// WP3.1 rich company Summary — the till's SummaryReport ported to the portal (feature-match, portal
// styling): period select, inc/ex-VAT toggle, previous-period deltas, a daily chart, top items and
// the payment-method split. Reads summary-rich (SalesV2, tenant-wide) in POUNDS. The Dashboard tab
// keeps the simpler rollup chart + KPI pills.

const PERIODS = [
  { label: "Last 7 days", days: 7 },
  { label: "Last 30 days", days: 30 },
  { label: "Last 90 days", days: 90 },
  { label: "Last 12 months", days: 365 },
] as const;

const P = (pounds: number) => Math.round(pounds * 100); // pounds → pence for gbp()

/** % change vs the previous equal period, with a direction glyph (never colour-alone). */
function Delta({ curr, prev }: { curr: number; prev: number }) {
  if (prev === 0) return <span className="muted small"> —</span>;
  const pct = ((curr - prev) / prev) * 100;
  const up = pct >= 0;
  return (
    <span className="small" style={{ color: up ? "#15803d" : "#b91c1c", marginLeft: 4 }}>
      {up ? "▲" : "▼"} {Math.abs(pct).toFixed(0)}%
    </span>
  );
}

export default function SummaryReport() {
  const [days, setDays] = useState(30);
  const [exVat, setExVat] = useState(false);
  const [curr, setCurr] = useState<SalesSummary | null>(null);
  const [prev, setPrev] = useState<SalesSummary | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    setError("");
    const to = new Date();
    const from = new Date();
    from.setDate(from.getDate() - days + 1);
    const prevTo = new Date(from);
    prevTo.setDate(prevTo.getDate() - 1);
    const prevFrom = new Date(prevTo);
    prevFrom.setDate(prevFrom.getDate() - days + 1);
    Promise.all([fetchSalesSummary(from, to), fetchSalesSummary(prevFrom, prevTo)])
      .then(([c, pr]) => { setCurr(c); setPrev(pr); })
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));
  }, [days]);

  const totalOf = (s: SalesSummary) => (exVat ? s.totalSalesExTax : s.totalSales);
  const dayTotal = (d: SalesSummary["byDay"][number]) => (exVat ? d.totalExTax : d.total);
  const grossOf = (i: SalesSummary["topItems"][number]) => (exVat ? i.grossExTax : i.gross);

  const avg = curr && curr.totalOrders > 0 ? totalOf(curr) / curr.totalOrders : 0;
  const prevAvg = prev && prev.totalOrders > 0 ? totalOf(prev) / prev.totalOrders : 0;
  const grossSum = curr?.topItems.reduce((t, i) => t + grossOf(i), 0) ?? 0;
  const paySum = curr?.byPayMethod.reduce((t, m) => t + m.total, 0) ?? 0;

  return (
    <section className="panel">
      <div className="toolbar">
        <label>Period{" "}
          <select value={days} onChange={(e) => setDays(Number(e.target.value))}>
            {PERIODS.map((pd) => <option key={pd.days} value={pd.days}>{pd.label}</option>)}
          </select>
        </label>
        <label>VAT{" "}
          <select value={exVat ? "ex" : "inc"} onChange={(e) => setExVat(e.target.value === "ex")}>
            <option value="inc">inc VAT</option><option value="ex">ex VAT</option>
          </select>
        </label>
        <span className="muted small">vs the previous {days} days</span>
      </div>

      {error && <p className="error">{error}</p>}

      {curr && prev && (
        <>
          <div className="stat-row">
            <div className="stat">
              <span className="stat-label">Total sales ({exVat ? "ex" : "inc"} VAT) <Delta curr={totalOf(curr)} prev={totalOf(prev)} /></span>
              <span className="stat-value">{gbp(P(totalOf(curr)))}</span>
            </div>
            <div className="stat">
              <span className="stat-label">Total orders <Delta curr={curr.totalOrders} prev={prev.totalOrders} /></span>
              <span className="stat-value">{curr.totalOrders}</span>
            </div>
            <div className="stat">
              <span className="stat-label">Avg. order value <Delta curr={avg} prev={prevAvg} /></span>
              <span className="stat-value">{gbp(P(avg))}</span>
            </div>
          </div>

          <h3>Daily sales ({exVat ? "ex" : "inc"} VAT)</h3>
          <DailyBars data={curr.byDay.map((d) => ({ date: d.date, total: dayTotal(d) }))} />

          <h3>Top selling items</h3>
          <table>
            <thead><tr>
              <th>Item</th><th className="num">Qty</th><th className="num">% of total</th>
              <th className="num">Change</th><th className="num">Gross sales</th>
            </tr></thead>
            <tbody>
              {curr.topItems.map((i) => {
                const before = prev.topItems.find((x) => x.itemId === i.itemId);
                return (
                  <tr key={i.itemId}>
                    <td>{i.name}</td>
                    <td className="num">{i.quantity}</td>
                    <td className="num">{grossSum > 0 ? `${Math.round((grossOf(i) / grossSum) * 100)}%` : "—"}</td>
                    <td className="num"><Delta curr={grossOf(i)} prev={before ? grossOf(before) : 0} /></td>
                    <td className="num">{gbp(P(grossOf(i)))}</td>
                  </tr>
                );
              })}
              {curr.topItems.length === 0 && <tr><td colSpan={5} className="muted">No sales in this period.</td></tr>}
            </tbody>
          </table>

          <h3>Sales by payment method</h3>
          <table>
            <thead><tr><th>Method</th><th className="num">% of takings</th><th className="num">Change</th><th className="num">Total</th></tr></thead>
            <tbody>
              {curr.byPayMethod.map((m) => {
                const before = prev.byPayMethod.find((x) => x.method === m.method);
                return (
                  <tr key={m.method}>
                    <td>{m.method}</td>
                    <td className="num">{paySum > 0 ? `${Math.round((m.total / paySum) * 100)}%` : "—"}</td>
                    <td className="num"><Delta curr={m.total} prev={before?.total ?? 0} /></td>
                    <td className="num">{gbp(P(m.total))}</td>
                  </tr>
                );
              })}
              {curr.byPayMethod.length === 0 && <tr><td colSpan={4} className="muted">No takings in this period.</td></tr>}
            </tbody>
          </table>
        </>
      )}
    </section>
  );
}

/**
 * Daily gross as a bar chart, reusing the portal's chart classes (no chart libraries).
 *
 * ⚠⚠ EVERY DAY IN THE RANGE IS A BAR SINCE WP-ZERO (2026-08-21) — including the shut ones. Matt:
 * *"The reports still have to show ALL days, even ones where no sales were made e.g. this graph
 * jumps from the 15th to the 17th."* The padding is done SERVER-SIDE, in `summary-rich`, because
 * three surfaces read that series; this component simply gets a complete one now.
 */
function DailyBars({ data }: { data: { date: string; total: number }[] }) {
  const { go } = useNav();
  const { bars, max } = useMemo(() => {
    const max = Math.max(1, ...data.map((d) => d.total));
    return { bars: data, max };
  }, [data]);
  if (data.length === 0) return <p className="muted">No sales in this period.</p>;
  // ⚠ A FLAT CHART OF ZEROS IS NOT THE SAME AS NO CHART, and since WP-ZERO an empty period returns a
  // row per day rather than an empty array — so the "no sales" line has to be said alongside the
  // bars, not instead of them. Without it a shut fortnight looks like a rendering fault.
  const silent = data.every((d) => d.total === 0);
  const gutter = 64, top = 8, h = 200, gap = 4, w = 780;
  const bw = Math.max(2, Math.floor((w - gutter) / data.length) - gap);
  const labelEvery = Math.max(1, Math.ceil(data.length / 10));
  return (
    <>
    {silent && <p className="muted small">No sales in this period — every day below is zero.</p>}
    <svg className="chart" viewBox={`0 0 ${w} ${h + top + 24}`} role="img" aria-label="Daily sales">
      {[0, 0.25, 0.5, 0.75, 1].map((f) => {
        const y = top + h - f * h;
        return (
          <g key={f}>
            <line x1={gutter} y1={y} x2={w} y2={y} className="grid-line" />
            <text x={gutter - 6} y={y + 3} textAnchor="end" className="chart-label">£{Math.round(f * max).toLocaleString("en-GB")}</text>
          </g>
        );
      })}
      {bars.map((b, i) => {
        // ⚠⚠ A TRUE ZERO DRAWS NOTHING. `Math.max(1, …)` exists so a day of £0.30 beside a day of
        // £400 is still visible as a hairline — but applying it to an actual zero makes a shut shop
        // look like it took something, which since WP-ZERO happens on every closed day rather than
        // never. Zero is the one value allowed no pixels.
        const bh = b.total === 0 ? 0 : Math.max(1, Math.round((b.total / max) * h));
        const x = gutter + i * (bw + gap);
        return (
          <g key={b.date}>
            {/* ⚠⚠ THE BAR IS THE DRILL (WP-DRILL, 2026-08-21). Matt: *"In reporting, I need to be
                able to click on a day and it shows all the sales from that specific day."*

                ⚠ THE WHOLE COLUMN IS THE TARGET, not the drawn bar. A zero day draws NO bar since
                WP-ZERO, and a quiet day draws a hairline — so hit-testing the rectangle would make
                exactly the days somebody is investigating the ones they cannot click. The invisible
                full-height rect below carries the click; the drawn bar is decoration on top of it. */}
            <rect
              x={x} y={top} width={bw} height={h} fill="transparent"
              className="chart-hit"
              role="button" tabIndex={0}
              onClick={() => go("Reporting", dayFocus(b.date))}
              onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") go("Reporting", dayFocus(b.date)); }}
            >
              <title>{`${b.date}: ${gbp(P(b.total))} — open this day's sales`}</title>
            </rect>
            <rect x={x} y={top + h - bh} width={bw} height={bh} rx="2" pointerEvents="none" />
            {i % labelEvery === 0 && (
              <text x={x + bw / 2} y={top + h + 14} textAnchor="middle" className="chart-label">
                {/* ⚠ A BUSINESS DAY, NOT AN INSTANT — so this is parsed as LOCAL midnight on purpose
                    and must NOT go through `apiTime.ts`. `apiDate` treats a bare value as UTC, which
                    is right for every timestamp on this API and wrong for exactly this one: in a
                    negative-offset zone it would label 12 March as the 11th. */}
                {new Date(b.date + "T00:00:00").toLocaleDateString("en-GB", { day: "2-digit", month: "short" })}
              </text>
            )}
          </g>
        );
      })}
    </svg>
    </>
  );
}
