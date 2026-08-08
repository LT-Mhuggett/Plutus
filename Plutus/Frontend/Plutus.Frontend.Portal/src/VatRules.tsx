import { useEffect, useState } from "react";
import { fetchVatBands, type VatBandAdmin, type VatRule } from "./api.ts";

// WP2c — "which VAT rules is this system actually using, and where do they come from?"
//
// ⚠ THIS PAGE IS NOT HAND-WRITTEN PROSE. Every rule below is served from `VatGuidance.Rules` in
// the backend, which sits next to the code that implements it. A rule that drifts away from its
// implementation is worse than no rule at all — it is a document that will be believed. Keeping
// the text in the same repo, and the same commit, as the behaviour is what stops that.
//
// Each rule carries WHERE in Plutus it happens, so the claim is checkable rather than reassuring.

const rateLabel = (bp: number) => (bp === 0 ? "0%" : `${(bp / 100).toFixed(2).replace(/\.00$/, "")}%`);

export default function VatRules() {
  const [rules, setRules] = useState<VatRule[]>([]);
  const [bands, setBands] = useState<VatBandAdmin[]>([]);
  const [error, setError] = useState("");

  useEffect(() => {
    fetchVatBands()
      .then((r) => { setRules(r.guidance); setBands(r.bands); })
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));
  }, []);

  return (
    <section className="panel">
      <h3 style={{ marginTop: 0 }}>How Plutus handles VAT</h3>
      <p className="muted small">
        Every rule below is applied by the software, and each one names the HMRC guidance it comes from and the
        part of Plutus that implements it. This is a description of what the system does — it is not tax advice,
        and choices that belong to you and your accountant (which retail scheme you use, whether a past error is
        corrected on the next return or disclosed separately) are flagged as such.
      </p>

      {error && <p className="error">{error}</p>}

      {/* What is actually in force right now, so the rules aren't read in the abstract. */}
      {bands.length > 0 && (
        <>
          <h4>Your bands today</h4>
          <table>
            <thead><tr><th>Band</th><th>Rate</th><th>Class</th><th>What that means</th></tr></thead>
            <tbody>
              {bands.map((b) => (
                <tr key={b.key}>
                  <td>{b.displayName} <span className="mono small muted">{b.key}</span></td>
                  <td>{b.currentRateBp == null ? <span className="muted">not yet in force</span> : rateLabel(b.currentRateBp)}</td>
                  <td>{b.vatClass === "OutsideScope" ? "Outside scope" : b.vatClass}</td>
                  <td className="small muted">
                    {b.vatClass === "Zero" && "Taxable at 0%. You charge no VAT and can still reclaim VAT on related costs."}
                    {b.vatClass === "Exempt" && "Not a taxable supply. You charge no VAT and CANNOT reclaim VAT on attributable costs."}
                    {b.vatClass === "Standard" && "Taxable at the standard rate; input tax recoverable."}
                    {b.vatClass === "Reduced" && "Taxable at the reduced rate; input tax recoverable."}
                    {b.vatClass === "OutsideScope" && "Outside the scope of UK VAT."}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <p className="muted small">Change these on the <strong>Bands</strong> tab. Current UK rates:{" "}
            <a href="https://www.gov.uk/vat-rates" target="_blank" rel="noreferrer">gov.uk/vat-rates</a>.</p>
        </>
      )}

      <h4>The rules this system applies</h4>
      {rules.map((r) => (
        <div key={r.id} id={`vat-rule-${r.id}`} className="panel" style={{ padding: "10px 12px", marginBottom: 10 }}>
          <strong>{r.title}</strong>
          <p style={{ marginBottom: 6 }}>{r.whatPlutusDoes}</p>
          <p className="muted small" style={{ marginBottom: 2 }}>
            <strong>Where:</strong> <span className="mono">{r.where}</span>
          </p>
          <p className="muted small" style={{ margin: 0 }}>
            <strong>Source:</strong> {r.source} — <a href={r.url} target="_blank" rel="noreferrer">read it on GOV.UK</a>
          </p>
        </div>
      ))}
      {rules.length === 0 && !error && <p className="muted">Loading…</p>}

      <p className="muted small">
        Plutus is not a tax adviser and does not file your returns. It produces the figures, shows the basis it
        used, and reports anything it cannot classify rather than making an assumption. If a figure here does
        not match your accountant's expectation, the <strong>Return</strong> tab shows what the tills charged
        alongside what is due, which is usually where the difference is.
      </p>
    </section>
  );
}
