import { useEffect, useState } from "react";
import DialogX from "../DialogX.tsx";
import { fetchDiscounts, type Discount } from "../api.ts";
import { MAX_DISCOUNT_REASON, normaliseReason, plannedDiscountPence, type BasketLine } from "./basket.ts";
import { can, ceilingFor, POS_DISCOUNT } from "../permissions.ts";
import { cachedRoster, type TillOperator } from "../roster.ts";
import { getSession } from "../session.ts";
import { verifyAgainstRoster } from "../offlineLogin.ts";

interface Props {
  lines: BasketLine[];
  /** ⚠ `authorisedBy` is the SUPERVISOR's userId when the discount needed a step-up, else null.
   *  It lands in `LineMeta.discountAuthority[]` — see the caller. */
  onApply: (discount: Discount, keys: number[], reason: string, authorisedBy: string | null) => void;
  /** D8: a figure the operator typed, with no catalogue row behind it. ⚠ `kind` is 0 for pounds-off
   *  per unit and 1 for a fraction, matching `Discount.type`; `amount` is already in those units. */
  onApplyAdHoc: (
    kind: number, amount: number, label: string,
    keys: number[], reason: string, authorisedBy: string | null,
  ) => void;
  onClose: () => void;
}

/** ⚠ The sentinel the "type your own" row uses in the picker. It is NOT the id that reaches the
 *  basket (`AD_HOC_DISCOUNT_ID` is), only a marker so one list can hold the catalogue plus one
 *  synthetic row without a second piece of state to keep in step. */
const AD_HOC = -1;

export default function DiscountDialog({ lines, onApply, onApplyAdHoc, onClose }: Props) {
  const [discounts, setDiscounts] = useState<Discount[] | null>(null);
  const [selected, setSelected] = useState<Discount | null>(null);
  const [keys, setKeys] = useState<number[]>([]);
  const [error, setError] = useState("");

  // ── D8: the typed amount ──────────────────────────────────────────────────
  //
  // ⚠⚠ THE MAUI TILL HAS ALWAYS HAD THIS AND THIS TILL NEVER DID, which under the 2026-08-19
  // look-and-feel ruling is a gap an operator would feel the moment they swapped machines: on one
  // till a dented box can be marked down, on the other it cannot unless somebody set up a catalogue
  // discount for it first.
  //
  // ⚠ Held as TYPED text and converted once, at apply. A percent is typed as "10" because that is
  // what a person says, and becomes the fraction 0.1 — the conversion `LineDiscounts.Percentage`
  // throws above 1.0 to protect, because the legacy till multiplied a price by a raw "10".
  const [adHocKind, setAdHocKind] = useState<number>(1);
  const [adHocTyped, setAdHocTyped] = useState("");

  // ── W-P3: the ceiling, and the step-up ────────────────────────────────────
  //
  // ⚠⚠ THE WEB TILL HAD NO PERMISSION MODEL AT ALL until 2026-08-17 — a cashier could take off ANY
  // amount and only the server at ingest stood in the way. Matt, 2026-08-14: *"Base it on roles"* — a
  // discount level IS a role's `pos.discount` MaxPence.
  const [operators, setOperators] = useState<TillOperator[] | null>(null);
  /** The supervisor who authorised this one, once they have. */
  const [authorisedBy, setAuthorisedBy] = useState<string | null>(null);
  const [stepUp, setStepUp] = useState(false);
  const [supEmail, setSupEmail] = useState("");
  const [supPassword, setSupPassword] = useState("");
  const [stepUpError, setStepUpError] = useState("");

  const me = getSession()?.employeeId ?? null;

  useEffect(() => {
    // ⚠ The roster W-P2 caches. Read from the CACHE, not the network: the gate must work with the
    // line down, exactly as it does on MAUI.
    void cachedRoster().then((r) => setOperators(r?.operators ?? []));
  }, []);

  const myGrants = operators?.find((o) => (o.userId ?? "").toLowerCase() === (me ?? "").toLowerCase())?.grants ?? [];

  /**
   * The discount as it would actually be applied — the catalogue row, or the typed one with the
   * figure the operator has entered so far.
   *
   * ⚠⚠ ONE SHAPE FOR BOTH PATHS, deliberately, so the ceiling, the step-up, the planned figure and
   * the Apply button cannot treat a typed discount differently from a catalogue one. A typed £50 has
   * to hit exactly the same limit a catalogue £50 does — a second code path is how one of them ends
   * up ungated, and it would be the new one.
   */
  const adHocAmount = (() => {
    const n = Number(adHocTyped.replace(/[£%\s,]/g, ""));
    if (!Number.isFinite(n) || n <= 0) return null;
    // ⚠ A percent is typed as a NUMBER and stored as a FRACTION. Over 100% is refused here rather
    // than left to `LineDiscounts.Percentage`, which throws — an exception on the selling path with
    // a customer waiting is worse than a disabled button.
    if (adHocKind === 1) return n <= 100 ? n / 100 : null;
    return n;
  })();

  const effective: Discount | null =
    selected && selected.id === AD_HOC
      ? (adHocAmount === null ? null : { ...selected, type: adHocKind, amount: adHocAmount })
      : selected;

  const plannedPence = effective ? plannedDiscountPence(lines, effective, keys) : 0;
  const now = new Date();

  /** ⚠ Asked WITHOUT an amount — "may this operator discount at all?" is a different question from
   *  "may they discount £40", and a cashier with no grant should not reach the reason box. */
  const mayDiscountAtAll = can(myGrants, POS_DISCOUNT, now, null);
  const withinMyCeiling = can(myGrants, POS_DISCOUNT, now, plannedPence);
  const myCeiling = ceilingFor(myGrants, POS_DISCOUNT, now);

  /** ⚠⚠ A stepped-up discount is allowed once a supervisor has signed for it — and `authorisedBy`
   *  must then travel to the sale, or the record says a cashier gave it alone. */
  const allowed = withinMyCeiling || authorisedBy !== null;

  async function authorise() {
    setStepUpError("");

    const who = (operators ?? []).find(
      (o) => (o.email ?? "").trim().toLowerCase() === supEmail.trim().toLowerCase(),
    );

    if (!who) {
      setStepUpError("No account on this till matches that email.");
      return;
    }

    // ⚠⚠ SELF-APPROVAL IS REFUSED — the same rule `DiscountAudit` pins on MAUI. An operator who can
    // authorise their own over-ceiling discount has no ceiling.
    if ((who.userId ?? "").toLowerCase() === (me ?? "").toLowerCase()) {
      setStepUpError("You cannot authorise your own discount — ask somebody else.");
      return;
    }

    if (!(await verifyAgainstRoster(who, supPassword))) {
      setStepUpError("That password doesn't match.");
      return;
    }

    // ⚠ THE AUTHORISER MUST THEMSELVES PASS THE GATE. A supervisor with a £20 ceiling cannot approve
    // £50 — otherwise "step up" becomes "ask anyone at all".
    if (!can(who.grants, POS_DISCOUNT, now, plannedPence)) {
      setStepUpError(`${who.displayName} isn't allowed to authorise that much either.`);
      return;
    }

    setAuthorisedBy(who.userId);
    setStepUp(false);
    setSupPassword("");
  }
  // Binding default 22(c) — Matt, 2026-08-13: "All discounts need to be tracked — till, logged-in
  // employee and reason." The till and the employee were already on the sale header; the reason was
  // collected nowhere, on any till.
  const [reason, setReason] = useState("");

  useEffect(() => {
    fetchDiscounts().then(setDiscounts).catch((e) => setError(String(e)));
  }, []);

  const eligible = lines.filter((l) => !l.isReturn);

  function choose(d: Discount) {
    setSelected(d);
    // AllApplicable discounts default to the whole basket; others start empty
    setKeys(d.allApplicable ? eligible.map((l) => l.key) : []);
  }

  const label = (d: Discount) => (d.type === 0 ? `£${d.amount.toFixed(2)} off per item` : `${Math.round(d.amount * 100)}% off`);

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog">
        <h2>Apply discount</h2>
        <DialogX onClose={onClose} />
        {error && <p className="error small">{error}</p>}
        {!discounts && !error && <p className="muted">Loading…</p>}

        {discounts && !selected && (
          <ul className="results">
            {discounts.map((d) => (
              <li key={d.id}>
                <button onClick={() => choose(d)}>
                  <span className="grow">{d.name}</span>
                  <span className="muted small">{label(d)}</span>
                </button>
              </li>
            ))}
            {/* D8 — parity with MAUI, which has always been able to type a figure. Last in the list
                on purpose: a shop that has set up its discounts properly should reach for those, and
                this is the answer for the dented box nobody planned for. */}
            <li>
              <button onClick={() => choose({
                id: AD_HOC, name: "Type an amount", type: 1, amount: 0,
                allApplicable: false, canUseWithOtherDiscounts: false, autoApply: false,
              })}>
                <span className="grow">Type an amount…</span>
                <span className="muted small">£ or %</span>
              </button>
            </li>
          </ul>
        )}

        {selected && (
          <>
            {selected.id === AD_HOC ? (
              <div className="setting-row" style={{ gap: 6 }}>
                <select value={adHocKind} onChange={(e) => setAdHocKind(Number(e.target.value))}>
                  <option value={1}>% off</option>
                  <option value={0}>£ off each</option>
                </select>
                <input
                  className="pref-input"
                  inputMode="decimal"
                  autoFocus
                  placeholder={adHocKind === 1 ? "10" : "1.50"}
                  value={adHocTyped}
                  onChange={(e) => setAdHocTyped(e.target.value)}
                />
                {/* ⚠ Refusals are shown as they are typed rather than on submit: a disabled Apply with
                    no explanation is the dead-button fault this dialog already fixed once. */}
                {adHocTyped.trim() !== "" && adHocAmount === null && (
                  <span className="error small">
                    {adHocKind === 1 ? "Enter a percentage between 0 and 100." : "Enter an amount in pounds, e.g. 1.50."}
                  </span>
                )}
              </div>
            ) : (
              <p>
                <strong>{selected.name}</strong> — {label(selected)}
              </p>
            )}

            {/* ⚠⚠ SELECT ALL — Matt, 2026-08-20: *"the till asks me to select items, with an option to
                select all"*. It is the whole-basket case, and it goes through the SAME per-line keys
                every other discount does: there is no basket-level discount anywhere in this platform
                (`GrossPence` must equal Σ line gross), so "everything" has to mean "every eligible
                line" or it could not be sent at all. */}
            <div className="setting-row" style={{ justifyContent: "space-between" }}>
              <span className="muted small">Tick the lines it applies to:</span>
              <span>
                <button
                  type="button"
                  className="linklike small"
                  disabled={keys.length === eligible.length}
                  onClick={() => setKeys(eligible.map((l) => l.key))}
                >
                  Select all
                </button>{" "}
                <button
                  type="button"
                  className="linklike small"
                  disabled={keys.length === 0}
                  onClick={() => setKeys([])}
                >
                  Clear
                </button>
              </span>
            </div>
            <ul className="checklist">
              {eligible.map((l) => (
                <li key={l.key}>
                  <label>
                    <input
                      type="checkbox"
                      checked={keys.includes(l.key)}
                      onChange={(e) =>
                        setKeys((k) => (e.target.checked ? [...k, l.key] : k.filter((x) => x !== l.key)))
                      }
                    />
                    <span className="grow">{l.item.name}</span>
                    <span className="muted small">×{l.quantity}</span>
                  </label>
                </li>
              ))}
            </ul>

            {/* ⚠ MANDATORY, and that is the ruling rather than an oversight. An optional reason is an
                empty column: the one discount anybody ever asks about is the one where nobody typed
                anything. The Apply button below is disabled until this holds words. */}
            <label className="field">
              <span>Why is this discount being given?</span>
              <input
                type="text"
                value={reason}
                maxLength={MAX_DISCOUNT_REASON}
                placeholder="e.g. damaged box, price-match, staff purchase"
                onChange={(e) => setReason(e.target.value)}
                autoFocus
              />
            </label>

            {/* ── W-P3: over the operator's ceiling ────────────────────────────────────────────
                ⚠ Shown only when it actually bites, so an ordinary discount is unchanged. ⚠ The
                figure is `plannedDiscountPence` — the REAL engine's answer, not a second
                derivation, so what is checked is what will be applied. */}
            {keys.length > 0 && !withinMyCeiling && authorisedBy === null && (
              <div className="setting-row" style={{ flexDirection: "column", alignItems: "stretch", gap: 6 }}>
                <span className="error small">
                  That is £{(plannedPence / 100).toFixed(2)} off, which is over your limit
                  {myCeiling != null ? ` of £${(myCeiling / 100).toFixed(2)}` : ""}. A supervisor can
                  authorise it.
                </span>

                {!stepUp && (
                  <button className="ghost small" onClick={() => setStepUp(true)}>
                    Get a supervisor to authorise…
                  </button>
                )}

                {stepUp && (
                  <>
                    <input
                      className="pref-input"
                      type="email"
                      placeholder="Supervisor's email"
                      value={supEmail}
                      onChange={(e) => setSupEmail(e.target.value)}
                    />
                    {/* ⚠ Masked — a supervisor's password must not be readable over the shoulder of
                        the operator whose discount they are authorising. */}
                    <input
                      className="pref-input"
                      type="password"
                      placeholder="Supervisor's password"
                      value={supPassword}
                      onChange={(e) => setSupPassword(e.target.value)}
                    />
                    {stepUpError && <span className="error small">{stepUpError}</span>}
                    <button className="primary small" onClick={() => void authorise()}>
                      Authorise
                    </button>
                  </>
                )}
              </div>
            )}

            {authorisedBy !== null && (
              <p className="muted small">
                ✅ Authorised by a supervisor — their name goes on the sale with the reason.
              </p>
            )}
          </>
        )}

        <div className="dialog-actions">
          <button className="ghost" onClick={selected ? () => setSelected(null) : onClose}>
            {selected ? "Back" : "Cancel"}
          </button>
          {selected && (
            <button
              className="primary"
              // ⚠ `normaliseReason`, not `reason.length` — "   " passes a length check and is blank
              // to a human, which is the exact empty-column failure this field exists to prevent.
              // ⚠⚠ W-P3: `allowed` is the ceiling gate — within the operator's own limit, or signed
              // for by a supervisor. `mayDiscountAtAll` refuses an operator with no grant outright.
              // ⚠ `!effective` covers the typed path with nothing (or nonsense) typed yet.
              disabled={!effective || keys.length === 0 || !normaliseReason(reason) || !mayDiscountAtAll || !allowed}
              onClick={() => {
                if (!effective) return;
                // ⚠ A TYPED DISCOUNT TAKES A DIFFERENT DOOR because it has no catalogue row, and the
                // basket must record that: an id that looks real would be projected into legacy
                // `Transaction_Discount` and FK-fail the whole sale. Everything else about it — the
                // ceiling, the step-up, the mandatory reason — is identical.
                if (effective.id === AD_HOC) {
                  onApplyAdHoc(
                    effective.type, effective.amount,
                    effective.type === 1
                      ? `${+(effective.amount * 100).toFixed(2)}% off`
                      : `£${effective.amount.toFixed(2)} off`,
                    keys, reason, authorisedBy);
                } else {
                  onApply(effective, keys, reason, authorisedBy);
                }
              }}
            >
              Apply to {keys.length} line{keys.length === 1 ? "" : "s"}
            </button>
          )}
        </div>

        {/* ⚠ An operator with NO `pos.discount` grant is told why, rather than staring at a dead
            button. ⚠ Shown only once the roster has loaded — before that we do not know. */}
        {operators !== null && !mayDiscountAtAll && (
          <p className="error small">
            Your account isn't allowed to give discounts. A supervisor can do it, or ask for the
            permission in the portal.
          </p>
        )}
      </div>
    </div>
  );
}
