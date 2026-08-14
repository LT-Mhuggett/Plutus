import { useEffect, useState } from "react";
import { fetchDiscounts, type Discount } from "../api.ts";
import { MAX_DISCOUNT_REASON, normaliseReason, type BasketLine } from "./basket.ts";

interface Props {
  lines: BasketLine[];
  onApply: (discount: Discount, keys: number[], reason: string) => void;
  onClose: () => void;
}

export default function DiscountDialog({ lines, onApply, onClose }: Props) {
  const [discounts, setDiscounts] = useState<Discount[] | null>(null);
  const [selected, setSelected] = useState<Discount | null>(null);
  const [keys, setKeys] = useState<number[]>([]);
  const [error, setError] = useState("");
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
              disabled={keys.length === 0 || !normaliseReason(reason)}
              onClick={() => onApply(selected, keys, reason)}
            >
              Apply to {keys.length} line{keys.length === 1 ? "" : "s"}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
