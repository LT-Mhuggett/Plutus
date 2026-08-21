import { useEffect, useState } from "react";
import type { ItemHistoryRow } from "./api.ts";
import {
  addItemBarcode, fetchItemBarcodes, fetchItemHistory, findItemByBarcode, removeItemBarcode,
  renameItemBarcode,
} from "./api.ts";
import { barcodeProblem } from "./barcodeProblem.ts";

/**
 * An item's ADDITIONAL barcodes, on the TILL's item editor (multi-barcode, 2026-08-20).
 *
 * ⚠⚠ MATT: *"When I am trying to edit an item in the portal or on the webtill, I cannot edit or add a
 * new barcode?"* — the portal had this list and the till did not, so they now match. Under the
 * 2026-08-19 look-and-feel ruling an operator moving between the two should not have to learn that one
 * of them can do it.
 *
 * ⚠⚠ AN ALIAS IS NOT AN IDENTITY. The "Barcode / id" box above is the item's identity — it seeds the
 * deterministic item GUID, it is on every historical sale line, and it stays disabled on an edit.
 * These are additive codes that RESOLVE to the item; a scan of one rings up the item at the item's own
 * price under the item's own code.
 *
 * ⚠ THE LIVE CHECK ITSELF IS NOT HERE — it is `barcodeProblem.ts`, which the portal has a
 * byte-identical copy of and a test pins. This component is deliberately NOT identical to the
 * portal's (it gates on `canManageBarcodes()` and uses the till's class names), which is exactly why
 * the rule had to move out of it.
 */

export default function ItemBarcodeList({ itemIdOne, busy }: { itemIdOne: string; busy: boolean }) {
  const [codes, setCodes] = useState<string[] | null>(null);
  const [allCodes, setAllCodes] = useState<string[]>([]);
  const [adding, setAdding] = useState("");
  const [unlocked, setUnlocked] = useState<string | null>(null);
  const [draft, setDraft] = useState("");
  const [working, setWorking] = useState(false);
  const [error, setError] = useState("");
  const [ownedBy, setOwnedBy] = useState<string | null>(null);

  const load = () =>
    fetchItemBarcodes()
      .then((rows) => {
        setCodes(rows.filter((r) => r.itemIdOne === itemIdOne).map((r) => r.code));
        setAllCodes(rows.map((r) => r.code));
        setError("");
      })
      .catch((e) => setError(e instanceof Error ? e.message : String(e)));

  useEffect(() => { void load(); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [itemIdOne]);

  const typed = unlocked !== null ? draft : adding;
  const candidate = typed.trim();

  // ⚠ Debounced at 350ms, and only for the half the local list cannot answer — whether the code is
  // some other item's OWN barcode. A scanner types a whole code in one burst, so this fires once.
  useEffect(() => {
    setOwnedBy(null);
    if (candidate === "" || candidate === itemIdOne) return;
    const t = setTimeout(() => {
      void findItemByBarcode(candidate)
        .then((hit) => { if (hit && hit.idOne !== itemIdOne) setOwnedBy(hit.name || hit.idOne); })
        .catch(() => undefined);
    }, 350);
    return () => clearTimeout(t);
  }, [candidate, itemIdOne]);

  const mine = codes ?? [];
  const local = barcodeProblem(typed, mine, allCodes, itemIdOne, unlocked ?? undefined);
  const problem = local
    ?? (ownedBy ? { level: "error" as const, message: `That is already the barcode of "${ownedBy}".` } : null);
  const blocked = problem?.level === "error";
  const disabled = busy || working;

  const run = async (action: () => Promise<unknown>) => {
    setWorking(true);
    setError("");
    try {
      await action();
      setAdding("");
      setUnlocked(null);
      setDraft("");
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setWorking(false);
    }
  };

  const saveRename = (from: string) => run(() => renameItemBarcode(itemIdOne, from, draft.trim()));

  return (
    <div className="barcode-block">
      {codes === null ? (
        <p className="muted small">Loading barcodes…</p>
      ) : codes.length > 0 ? (
        <ul className="barcode-list">
          {codes.map((c) => (
            <li key={c}>
              {unlocked === c ? (
                <>
                  <input
                    className="pref-input grow"
                    value={draft}
                    maxLength={20}
                    autoFocus
                    disabled={disabled}
                    aria-invalid={blocked}
                    aria-label={`Correct barcode ${c}`}
                    onChange={(e) => setDraft(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === "Enter" && !blocked && draft.trim()) { e.preventDefault(); void saveRename(c); }
                      if (e.key === "Escape") { setUnlocked(null); setDraft(""); setError(""); }
                    }}
                  />
                  <button type="button" className="primary small"
                    disabled={disabled || blocked || !draft.trim()} onClick={() => void saveRename(c)}>Save</button>
                  <button type="button" className="ghost small" disabled={disabled}
                    onClick={() => { setUnlocked(null); setDraft(""); setError(""); }}>Cancel</button>
                </>
              ) : (
                <>
                  <span className="grow mono">{c}</span>
                  {/* ⚠ THE LOCK IS THE SAFETY. A barcode is the string a scanner matches on, so a stray
                      keystroke in an always-editable box is a code that silently stops scanning. */}
                  <button type="button" className="ghost small" disabled={disabled}
                    title="Unlock to correct a mistyped barcode"
                    onClick={() => { setUnlocked(c); setDraft(c); setError(""); }}>🔒 Edit</button>
                  <button type="button" className="ghost small" disabled={disabled}
                    onClick={() => void run(() => removeItemBarcode(itemIdOne, c))}>Remove</button>
                </>
              )}
            </li>
          ))}
        </ul>
      ) : (
        <p className="muted small">This item scans on its own barcode only.</p>
      )}

      <details className="barcode-add">
        <summary>Add another barcode</summary>
        <p className="muted small">
          Scanning any of these rings up this item. Useful when a supplier changes codes and you still
          have old stock on the shelf — keep both while the changeover lasts, then remove the old one.
        </p>
        <div className="setting-row">
          <input
            className="pref-input grow"
            value={adding}
            maxLength={20}
            placeholder="Scan or type a barcode"
            disabled={disabled || unlocked !== null}
            aria-invalid={blocked && unlocked === null}
            aria-label="Additional barcode"
            onChange={(e) => { setAdding(e.target.value); setError(""); }}
            onKeyDown={(e) => {
              if (e.key === "Enter" && !blocked && adding.trim()) {
                e.preventDefault();
                void run(() => addItemBarcode(itemIdOne, adding.trim()));
              }
            }}
          />
          <button type="button" className="primary small"
            disabled={disabled || blocked || !adding.trim() || unlocked !== null}
            onClick={() => void run(() => addItemBarcode(itemIdOne, adding.trim()))}>Add barcode</button>
        </div>
      </details>

      {problem && (
        <p className={problem.level === "error" ? "error small" : "small barcode-warn"}>{problem.message}</p>
      )}
      {error !== "" && <p className="error small">{error}</p>}
    </div>
  );
}

/**
 * Every recorded change to one item — the same list the portal shows, same columns, same order.
 *
 * ⚠⚠ COLLAPSED, AND LOADED ONLY WHEN OPENED. This is the bottom of a dialog the operator opens to
 * change a price; making every such open fetch an audit trail nobody asked for would slow the common
 * case for the rare one. `open` gates the fetch, and `rows !== null` stops a re-fetch on every toggle.
 *
 * ⚠ The early rows may be SYNTHETIC — the server derives "created" / "last changed" bookends from the
 * item's own timestamps for the era before audit logging, and says so in the detail. That is honest
 * about a gap rather than presenting a short list as a complete one.
 */
export function ItemHistory({ itemIdOne }: { itemIdOne: string }) {
  const [rows, setRows] = useState<ItemHistoryRow[] | null>(null);
  const [error, setError] = useState("");
  const [open, setOpen] = useState(false);

  useEffect(() => {
    if (!open || rows !== null) return;
    fetchItemHistory(itemIdOne)
      .then((r) => setRows(r.rows))
      .catch((e) => setError(e instanceof Error ? e.message : String(e)));
  }, [open, rows, itemIdOne]);

  return (
    <details
      className="barcode-add"
      onToggle={(e) => setOpen((e.currentTarget as HTMLDetailsElement).open)}
    >
      <summary><strong>Change history</strong></summary>
      {error !== "" && <p className="error small">{error}</p>}
      {!open ? null : rows === null ? (
        <p className="muted small">Loading…</p>
      ) : rows.length === 0 ? (
        <p className="muted small">Nothing recorded for this item yet.</p>
      ) : (
        <table>
          <thead><tr><th>When</th><th>What</th><th>Detail</th><th>By</th></tr></thead>
          <tbody>
            {rows.map((r, i) => (
              <tr key={`${r.atUtc}-${i}`}>
                {/* ⚠ The server sends UTC; a bare instant with no `Z` parses as LOCAL in a browser and
                    would shift every timestamp by the offset. */}
                <td className="small">{new Date(r.atUtc.endsWith("Z") ? r.atUtc : `${r.atUtc}Z`).toLocaleString("en-GB")}</td>
                <td className="small">{r.type}</td>
                <td className="small">{r.detail}</td>
                {/* ⚠ "—" rather than blank: a change with no recorded actor is a real state (an import,
                    or a system job), and an empty cell reads as a load failure. */}
                <td className="small">{r.by ?? "—"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </details>
  );
}
