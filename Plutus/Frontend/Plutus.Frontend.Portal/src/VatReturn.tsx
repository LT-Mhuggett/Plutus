import { useEffect, useMemo, useState } from "react";
import { downloadCsv, csvUrl, fetchVat, fetchVatIntegrity, gbp, type VatBucket, type VatIntegrity, type VatPartialExemption, type VatReturnTotals } from "./api.ts";
import DataTable from "./DataTable.tsx";

type OffBandItem = VatIntegrity["offBandItems"][number];

// WP2c: the VAT return, on the basis HMRC actually requires.
//
// This screen used to re-aggregate the server's buckets by their RAW `vatRateBp` and print the
// result as "By VAT band". It wasn't: a till derives each line's rate from its price pair, so one
// 20% band arrives as 1993–2004bp and the table showed six rows for one rate, each labelled with a
// number no VAT return has ever contained. The server now groups by BAND and applies the VAT
// fraction to takings (Notice 727 §3.4.1); this renders that, and shows what the tills actually
// charged next to it so the rounding gap is visible rather than folded away.

const MONTHS = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
const isoDay = (d: Date) => d.toISOString().slice(0, 10);
const rateLabel = (bp: number) => (bp === 0 ? "0%" : `${(bp / 100).toFixed(2).replace(/\.00$/, "")}%`);

/** Zero-rated and exempt are both 0% and completely different in law, so the class is shown as a
 *  chip rather than left implicit in a name. */
function ClassChip({ vatClass }: { vatClass: string }) {
  const tone: Record<string, string> = {
    Standard: "#2563eb", Reduced: "#7c3aed", Zero: "#059669", Exempt: "#d97706",
    OutsideScope: "#64748b", Unknown: "#dc2626",
  };
  return (
    <span className="small" style={{
      background: tone[vatClass] ?? "#64748b", color: "white",
      borderRadius: 4, padding: "1px 6px", marginLeft: 6, whiteSpace: "nowrap",
    }}>{vatClass === "OutsideScope" ? "Outside scope" : vatClass}</span>
  );
}

export default function VatReturn() {
  const now = new Date();
  const [view, setView] = useState<"month" | "quarter">("quarter");
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState(now.getMonth());
  const [quarter, setQuarter] = useState(Math.floor(now.getMonth() / 3) + 1);
  const [buckets, setBuckets] = useState<VatBucket[]>([]);
  const [totals, setTotals] = useState<VatReturnTotals | null>(null);
  const [pe, setPe] = useState<VatPartialExemption | null>(null);
  const [basis, setBasis] = useState("");
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
      .then((r) => { setBuckets(r.buckets); setTotals(r.totals); setBasis(r.basis); setPe(r.partialExemption); })
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));
  }, [view, year, month, quarter]);

  // Sum by BAND (the server already snapped each line's derived rate to its band; this just
  // collapses the period dimension when the range spans several).
  const byBand = useMemo(() => {
    const m = new Map<string, { key: string; name: string; cls: string; bp: number; gross: number; net: number; vat: number; charged: number; unclassified: boolean }>();
    for (const b of buckets) {
      const r = m.get(b.bandKey) ?? {
        key: b.bandKey, name: b.displayName, cls: b.vatClass, bp: b.vatRateBp,
        gross: 0, net: 0, vat: 0, charged: 0, unclassified: b.unclassified,
      };
      r.gross += b.grossPence; r.net += b.netPence; r.vat += b.vatPence; r.charged += b.vatChargedPence;
      m.set(b.bandKey, r);
    }
    return [...m.values()].sort((a, b) => (a.unclassified ? 1 : 0) - (b.unclassified ? 1 : 0) || b.gross - a.gross);
  }, [buckets]);

  const years = Array.from({ length: now.getFullYear() - 2019 + 1 }, (_, i) => 2019 + i).reverse();
  const diff = totals?.roundingDifferencePence ?? 0;

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
            // FE4.3: standard table — this is a repair worklist that can run to dozens of items,
            // so it needs sort/search/paging (the by-band totals below stay a fixed breakdown).
            <DataTable<OffBandItem>
              columns={[
                { key: "id", label: "Barcode / id", render: (i) => <span className="mono small">{i.id}</span> },
                { key: "name", label: "Name" },
                { key: "band", label: "Band" },
                { key: "price", label: "Price", numeric: true, render: (i) => gbp(Math.round(i.price * 100)) },
                { key: "exPrice", label: "Ex VAT (stored)", numeric: true, render: (i) => gbp(Math.round(i.exPrice * 100)) },
                { key: "expectedPrice", label: "Price implied by band", numeric: true, render: (i) => gbp(Math.round(i.expectedPrice * 100)) },
              ]}
              rows={integrity.offBandItems} getKey={(i) => i.id} initialSortKey="name"
              search={(i) => `${i.id} ${i.name} ${i.band}`}
              searchPlaceholder="Search barcode / name / band…"
              emptyText="No off-band items."
            />
          )}
        </div>
      )}

      {error && <p className="error">{error}</p>}

      {totals && (
        <div className="stat-row">
          <div className="stat"><span className="stat-label">VAT due on sales (Box 1)</span><span className="stat-value">{gbp(totals.vatPence)}</span></div>
          <div className="stat"><span className="stat-label">Net sales, ex VAT (Box 6)</span><span className="stat-value">{gbp(totals.netPence)}</span></div>
          <div className="stat"><span className="stat-label">Gross takings (inc VAT)</span><span className="stat-value">{gbp(totals.grossPence)}</span></div>
        </div>
      )}

      <h3>By VAT band</h3>
      <table>
        <thead><tr>
          <th>Band</th><th className="num">Gross takings</th><th className="num">Net</th>
          <th className="num">VAT due</th><th className="num">VAT charged at the till</th><th className="num">Difference</th>
        </tr></thead>
        <tbody>
          {byBand.map((b) => (
            <tr key={b.key}>
              <td>
                {b.name}
                {!b.unclassified && <span className="muted small"> · {rateLabel(b.bp)}</span>}
                <ClassChip vatClass={b.cls} />
              </td>
              <td className="num">{gbp(b.gross)}</td>
              <td className="num">{gbp(b.net)}</td>
              <td className="num">{gbp(b.vat)}</td>
              <td className="num muted">{gbp(b.charged)}</td>
              <td className="num">{b.vat - b.charged === 0 ? <span className="muted">—</span> : gbp(b.vat - b.charged)}</td>
            </tr>
          ))}
          {byBand.length === 0 && <tr><td colSpan={6} className="muted">No VAT recorded in this period.</td></tr>}
        </tbody>
      </table>

      {totals && totals.unclassifiedGrossPence !== 0 && (
        <p className="error small" style={{ padding: "6px 10px" }}>
          <strong>{gbp(totals.unclassifiedGrossPence)}</strong> of takings match none of your published VAT bands.
          They are reported on their own line above and are <strong>not</strong> folded into a real band —
          Plutus will not invent a rate for takings nothing supports. Fix the underlying items (see the
          off-band list) so this reaches zero.
        </p>
      )}

      {/* WP2c-exempt: partial exemption (Notice 706). This block is only possible because the BAND
          is recorded on each sale line — zero-rated and exempt are both 0%, so the rate could never
          carry the difference, and the difference decides what input tax you can reclaim. */}
      {pe && (
        <>
          <h3>Partial exemption</h3>
          {pe.applies ? (
            <>
              <div className="stat-row">
                <div className="stat"><span className="stat-label">Taxable supplies</span><span className="stat-value">{gbp(pe.taxableGrossPence)}</span></div>
                <div className="stat"><span className="stat-label">Exempt supplies</span><span className="stat-value">{gbp(pe.exemptGrossPence)}</span></div>
                <div className="stat">
                  <span className="stat-label">Input tax recoverable</span>
                  <span className="stat-value">{pe.recoverablePercent == null ? "—" : `${pe.recoverablePercent}%`}</span>
                </div>
              </div>
              <p className="muted small">
                You made exempt supplies in this period, so <strong>partial exemption applies</strong> and you
                cannot reclaim all your input tax. The percentage above is the standard turnover-based
                proportion — taxable supplies as a share of all supplies. A special method has to be agreed
                with HMRC. {pe.outsideScopeGrossPence !== 0 && <>Takings outside the scope of VAT
                ({gbp(pe.outsideScopeGrossPence)}) are excluded from both figures.</>}
              </p>
            </>
          ) : (
            <p className="muted small">
              <strong>Partial exemption does not apply.</strong> Every supply in this period was taxable
              (standard, reduced or zero-rated), so your input tax is recoverable in full. Zero-rated is a
              taxable supply — it charges the customer nothing but does <em>not</em> restrict recovery. Only
              genuinely <em>exempt</em> supplies would.
            </p>
          )}
          {pe.unbandedGrossPence !== 0 && (
            <p className="muted small">
              ⚠ {gbp(pe.unbandedGrossPence)} of these takings were recorded before the VAT band travelled with
              each sale line, so for those the band was worked out from the rate. That is exact for every band
              except telling zero-rated from exempt apart — so treat this split as indicative for that portion.
            </p>
          )}
          <p className="muted small">
            {pe.basis} <a href={pe.url} target="_blank" rel="noreferrer">Notice 706</a>
          </p>
        </>
      )}

      {totals && diff !== 0 && (
        <p className="muted small">
          <strong>Rounding difference {gbp(diff)}.</strong> Each till line's VAT is rounded to the penny, and
          thousands of those roundings do not add up to the rounding of the total. The <em>VAT due</em> column is
          the figure for the return — the VAT fraction applied to takings. <em>VAT charged at the till</em> is the
          sum of the lines, shown so the gap is visible rather than absorbed silently.
          {diff > 0 && " A positive difference means the tills charged slightly less than is due."}
        </p>
      )}

      <p className="muted small">
        Basis: {basis || "HMRC Notice 727 §3.4.1 — VAT fraction applied to takings at each rate."} Figures come from
        the VAT rollups and are locked once a financial period is closed. See the <strong>Rules</strong> tab for
        every rule this calculation applies and where it comes from.
      </p>
    </section>
  );
}
