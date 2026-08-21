import { useEffect, useState } from "react";
import { fetchVatPeriods, setVatPeriods, type VatPeriodSettings } from "./api.ts";
import { ask } from "./Ask.tsx";
import { apiDateTime } from "./apiTime.ts";

/**
 * **Company → Financial year & VAT periods (WP-FY, 2026-08-21).**
 *
 * ⚠⚠ MATT: *"I need to be able to set the company year in the portal. And the VAT periods. This
 * then needs to be reflected in the reports, specifically the VAT reports needs to match the months
 * it reports on."*
 *
 * ⚠⚠ **UNTIL THIS EXISTED, THE VAT RETURN SCREEN COULD NOT PRODUCE SOME BUSINESSES' RETURN PERIODS
 * AT ALL.** Its quarter picker was hard-wired to CALENDAR quarters (Q1 = Jan–Mar); a business on
 * stagger 2 files November to January, and there was no way to ask for that. `vat-corrections` did
 * take a stagger — off the query string, defaulting to 1 — so the two screens could disagree about
 * which quarter a day belonged to and neither said which it had used.
 *
 * ⚠ ON THE COMPANY TAB, beside the VAT number and the card surcharge, because these are the facts
 * HMRC knows this business by — not a per-store setting. Two stores disagreeing about one VAT
 * return is not a configuration anybody should be able to express.
 */

const MONTHS = ["January", "February", "March", "April", "May", "June",
                "July", "August", "September", "October", "November", "December"];

/**
 * ⚠ HMRC'S THREE STAGGERS, BY THE MONTH A QUARTER ENDS — the same representation the server stores
 * and `FinancialCalendar` computes in. Showing the stagger NUMBER alone would make somebody look up
 * which months it means; showing the months is the whole point.
 */
const STAGGERS = [
  { endMonth: 3, label: "Stagger 1 — Mar, Jun, Sep, Dec" },
  { endMonth: 1, label: "Stagger 2 — Jan, Apr, Jul, Oct" },
  { endMonth: 2, label: "Stagger 3 — Feb, May, Aug, Nov" },
];

export default function VatPeriodsSection() {
  const [s, setS] = useState<VatPeriodSettings | null>(null);
  const [basis, setBasis] = useState<"quarter" | "month">("quarter");
  const [stagger, setStagger] = useState(3);
  const [yearMonth, setYearMonth] = useState(4);
  const [yearDay, setYearDay] = useState(1);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [denied, setDenied] = useState(false);

  const load = () =>
    fetchVatPeriods()
      .then((v) => {
        setS(v);
        setBasis(v.basis);
        setStagger(v.staggerEndMonth);
        setYearMonth(v.yearStartMonth);
        setYearDay(v.yearStartDay);
      })
      .catch(() => setDenied(true));

  useEffect(() => { void load(); }, []);

  // ⚠ HIDDEN, NOT DISABLED, when the read is refused — the same shape the other Company sections
  // use. A control that refuses everybody who can see it is worse than one that is not there.
  if (denied) return null;

  const dirty = !!s && (
    basis !== s.basis ||
    (basis === "quarter" && stagger !== s.staggerEndMonth) ||
    yearMonth !== s.yearStartMonth ||
    yearDay !== s.yearStartDay
  );

  /**
   * ⚠⚠ IT WARNS BEFORE SAVING, AND THE WARNING IS NOT A FORMALITY. Changing a stagger **re-buckets
   * every historical return**: the same takings, filed against different periods, produce different
   * numbers on different returns. Somebody reconciling last quarter against HMRC needs to know that
   * the report they printed yesterday will not reproduce.
   *
   * ⚠ Only when the PERIOD changes. Moving the financial year start does not move a VAT period, so
   * warning about it would train people to click through the warning that matters.
   */
  async function save() {
    if (!s) return;

    const periodChanged = basis !== s.basis || (basis === "quarter" && stagger !== s.staggerEndMonth);

    if (periodChanged && s.configured) {
      const ok = await ask.confirm({
        title: "Change the VAT periods?",
        body: (
          <>
            <p>
              Every VAT report is bucketed on these periods, <strong>including past ones</strong> —
              so a report you have already printed, or filed against, will not reproduce with the
              same figures.
            </p>
            <p className="muted small">The change is recorded against your name.</p>
          </>
        ),
        confirmLabel: "Change them",
      });
      if (!ok) return;
    }

    setBusy(true);
    setError("");
    setNotice("");
    try {
      await setVatPeriods({
        basis,
        // ⚠ NULL WHEN MONTHLY. A stale stagger under a monthly basis is a value that means nothing
        // and reads as if it means something — and it would come back on switching to quarterly.
        staggerEndMonth: basis === "quarter" ? stagger : null,
        yearStartMonth: yearMonth,
        yearStartDay: yearDay,
      });
      await load();
      setNotice("Saved. VAT reports will use these periods from now on.");
    } catch (e) {
      setError(String(e instanceof Error ? e.message : e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <details className="card store-card">
      <summary><strong>Financial year &amp; VAT periods</strong></summary>

      <p className="muted">
        What this business's financial year is, and the periods it files VAT on. ⚠ Every VAT report
        in the portal buckets on these — the VAT return's period picker is built from them.
      </p>

      {/* ⚠⚠ THE UNSET STATE IS SAID OUT LOUD. Until somebody chooses, the reports run on the
          HMRC-typical default, and an accountant reading a return has to know whether the period
          shown is the one this business files on or a guess made on their behalf. */}
      {s && !s.configured && (
        <p className="muted small">
          ⚠ Not set yet — reports are using the common default ({s.describe.replace(/ — .*$/, "")}).
          If that is not how this business files, change it here.
        </p>
      )}

      {s?.changedAtUtc && (
        <p className="muted small">Last changed {apiDateTime(s.changedAtUtc)}.</p>
      )}

      <div className="toolbar">
        <label>VAT periods{" "}
          <select value={basis} disabled={busy} onChange={(e) => setBasis(e.target.value as "quarter" | "month")}>
            <option value="quarter">Quarterly</option>
            <option value="month">Monthly</option>
          </select>
        </label>

        {/* ⚠ Only when quarterly — a monthly filer has no stagger, and offering one would invite a
            setting that means nothing. */}
        {basis === "quarter" && (
          <label>Quarters end{" "}
            <select value={stagger} disabled={busy} onChange={(e) => setStagger(Number(e.target.value))}>
              {STAGGERS.map((x) => <option key={x.endMonth} value={x.endMonth}>{x.label}</option>)}
            </select>
          </label>
        )}

        <label>Financial year starts{" "}
          <select value={yearMonth} disabled={busy} onChange={(e) => setYearMonth(Number(e.target.value))}>
            {MONTHS.map((m, i) => <option key={m} value={i + 1}>{m}</option>)}
          </select>
        </label>

        {/* ⚠ A DAY AS WELL AS A MONTH: a year end tied to the incorporation date is normal for a
            small company, and the UK tax year itself starts on the 6th. */}
        <label>on the{" "}
          <input
            type="number" min={1} max={31} className="short" value={yearDay} disabled={busy}
            onChange={(e) => setYearDay(Math.min(31, Math.max(1, Number(e.target.value) || 1)))}
          />
        </label>

        <button className="primary" disabled={busy || !dirty} onClick={() => void save()}>
          {busy ? "Saving…" : "Save"}
        </button>
      </div>

      {error && <p className="error">{error}</p>}
      {notice && <p className="muted small">{notice}</p>}
    </details>
  );
}
