import { useEffect, useMemo, useRef, useState } from "react";
import { fetchSalesSummary, type SalesSummary } from "../api.ts";
import { gbp } from "../money.ts";

const PERIODS = [
  { label: "Last 7 days", days: 7 },
  { label: "Last 30 days", days: 30 },
  { label: "Last 90 days", days: 90 },
  { label: "Last 12 months", days: 365 },
] as const;

const p = (pounds: number) => Math.round(pounds * 100);

/** % change vs previous period, rendered with a direction glyph (not colour-alone). */
function Delta({ curr, prev }: { curr: number; prev: number }) {
  if (prev === 0) return <span className="muted small">—</span>;
  const pct = ((curr - prev) / prev) * 100;
  const up = pct >= 0;
  return (
    <span className={up ? "delta up" : "delta down"}>
      {up ? "▲" : "▼"} {Math.abs(pct).toFixed(0)}%
    </span>
  );
}

export default function SummaryReport() {
  const [days, setDays] = useState(30);
  const [exVat, setExVat] = useState(false);
  const [curr, setCurr] = useState<SalesSummary | null>(null);
  const [prev, setPrev] = useState<SalesSummary | null>(null);
  const [state, setState] = useState<"loading" | "ready" | "error">("loading");
  const [error, setError] = useState("");

  useEffect(() => {
    setState("loading");
    const to = new Date();
    const from = new Date();
    from.setDate(from.getDate() - days + 1);
    const prevTo = new Date(from);
    prevTo.setDate(prevTo.getDate() - 1);
    const prevFrom = new Date(prevTo);
    prevFrom.setDate(prevFrom.getDate() - days + 1);
    Promise.all([fetchSalesSummary(from, to), fetchSalesSummary(prevFrom, prevTo)])
      .then(([c, pr]) => {
        setCurr(c);
        setPrev(pr);
        setState("ready");
      })
      .catch((e) => {
        setError(String(e));
        setState("error");
      });
  }, [days]);

  // inc/ex VAT toggle: pick the matching field everywhere it applies
  const totalOf = (s: SalesSummary) => (exVat ? s.totalSalesExTax : s.totalSales);
  const dayTotal = (d: SalesSummary["byDay"][number]) => (exVat ? d.totalExTax : d.total);
  const grossOf = (i: SalesSummary["topItems"][number]) => (exVat ? i.grossExTax : i.gross);

  const avg = curr && curr.totalOrders > 0 ? totalOf(curr) / curr.totalOrders : 0;
  const prevAvg = prev && prev.totalOrders > 0 ? totalOf(prev) / prev.totalOrders : 0;
  const grossSum = curr?.topItems.reduce((t, i) => t + grossOf(i), 0) ?? 0;
  const paySum = curr?.byPayMethod.reduce((t, m) => t + m.total, 0) ?? 0;

  return (
    <div>
      <div className="range-row">
        <select value={days} onChange={(e) => setDays(Number(e.target.value))}>
          {PERIODS.map((pd) => (
            <option key={pd.days} value={pd.days}>
              {pd.label}
            </option>
          ))}
        </select>
        <div className="vat-toggle" role="group" aria-label="VAT display">
          <button className={exVat ? "" : "active"} onClick={() => setExVat(false)}>
            inc VAT
          </button>
          <button className={exVat ? "active" : ""} onClick={() => setExVat(true)}>
            ex VAT
          </button>
        </div>
        <span className="muted small">compared to the previous {days} days</span>
      </div>

      {state === "error" && <p className="error small">{error}</p>}
      {state === "loading" && <p className="muted">Loading…</p>}

      {state === "ready" && curr && prev && (
        <>
          <div className="stat-tiles">
            <div className="stat-tile">
              <span className="stat-value">{gbp(p(totalOf(curr)))}</span>
              <span className="stat-label">
                total sales ({exVat ? "ex" : "inc"} VAT) <Delta curr={totalOf(curr)} prev={totalOf(prev)} />
              </span>
            </div>
            <div className="stat-tile">
              <span className="stat-value">{curr.totalOrders}</span>
              <span className="stat-label">
                total orders <Delta curr={curr.totalOrders} prev={prev.totalOrders} />
              </span>
            </div>
            <div className="stat-tile">
              <span className="stat-value">{gbp(p(avg))}</span>
              <span className="stat-label">
                avg. order value <Delta curr={avg} prev={prevAvg} />
              </span>
            </div>
          </div>

          <div className="report-grid">
            <div className="report-main">
              <div className="info-card">
                <h3>Daily sales ({exVat ? "ex" : "inc"} VAT)</h3>
                <SalesChart data={curr.byDay.map((d) => ({ date: d.date, total: dayTotal(d) }))} />
              </div>

              <div className="info-card">
                <h3>Top selling items</h3>
                <table>
                  <thead>
                    <tr>
                      <th>Item</th>
                      <th className="num">Qty</th>
                      <th className="num">% of total</th>
                      <th className="num">Change</th>
                      <th className="num">Gross sales</th>
                    </tr>
                  </thead>
                  <tbody>
                    {curr.topItems.map((i) => {
                      const before = prev.topItems.find((x) => x.itemId === i.itemId);
                      return (
                        <tr key={i.itemId}>
                          <td>{i.name}</td>
                          <td className="num">{i.quantity}</td>
                          <td className="num">{grossSum > 0 ? `${Math.round((grossOf(i) / grossSum) * 100)}%` : "—"}</td>
                          <td className="num">
                            <Delta curr={grossOf(i)} prev={before ? grossOf(before) : 0} />
                          </td>
                          <td className="num">{gbp(p(grossOf(i)))}</td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            </div>

            <div className="info-card side-card">
              <h3>Sales by payment method</h3>
              {curr.byPayMethod.map((m) => {
                const before = prev.byPayMethod.find((x) => x.method === m.method);
                return (
                  <div key={m.method} className="breakdown-row">
                    <div className="breakdown-head">
                      <span className="grow">{m.method}</span>
                      <Delta curr={m.total} prev={before?.total ?? 0} />
                      <strong>{gbp(p(m.total))}</strong>
                    </div>
                    <div className="breakdown-bar">
                      <div style={{ width: paySum > 0 ? `${(m.total / paySum) * 100}%` : 0 }} />
                    </div>
                  </div>
                );
              })}
            </div>
          </div>
        </>
      )}
    </div>
  );
}

// ── single-series area chart: 2px line, soft fill, recessive grid,
//    crosshair + tooltip on hover (chart colour validated for both modes) ──
function SalesChart({ data }: { data: { date: string; total: number }[] }) {
  const [hover, setHover] = useState<number | null>(null);
  const svgRef = useRef<SVGSVGElement>(null);

  const W = 640;
  const H = 220;
  const PAD = { l: 46, r: 10, t: 10, b: 24 };

  const { points, max, ticks } = useMemo(() => {
    const max0 = Math.max(1, ...data.map((d) => d.total));
    // round the axis top up to a friendly number
    const step = Math.pow(10, Math.floor(Math.log10(max0)));
    const max = Math.ceil(max0 / step) * step;
    const points = data.map((d, i) => ({
      x: PAD.l + (data.length === 1 ? 0.5 : i / (data.length - 1)) * (W - PAD.l - PAD.r),
      y: PAD.t + (1 - d.total / max) * (H - PAD.t - PAD.b),
      ...d,
    }));
    const ticks = [0, 0.5, 1].map((f) => ({ f, v: max * f, y: PAD.t + (1 - f) * (H - PAD.t - PAD.b) }));
    return { points, max, ticks };
  }, [data]);

  if (data.length === 0) return <p className="muted small">No sales in this period.</p>;

  const line = points.map((pt, i) => `${i === 0 ? "M" : "L"}${pt.x.toFixed(1)},${pt.y.toFixed(1)}`).join(" ");
  const area = `${line} L${points[points.length - 1].x.toFixed(1)},${H - PAD.b} L${points[0].x.toFixed(1)},${H - PAD.b} Z`;
  const xLabelEvery = Math.max(1, Math.ceil(points.length / 6));
  const h = hover !== null ? points[hover] : null;

  function onMove(e: React.PointerEvent<SVGSVGElement>) {
    const rect = svgRef.current!.getBoundingClientRect();
    const x = ((e.clientX - rect.left) / rect.width) * W;
    let best = 0;
    for (let i = 1; i < points.length; i++) if (Math.abs(points[i].x - x) < Math.abs(points[best].x - x)) best = i;
    setHover(best);
  }

  return (
    <div className="chart-wrap">
      <svg
        ref={svgRef}
        viewBox={`0 0 ${W} ${H}`}
        role="img"
        aria-label={`Daily sales, ${data.length} days, peak ${max}`}
        onPointerMove={onMove}
        onPointerLeave={() => setHover(null)}
      >
        {ticks.map((t) => (
          <g key={t.f}>
            <line x1={PAD.l} x2={W - PAD.r} y1={t.y} y2={t.y} className="gridline" />
            <text x={PAD.l - 6} y={t.y + 3.5} className="axis-label" textAnchor="end">
              £{t.v >= 1000 ? `${(t.v / 1000).toFixed(t.v % 1000 === 0 ? 0 : 1)}k` : t.v}
            </text>
          </g>
        ))}
        {points.map((pt, i) =>
          i % xLabelEvery === 0 ? (
            <text key={pt.date} x={pt.x} y={H - 6} className="axis-label" textAnchor="middle">
              {new Date(pt.date).toLocaleDateString("en-GB", { day: "numeric", month: "short" })}
            </text>
          ) : null,
        )}
        <path d={area} className="chart-area" />
        <path d={line} className="chart-line" />
        {h && (
          <g>
            <line x1={h.x} x2={h.x} y1={PAD.t} y2={H - PAD.b} className="crosshair" />
            <circle cx={h.x} cy={h.y} r={4} className="chart-dot" />
          </g>
        )}
      </svg>
      {h && (
        <div className="chart-tip" style={{ left: `${(h.x / W) * 100}%` }}>
          <strong>{gbp(p(h.total))}</strong>
          <span>{new Date(h.date).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" })}</span>
        </div>
      )}
    </div>
  );
}
