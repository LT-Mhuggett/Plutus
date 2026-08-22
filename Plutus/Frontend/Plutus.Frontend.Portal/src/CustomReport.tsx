import { useEffect, useState } from "react";
import { fetchSales, gbp, type SaleRow } from "./api.ts";
import SaleDialog from "./SaleDialog.tsx";
import DataTable from "./DataTable.tsx";
import { apiDateTime, businessToday } from "./apiTime.ts";

// WP3.2 Custom report (portal, net-new): a date-range sales listing with per-sale drill-in (the
// shared SaleDialog) and a client-side CSV export — the portal equivalent of the till's Statistics
// page. Reads the v1 sales list (SalesV2); amounts are pence.

const iso = (d: Date) => d.toISOString().slice(0, 10);

/**
 * The sales-line drill-down (WP3.2).
 *
 * ⚠ `day` NARROWS IT TO ONE BUSINESS DAY on arrival — WP-DRILL, 2026-08-21. Matt: *"it needs to take
 * be to reporting filtered on the sales taken on THAT day, which is a custom report filtered on that
 * specific day."* Both charts hand a day here through `dayDrill`; opening the tab normally still
 * gets the 30-day window.
 */
export default function CustomReport({ day }: { day?: string }) {
  // ⚠ SEEDED FROM `day`, not applied afterwards. Setting the range in an effect would fire the
  // 30-day fetch first and then a second one — the operator sees a month of sales flash up and
  // collapse to one day, which reads as a bug in the filter they just used.
  const [from, setFrom] = useState(() => day ?? iso(new Date(Date.now() - 30 * 86400_000)));
  const [to, setTo] = useState(() => day ?? iso(new Date()));
  const [sales, setSales] = useState<SaleRow[] | null>(null);
  const [state, setState] = useState<"idle" | "loading" | "error">("idle");
  const [error, setError] = useState("");
  const [openSale, setOpenSale] = useState<string | null>(null);

  // ⚠ Takes the range as ARGUMENTS, defaulting to state. "Today's sales" sets both pickers and
  // loads in one click, and `setFrom`/`setTo` do not update `from`/`to` until the next render —
  // so a load() reading state here would fetch the range the operator just left.
  const load = (f: string = from, t: string = to) => {
    setState("loading"); setError("");
    fetchSales(f, t)
      .then((data) => { setSales(data); setState("idle"); })
      .catch((e) => { setError(String(e instanceof Error ? e.message : e)); setState("error"); });
  };
  useEffect(load, []); // the initial window — 30 days, or the one day drilled to

  // ⚠⚠ A SECOND DRILL WHILE ALREADY HERE MUST STILL WORK. `useState` initialisers run once, so
  // clicking another day on a chart with this tab already mounted would change nothing at all —
  // the operator clicks, the page does not move, and the feature looks broken on its second use.
  useEffect(() => {
    if (!day) return;
    setFrom(day);
    setTo(day);
    setState("loading"); setError("");
    fetchSales(day, day)
      .then((data) => { setSales(data); setState("idle"); })
      .catch((e) => { setError(String(e instanceof Error ? e.message : e)); setState("error"); });
  }, [day]);

  const totalPence = sales?.reduce((t, s) => t + s.grossPence, 0) ?? 0;
  const vatPence = sales?.reduce((t, s) => t + s.vatPence, 0) ?? 0;
  const exTaxPence = totalPence - vatPence;

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

  // ⚠ `businessToday`, NOT `iso(new Date())` — that is the UTC day, and Britain is an hour ahead of
  // it all summer, so between midnight and 01:00 BST it answers YESTERDAY. An empty "today" while a
  // late shop is still cashing up reads as lost takings.
  const loadToday = () => {
    const day = businessToday();
    setFrom(day); setTo(day);
    load(day, day);
  };

  return (
    <section className="panel">
      <div className="toolbar">
        {/* ⚠ BEFORE THE PICKERS, because it REPLACES them for the commonest question a shop asks.
            Matt, 2026-08-22: *"a button before the data pickets that says Today's sales ... Just to
            make things easier"*. ⚠ It loads as well as setting the dates — setting two pickers and
            leaving the operator to find Load is most of the clicks it was meant to save. */}
        <button className="ghost small" onClick={loadToday} disabled={state === "loading"}>
          Today&rsquo;s sales
        </button>
        <label>From <input type="date" value={from} max={to} onChange={(e) => setFrom(e.target.value)} /></label>
        <label>To <input type="date" value={to} min={from} onChange={(e) => setTo(e.target.value)} /></label>
        <button className="ghost small" onClick={() => load()} disabled={state === "loading"}>Load</button>
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

          {/* FE4.3: the standard DataTable — and because it pages, the old "latest 100 only"
              client-side cap is gone: every loaded sale is now reachable. */}
          <DataTable<SaleRow>
            columns={[
              { key: "occurredAtUtc", label: "Date", render: (s) => apiDateTime(s.occurredAtUtc) },
              { key: "id", label: "Sale id", render: (s) => <span className="mono small">{s.id.slice(0, 8)}…</span> },
              { key: "channel", label: "Channel", render: (s) => `${s.channel}${s.legacyRef ? " (migrated)" : ""}` },
              { key: "netPence", label: "Net (ex VAT)", numeric: true, sort: (s) => s.grossPence - s.vatPence, render: (s) => gbp(s.grossPence - s.vatPence) },
              { key: "vatPence", label: "VAT", numeric: true, render: (s) => gbp(s.vatPence) },
              { key: "grossPence", label: "Total", numeric: true, render: (s) => gbp(s.grossPence) },
            ]}
            rows={sales} getKey={(s) => s.id} initialSortKey="occurredAtUtc" initialSortDir="desc"
            search={(s) => `${s.id} ${s.channel} ${s.legacyRef ?? ""}`}
            searchPlaceholder="Search sale id / channel…"
            rowActions={(s) => <button className="ghost small" onClick={() => setOpenSale(s.id)}>Open</button>}
            emptyText="No sales in this range."
          />
          <p className="muted small">Open a sale to see its items, payments and notes — and reprint a copy receipt.</p>
        </>
      )}

      {openSale && <SaleDialog id={openSale} onClose={() => setOpenSale(null)} />}
    </section>
  );
}
