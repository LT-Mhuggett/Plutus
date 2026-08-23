import { useEffect, useState } from "react";
import DialogX from "../DialogX.tsx";
import { deleteParked, fetchParked, type ParkedTransaction } from "../api.ts";
import type { BasketState } from "./basket.ts";

interface Props {
  // ⚠ The NAME travels with the basket — see TillPage. Retrieving un-parks the row, so this is
  // the only moment the name is still known.
  onLoad: (state: BasketState, name: string) => void;
  onClose: () => void;
}

export default function ParkedDialog({ onLoad, onClose }: Props) {
  const [parked, setParked] = useState<ParkedTransaction[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  const refresh = () =>
    fetchParked()
      .then(setParked)
      .catch((e) => setError(String(e)));

  useEffect(() => {
    refresh();
  }, []);

  async function load(p: ParkedTransaction) {
    setBusy(true);
    try {
      const state = JSON.parse(p.data) as BasketState;
      await deleteParked(p.id); // un-park on retrieval, like the native till
      onLoad(state, p.name ?? "");
    } catch (e) {
      setError(String(e));
      setBusy(false);
    }
  }

  async function remove(p: ParkedTransaction) {
    setBusy(true);
    try {
      await deleteParked(p.id);
      await refresh();
    } catch (e) {
      setError(String(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && !busy && onClose()}>
      <div className="dialog">
        <h2>Parked baskets</h2>
        <DialogX onClose={onClose} disabled={busy} />
        {error && <p className="error small">{error}</p>}
        {!parked && !error && <p className="muted">Loading…</p>}
        {parked && parked.length === 0 && <p className="muted">Nothing parked.</p>}
        {parked && parked.length > 0 && (
          <ul className="results">
            {parked.map((p) => (
              <li key={p.id} className="parked-row">
                <button onClick={() => load(p)} disabled={busy}>
                  <span className="grow">{p.name || "(unnamed)"}</span>
                  <span className="muted small">load</span>
                </button>
                <button className="ghost small" onClick={() => remove(p)} disabled={busy} title="Delete">
                  ✕
                </button>
              </li>
            ))}
          </ul>
        )}
        <div className="dialog-actions">
          <button className="ghost" onClick={onClose} disabled={busy}>
            Close
          </button>
        </div>
      </div>
    </div>
  );
}
