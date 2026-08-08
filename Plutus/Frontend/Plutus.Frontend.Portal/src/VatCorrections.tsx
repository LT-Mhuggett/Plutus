import { useEffect, useState } from "react";
import { fetchVatCorrections, gbp, type VatCorrections } from "./api.ts";

// WP2c — RESTATING PAST VAT RETURNS. Matt's instruction, 2026-08-08: "correct past return".
//
// Until 2026-08-08 the VAT return added up each line's penny-rounded VAT instead of applying the
// VAT fraction to takings (HMRC Notice 727 §3.4.1). THE SALES DATA WAS NEVER WRONG — only the
// arithmetic on top of it — so this screen doesn't repair anything: it re-runs both methods over
// the same rollups and shows the difference per VAT period.
//
// ⚠ It deliberately stops short of filing. It produces the number and says which HMRC correction
// route the arithmetic points at; submitting the adjustment is the business's action, and whether
// the original error was "careless" (which forces a VAT652 however small it is) is a judgement no
// software can make.

// HMRC stagger groups — which months a business's VAT quarters END in. Getting this wrong
// attributes every correction to the wrong return, so it is asked rather than assumed.
const STAGGERS = [
  { m: 3, label: "Stagger 1 — quarters end Mar / Jun / Sep / Dec" },
  { m: 1, label: "Stagger 2 — quarters end Jan / Apr / Jul / Oct" },
  { m: 2, label: "Stagger 3 — quarters end Feb / May / Aug / Nov" },
];

const day = (iso: string) => new Date(`${iso}T00:00:00Z`).toLocaleDateString("en-GB", { day: "2-digit", month: "short", year: "numeric" });

export default function VatCorrections() {
  const [basis, setBasis] = useState<"quarter" | "month">("quarter");
  const [stagger, setStagger] = useState(3);
  const [data, setData] = useState<VatCorrections | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    setError("");
    fetchVatCorrections(basis, stagger).then(setData)
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));
  }, [basis, stagger]);

  const s = data?.summary ?? null;
  const affected = (data?.periods ?? []).filter((p) => p.affected);

  return (
    <section className="panel">
      <div className="toolbar">
        <label>VAT periods{" "}
          <select value={basis} onChange={(e) => setBasis(e.target.value as "quarter" | "month")}>
            <option value="quarter">Quarterly</option><option value="month">Monthly</option>
          </select>
        </label>
        {basis === "quarter" && (
          <label>Stagger{" "}
            <select value={stagger} onChange={(e) => setStagger(Number(e.target.value))}>
              {STAGGERS.map((x) => <option key={x.m} value={x.m}>{x.label}</option>)}
            </select>
          </label>
        )}
      </div>

      {error && <p className="error">{error}</p>}

      {data && (
        <p className="muted small">{data.guidance.whatHappened}</p>
      )}

      {s && s.periodsAffected === 0 && (
        <p className="muted">
          Nothing to correct — no completed VAT period ended before {data ? day(data.filedCorrectlyFrom) : "the fix"},
          so every return was produced on the correct basis.
        </p>
      )}

      {/* ⚠ Takings no band explains have no rate to take a fraction of, so they contribute nothing
          to the net error. Say so plainly — otherwise "nothing to correct" can quietly mean
          "nothing could be classified", which reads identically and is a very different thing. */}
      {s && s.unclassifiedGrossPence !== 0 && (
        <p className="error" style={{ padding: "8px 12px" }}>
          <strong>{gbp(s.unclassifiedGrossPence)}</strong> of takings in these periods match none of your VAT
          bands, so they <strong>cannot be restated</strong> and are excluded from the figures below. Until
          they are classified, treat this correction as incomplete — check the <strong>Bands</strong> tab and
          the off-band item list on the <strong>Return</strong> tab.
        </p>
      )}

      {s && s.periodsAffected > 0 && (
        <>
          <div className="stat-row">
            <div className="stat">
              <span className="stat-label">{s.underdeclared ? "VAT under-declared" : "VAT over-declared"}</span>
              <span className="stat-value">{gbp(Math.abs(s.netErrorPence))}</span>
            </div>
            <div className="stat"><span className="stat-label">As filed</span><span className="stat-value">{gbp(s.asFiledVatPence)}</span></div>
            <div className="stat"><span className="stat-label">Should have been</span><span className="stat-value">{gbp(s.restatedVatPence)}</span></div>
            <div className="stat"><span className="stat-label">Periods affected</span><span className="stat-value">{s.periodsAffected}</span></div>
          </div>

          {/* The threshold test, shown as arithmetic rather than asserted as a verdict — the whole
              point of this screen is that the numbers can be checked by someone who has to sign
              their name to them. */}
          <div className={s.route === "vat652" ? "error" : "panel"} style={{ padding: "10px 12px", marginBottom: 12 }}>
            <p style={{ marginTop: 0 }}>
              <strong>
                {s.route === "adjust-next-return"
                  ? "This can be corrected on your next VAT return."
                  : "This is above the reporting threshold — it must be disclosed on form VAT652."}
              </strong>
            </p>
            <p className="small" style={{ marginBottom: 4 }}>
              Net error {gbp(Math.abs(s.netErrorPence))} against a threshold of {gbp(s.thresholdPence)}{" "}
              ({s.thresholdBasis} Box 6 used: {gbp(s.thresholdBoxSixPence)}.)
            </p>
            <p className="small">
              {s.route === "adjust-next-return"
                ? <>Add {gbp(Math.abs(s.netErrorPence))} to <strong>{s.underdeclared ? "Box 1" : "Box 4"}</strong> of your
                    next return, and keep a record of what the error was, which periods it covers and how you
                    corrected it.</>
                : <>Report it separately to HMRC rather than adjusting a return.</>}
            </p>
            <p className="small"><strong>⚠ {data?.guidance.caveat}</strong></p>
            {data && <p className="small"><a href={data.guidance.url} target="_blank" rel="noreferrer">{data.guidance.source}</a></p>}
          </div>

          <h3>Period by period</h3>
          <table>
            <thead><tr>
              <th>VAT period</th><th className="num">Gross takings</th><th className="num">Box 6 (net)</th>
              <th className="num">Box 1 as filed</th><th className="num">Box 1 restated</th><th className="num">Difference</th>
            </tr></thead>
            <tbody>
              {affected.map((p) => (
                <tr key={p.key}>
                  <td>{day(p.startDay)} – {day(p.endDay)}</td>
                  <td className="num">{gbp(p.grossPence)}</td>
                  <td className="num">{gbp(p.boxSixPence)}</td>
                  <td className="num muted">{gbp(p.asFiledVatPence)}</td>
                  <td className="num">{gbp(p.restatedVatPence)}</td>
                  <td className="num">{p.netErrorPence === 0 ? <span className="muted">—</span> : gbp(p.netErrorPence)}</td>
                </tr>
              ))}
              <tr>
                <td><strong>Total</strong></td>
                <td className="num" /><td className="num" />
                <td className="num muted">{gbp(s.asFiledVatPence)}</td>
                <td className="num"><strong>{gbp(s.restatedVatPence)}</strong></td>
                <td className="num"><strong>{gbp(s.netErrorPence)}</strong></td>
              </tr>
            </tbody>
          </table>

          <p className="muted small">{data?.guidance.whatToDo}</p>
          <p className="muted small">
            A positive difference means the return understated the VAT due and the shortfall is owed to HMRC.
            Periods ending on or after {data ? day(data.filedCorrectlyFrom) : "the fix date"} were already
            produced on the correct basis and are excluded. Basis: {data?.returnBasis}
          </p>
        </>
      )}
    </section>
  );
}
