import { useEffect, useState } from "react";
import { fetchDiscounts, type Discount } from "../api.ts";
import type { BasketLine } from "./basket.ts";

interface Props {
  lines: BasketLine[];
  onApply: (discount: Discount, keys: number[]) => void;
  onClose: () => void;
}

export default function DiscountDialog({ lines, onApply, onClose }: Props) {
  const [discounts, setDiscounts] = useState<Discount[] | null>(null);
  const [selected, setSelected] = useState<Discount | null>(null);
  const [keys, setKeys] = useState<number[]>([]);
  const [error, setError] = useState("");

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
          </>
        )}

        <div className="dialog-actions">
          <button className="ghost" onClick={selected ? () => setSelected(null) : onClose}>
            {selected ? "Back" : "Cancel"}
          </button>
          {selected && (
            <button className="primary" disabled={keys.length === 0} onClick={() => onApply(selected, keys)}>
              Apply to {keys.length} line{keys.length === 1 ? "" : "s"}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
