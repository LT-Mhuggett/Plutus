import { useEffect, useState } from "react";
import { downloadSalesReport, fetchSales, type Sale } from "./api.ts";
import { gbp } from "./money.ts";
import SaleDetailDialog from "./reporting/SaleDetailDialog.tsx";
import DataTable from "./DataTable.tsx";

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
          {downloading ? "Preparing…" : "Download CSV"}
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

          {/* FE4.3: standard DataTable — paging replaces the old "latest 100" client cap, so the
              whole range is browsable without exporting. */}
          <DataTable<Sale>
            columns={[
              { key: "dateOfSale", label: "Date", render: (s) => new Date(s.dateOfSale).toLocaleString("en-GB") },
              { key: "id", label: "Sale id", render: (s) => <span className="mono small">{s.id}</span> },
              { key: "totalExTax", label: "Net (ex VAT)", numeric: true, render: (s) => gbp(Math.round(s.totalExTax * 100)) },
              { key: "vat", label: "VAT", numeric: true, sort: (s) => s.total - s.totalExTax, render: (s) => gbp(Math.round((s.total - s.totalExTax) * 100)) },
              { key: "total", label: "Total", numeric: true, render: (s) => gbp(Math.round(s.total * 100)) },
            ]}
            rows={sales} getKey={(s) => s.id} initialSortKey="dateOfSale" initialSortDir="desc"
            search={(s) => s.id}
            searchPlaceholder="Search sale id…"
            rowActions={(s) => <button className="ghost small" onClick={() => setOpenSale(s.id)}>Open</button>}
            emptyText="No sales in this range."
          />
          <p className="muted small">Open a sale to see its items, payments and notes — and reprint a copy receipt.</p>
        </>
      )}
      {openSale && <SaleDetailDialog saleId={openSale} onClose={() => setOpenSale(null)} />}
    </div>
  );
}
