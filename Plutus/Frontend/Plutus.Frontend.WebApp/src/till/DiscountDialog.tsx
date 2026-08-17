import { useEffect, useState } from "react";
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
  onClose: () => void;
}

export default function DiscountDialog({ lines, onApply, onClose }: Props) {
  const [discounts, setDiscounts] = useState<Discount[] | null>(null);
  const [selected, setSelected] = useState<Discount | null>(null);
  const [keys, setKeys] = useState<number[]>([]);
  const [error, setError] = useState("");

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
  const plannedPence = selected ? plannedDiscountPence(lines, selected, keys) : 0;
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
          </ul>
        )}

        {selected && (
          <>
            <p>
              <strong>{selected.name}</strong> — {label(selected)}
            </p>
            <p className="muted small">Tick the lines it applies to:</p>
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
              disabled={keys.length === 0 || !normaliseReason(reason) || !mayDiscountAtAll || !allowed}
              onClick={() => onApply(selected, keys, reason, authorisedBy)}
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
