import { useEffect, useState } from "react";
import {
  addVatRateChange, cancelVatRateChange, createVatBand, fetchVatBands, setVatTaxMapping, updateVatBand,
  type VatBandAdmin, type VatRule, type VatTaxRow,
} from "./api.ts";
import { ask } from "./Ask.tsx";

// WP2c — THE PORTAL IS THE SOURCE OF VAT TRUTH.
//
// Before this screen there was no VAT surface at all: bands were seeded legacy rows, read-only in
// both frontends, and changing one meant SQL. Now a rate change is a scheduled, audited, reversible
// action here — and the tills read the result rather than holding rates of their own.
//
// Two things this UI exists to make impossible:
//   1. Editing a rate in place. A rate change ADDS a dated point; the old rate stays true for the
//      sales rung up under it. There is deliberately no "change the rate" field.
//   2. Picking a class by looking at the rate. Zero and Exempt are both 0% and different in law, so
//      the class is always an explicit choice with the consequence spelled out next to it.

const CLASS_HELP: Record<string, string> = {
  Standard: "Taxable at the standard rate. VAT on related costs is recoverable.",
  Reduced: "Taxable at the reduced rate. VAT on related costs is recoverable.",
  Zero: "A TAXABLE supply at 0% — the customer pays no VAT, and VAT on your costs IS recoverable. Books, comics, magazines and newspapers belong here.",
  Exempt: "NOT a taxable supply — the customer pays no VAT, and VAT on costs attributable to it is NOT recoverable (partial exemption). Choosing this when the supply is really zero-rated costs you money.",
  OutsideScope: "Not within the scope of UK VAT at all.",
};

const rateLabel = (bp: number) => (bp === 0 ? "0%" : `${(bp / 100).toFixed(2).replace(/\.00$/, "")}%`);
const day = (iso: string) => new Date(iso).toLocaleDateString("en-GB", { day: "2-digit", month: "short", year: "numeric" });
const isEpoch = (iso: string) => new Date(iso).getUTCFullYear() <= 1970;

export default function VatBands() {
  const [bands, setBands] = useState<VatBandAdmin[]>([]);
  const [classes, setClasses] = useState<string[]>([]);
  const [guidance, setGuidance] = useState<VatRule[]>([]);
  const [taxRows, setTaxRows] = useState<VatTaxRow[]>([]);
  const [mappingRequired, setMappingRequired] = useState(false);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [adding, setAdding] = useState(false);

  const load = () =>
    fetchVatBands()
      .then((r) => {
        setBands(r.bands); setClasses(r.classes); setGuidance(r.guidance);
        setTaxRows(r.taxRows); setMappingRequired(r.mappingRequired); setError("");
      })
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));
  useEffect(() => { void load(); }, []);

  const run = async (fn: () => Promise<unknown>) => {
    setBusy(true);
    setError("");
    try { await fn(); await load(); }
    catch (e) { setError(String(e instanceof Error ? e.message : e)); }
    finally { setBusy(false); }
  };

  const rateChangeRule = guidance.find((g) => g.id === "rate-changes-are-future-dated");

  return (
    <section className="panel">
      <div className="toolbar">
        <h3 style={{ margin: 0 }}>VAT bands</h3>
        <span className="grow" />
        <button className="primary btn" disabled={busy} onClick={() => setAdding(true)}>Add a band</button>
      </div>

      <p className="muted small">
        These bands are what your tills apply. They are published to every till — a till never decides a VAT
        rule of its own — so a change here reaches the shop floor within a minute, and a change dated in the
        future applies itself on the day <strong>even on a till that is offline in between</strong>.
      </p>

      {error && <p className="error">{error}</p>}

      {bands.map((b) => (
        <BandCard key={b.key} band={b} classes={classes} busy={busy} run={run} />
      ))}
      {bands.length === 0 && !error && <p className="muted">No VAT bands yet.</p>}

      <TaxMapping rows={taxRows} bands={bands} mappingRequired={mappingRequired} busy={busy}
        onSet={(taxId, band) => run(() => setVatTaxMapping(taxId, band))} />

      {rateChangeRule && (
        <p className="muted small">
          {rateChangeRule.whatPlutusDoes}{" "}
          <a href={rateChangeRule.url} target="_blank" rel="noreferrer">{rateChangeRule.source}</a>
        </p>
      )}

      {adding && (
        <AddBand classes={classes} busy={busy} onCancel={() => setAdding(false)}
          onSave={async (key, name, cls, bp) => { await run(() => createVatBand(key, name, cls, bp)); setAdding(false); }} />
      )}
    </section>
  );
}

/**
 * WP2c-exempt — WHICH BAND DO MY ITEMS BELONG TO?
 *
 * Items are priced against legacy tax rows carrying a name and a rate, so the band could only ever
 * be inferred from that rate. That inference is complete and correct until a business has TWO bands
 * at the same rate — which in practice means "sells zero-rated AND exempt goods". Then the rate
 * cannot tell them apart, and the difference is real money: exempt supplies block recovery of input
 * tax attributable to them, zero-rated supplies don't (HMRC Notice 706).
 *
 * So this section stays quiet until it matters, and becomes a required decision when it does.
 */
function TaxMapping({ rows, bands, mappingRequired, busy, onSet }: {
  rows: VatTaxRow[]; bands: VatBandAdmin[]; mappingRequired: boolean; busy: boolean;
  onSet: (legacyTaxId: number, band: string) => Promise<void>;
}) {
  const [open, setOpen] = useState(false);
  if (rows.length === 0) return null;

  const undecided = rows.filter((r) => r.band == null);
  const bandName = (key: string | null) => bands.find((b) => b.key === key)?.displayName ?? key;
  // Only bands at the same rate are offerable: mapping a tax row to a band at a different rate would
  // make every item on it off-band. The server enforces this too.
  const optionsFor = (r: VatTaxRow) => bands.filter((b) => b.currentRateBp != null && Math.abs(b.currentRateBp - r.rateBp) <= 25);

  return (
    <div className="panel" style={{ padding: "10px 12px", marginBottom: 12 }}>
      <div className="toolbar" style={{ marginBottom: 4 }}>
        <strong>Which band your items use</strong>
        <span className="grow" />
        <button className="linklike small" onClick={() => setOpen((v) => !v)}>{open ? "hide" : "show"}</button>
      </div>

      {undecided.length > 0 ? (
        <p className="error" style={{ padding: "8px 12px" }}>
          <strong>⚠ {undecided.length} price group{undecided.length === 1 ? "" : "s"} need a decision.</strong>{" "}
          You have more than one band at the same rate — <strong>zero-rated and exempt both charge 0%</strong> —
          so Plutus cannot tell from the price which one your items are. Until you say, their takings are reported
          as <em>unclassified</em> and your partial-exemption figure will be incomplete. Nothing is guessed.
        </p>
      ) : mappingRequired ? (
        <p className="muted small">
          You have two bands at the same rate, so each price group has been assigned to one explicitly.
          Zero-rated and exempt both charge nothing, so this is the only thing that distinguishes them.
        </p>
      ) : (
        <p className="muted small">
          Each price group is identified by its rate, which is unambiguous for your bands — nothing to decide.
          You would only need to choose here if you had two bands at the same rate (a zero-rated band and an
          exempt band, for instance).
        </p>
      )}

      {(open || undecided.length > 0) && (
        <table>
          <thead><tr><th>Price group</th><th>Rate</th><th className="num">Items</th><th>VAT band</th></tr></thead>
          <tbody>
            {rows.map((r) => {
              const opts = optionsFor(r);
              return (
                <tr key={r.legacyTaxId} style={r.band == null ? { background: "rgba(220,38,38,0.08)" } : undefined}>
                  <td>{r.name} <span className="mono small muted">#{r.legacyTaxId}</span></td>
                  <td>{rateLabel(r.rateBp)}</td>
                  <td className="num">{r.itemCount.toLocaleString("en-GB")}</td>
                  <td>
                    {/* One option means the rate already decides it — show it, don't ask. */}
                    {opts.length <= 1 ? (
                      <>
                        {bandName(r.band) ?? <span className="error">no matching band</span>}
                        {r.band != null && !r.mappedExplicitly && <span className="muted small"> (from the rate)</span>}
                      </>
                    ) : (
                      <select value={r.band ?? ""} disabled={busy}
                        onChange={(e) => { if (e.target.value) void onSet(r.legacyTaxId, e.target.value); }}>
                        <option value="">— choose —</option>
                        {opts.map((b) => <option key={b.key} value={b.key}>{b.displayName} ({b.vatClass})</option>)}
                      </select>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}

      <p className="muted small">
        Changing a band here affects sales rung up <strong>from now on</strong>. Sales already recorded keep the
        band they were sold under — rewriting them would restate VAT returns you have already filed.
      </p>
    </div>
  );
}

function BandCard({ band, classes, busy, run }: {
  band: VatBandAdmin; classes: string[]; busy: boolean;
  run: (fn: () => Promise<unknown>) => Promise<void>;
}) {
  const [editing, setEditing] = useState(false);
  const [scheduling, setScheduling] = useState(false);
  const [name, setName] = useState(band.displayName);
  const [cls, setCls] = useState(band.vatClass);

  const pending = band.points.filter((p) => p.pending);

  // Reclassifying is the expensive edit, not the cosmetic one — moving a band between Zero and
  // Exempt silently changes whether input tax on everything in it is recoverable. Confirm it, and
  // say which direction the money goes.
  const saveIdentity = async () => {
    if (cls !== band.vatClass) {
      const toExempt = cls === "Exempt";
      const ok = await ask.confirm({
        title: `Change "${band.displayName}" from ${band.vatClass} to ${cls}?`,
        danger: toExempt,
        body: (
          <>
            <p>{CLASS_HELP[cls]}</p>
            {toExempt && (
              <p><strong>Exempt blocks input-tax recovery.</strong> If these goods are actually zero-rated
                (books, comics, magazines and newspapers are), this will stop you reclaiming VAT you are
                entitled to. Both charge the customer nothing, so nothing on a receipt will look different.</p>
            )}
            {band.vatClass === "Exempt" && !toExempt && (
              <p>This restores input-tax recovery on everything in this band. No output tax changes —
                exempt and zero-rated both charge the customer nothing.</p>
            )}
          </>
        ),
        confirmLabel: "Change the class",
      });
      if (!ok) return;
    }
    await run(() => updateVatBand(band.key, name.trim(), cls));
    setEditing(false);
  };

  return (
    <div className="panel" style={{ marginBottom: 12, padding: "10px 12px" }}>
      <div className="toolbar" style={{ marginBottom: 4 }}>
        {editing ? (
          <>
            <label>Name{" "}
              <input value={name} maxLength={60} onChange={(e) => setName(e.target.value)} />
            </label>
            <label>Class{" "}
              <select value={cls} onChange={(e) => setCls(e.target.value)}>
                {classes.map((c) => <option key={c} value={c}>{c === "OutsideScope" ? "Outside scope" : c}</option>)}
              </select>
            </label>
            <span className="grow" />
            <button className="primary btn" disabled={busy || !name.trim()} onClick={() => void saveIdentity()}>Save</button>
            <button className="ghost btn" disabled={busy} onClick={() => { setName(band.displayName); setCls(band.vatClass); setEditing(false); }}>Cancel</button>
          </>
        ) : (
          <>
            <strong>{band.displayName}</strong>
            <span className="mono small muted">{band.key}</span>
            <span className="small">{band.currentRateBp == null ? "not yet in force" : rateLabel(band.currentRateBp)}</span>
            <span className="small" style={{ background: "#64748b", color: "white", borderRadius: 4, padding: "1px 6px" }}>
              {band.vatClass === "OutsideScope" ? "Outside scope" : band.vatClass}
            </span>
            <span className="grow" />
            <button className="ghost btn" disabled={busy} onClick={() => setEditing(true)}>Rename / reclassify</button>
            <button className="ghost btn" disabled={busy} onClick={() => setScheduling(true)}>Schedule a rate change</button>
          </>
        )}
      </div>

      {editing && <p className="muted small">{CLASS_HELP[cls]}</p>}

      <table>
        <thead><tr><th>Rate</th><th>From</th><th>Note</th><th /></tr></thead>
        <tbody>
          {band.points.map((p) => (
            <tr key={p.id} style={p.pending ? { opacity: 0.75 } : undefined}>
              <td>{rateLabel(p.rateBp)}</td>
              <td>
                {isEpoch(p.effectiveFromUtc) ? <span className="muted">from the start</span> : day(p.effectiveFromUtc)}
                {p.inForce && <span className="small" style={{ color: "#059669" }}> · in force</span>}
                {p.pending && <span className="small" style={{ color: "#d97706" }}> · scheduled</span>}
              </td>
              <td className="muted small">{p.note ?? ""}</td>
              <td className="num">
                {/* Only a change that hasn't taken effect can be cancelled: once tills have charged
                    it, sales exist that only it explains. */}
                {p.pending && (
                  <button className="linklike small" disabled={busy}
                    onClick={() => void run(() => cancelVatRateChange(band.key, p.id))}>cancel</button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      {pending.length > 0 && (
        <p className="muted small">
          ⚠ Every item priced against this band will be charged the new rate from that date. Item prices are
          VAT-inclusive, so unless you also reprice, the shelf price stays the same and the split between net
          and VAT moves.
        </p>
      )}

      {scheduling && (
        <ScheduleChange band={band} busy={busy} onCancel={() => setScheduling(false)}
          onSave={async (bp, from, note) => { await run(() => addVatRateChange(band.key, bp, from, note)); setScheduling(false); }} />
      )}
    </div>
  );
}

function ScheduleChange({ band, busy, onCancel, onSave }: {
  band: VatBandAdmin; busy: boolean; onCancel: () => void;
  onSave: (rateBp: number, effectiveFromUtc: string, note: string) => Promise<void>;
}) {
  const tomorrow = new Date(Date.now() + 86_400_000).toISOString().slice(0, 10);
  const [pct, setPct] = useState(band.currentRateBp == null ? "" : String(band.currentRateBp / 100));
  const [from, setFrom] = useState(tomorrow);
  const [note, setNote] = useState("");

  const bp = Math.round(Number(pct) * 100);
  const valid = pct !== "" && Number.isFinite(bp) && bp >= 0 && bp <= 10000 && from >= tomorrow;

  return (
    <div className="panel" style={{ padding: "10px 12px", marginTop: 8 }}>
      <div className="toolbar">
        <label>New rate{" "}
          <input type="number" step="0.01" min="0" max="100" value={pct} style={{ width: 90 }}
            onChange={(e) => setPct(e.target.value)} /> %
        </label>
        <label>From{" "}
          {/* The server refuses a past date outright; min= keeps the picker honest too. */}
          <input type="date" value={from} min={tomorrow} onChange={(e) => setFrom(e.target.value)} />
        </label>
        <label>Note{" "}
          <input value={note} maxLength={200} placeholder="e.g. Budget 2026, standard rate cut"
            onChange={(e) => setNote(e.target.value)} />
        </label>
        <span className="grow" />
        <button className="primary btn" disabled={busy || !valid}
          onClick={() => void onSave(bp, new Date(`${from}T00:00:00Z`).toISOString(), note)}>Schedule</button>
        <button className="ghost btn" disabled={busy} onClick={onCancel}>Cancel</button>
      </div>
      <p className="muted small">
        A rate change is <strong>scheduled, never back-dated</strong>. The current rate stays true for every
        sale already rung up under it. If a rate was wrong historically, that is an error to correct on the
        VAT return (see the Corrections tab), not a change to this timeline.
      </p>
    </div>
  );
}

function AddBand({ classes, busy, onCancel, onSave }: {
  classes: string[]; busy: boolean; onCancel: () => void;
  onSave: (key: string, displayName: string, cls: string, rateBp: number) => Promise<void>;
}) {
  const [key, setKey] = useState("");
  const [name, setName] = useState("");
  const [cls, setCls] = useState("Standard");
  const [pct, setPct] = useState("20");

  // The one rule that IS derivable from the class: Zero/Exempt/Outside scope charge nothing, so
  // their rate is 0 and the field is not the operator's to get wrong. (The server enforces it too.)
  const zeroOnly = cls === "Zero" || cls === "Exempt" || cls === "OutsideScope";
  const bp = zeroOnly ? 0 : Math.round(Number(pct) * 100);
  const valid = /^[a-z0-9_-]+$/i.test(key.trim()) && name.trim() !== "" && bp >= 0 && bp <= 10000 && (zeroOnly || bp > 0);

  return (
    <div className="panel" style={{ padding: "10px 12px" }}>
      <div className="toolbar">
        <label>Key{" "}
          <input value={key} maxLength={40} placeholder="standard" style={{ width: 120 }}
            onChange={(e) => setKey(e.target.value)} />
        </label>
        <label>Name{" "}
          <input value={name} maxLength={60} placeholder="20%" onChange={(e) => setName(e.target.value)} />
        </label>
        <label>Class{" "}
          <select value={cls} onChange={(e) => setCls(e.target.value)}>
            {classes.map((c) => <option key={c} value={c}>{c === "OutsideScope" ? "Outside scope" : c}</option>)}
          </select>
        </label>
        <label>Rate{" "}
          <input type="number" step="0.01" min="0" max="100" value={zeroOnly ? "0" : pct} disabled={zeroOnly}
            style={{ width: 90 }} onChange={(e) => setPct(e.target.value)} /> %
        </label>
        <span className="grow" />
        <button className="primary btn" disabled={busy || !valid}
          onClick={() => void onSave(key.trim().toLowerCase(), name.trim(), cls, bp)}>Create</button>
        <button className="ghost btn" disabled={busy} onClick={onCancel}>Cancel</button>
      </div>
      <p className="muted small">
        The <strong>key</strong> is the band's permanent identity — it survives every rate change and is what
        the tills match on, so it can never be renamed. The <strong>name</strong> is what people read and can
        be changed at any time. {CLASS_HELP[cls]}
      </p>
    </div>
  );
}
