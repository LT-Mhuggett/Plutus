import { useEffect, useState } from "react";
import {
  addItemBarcode, fetchItemBarcodes, fetchItemHistory, findItemByBarcode, removeItemBarcode,
  renameItemBarcode, type ItemHistoryRow,
} from "./api.ts";
import { barcodeProblem } from "./barcodeProblem.ts";

// An item's ADDITIONAL barcodes, and its change history — the two blocks that hang off the item
// editor (`InventoryItems.tsx`). Kept out of that file because it was already 560 lines and these are
// self-contained.
//
// ⚠⚠ AN ALIAS IS NOT AN IDENTITY. The "Barcode / id" box in the dialog above is the item's identity —
// it seeds the deterministic item GUID, it is on every historical sale line, and it stays immutable on
// an edit. Everything here is additive: a scan of one of these codes RESOLVES to the item, and the
// till then carries the item's own code onward. See `Build/archive/Multi-barcode plan.md`.
//
// ⚠ The live check moved to `barcodeProblem.ts` — byte-identical in the web till, pinned by a test
// there. This component and the till's are NOT identical (theirs gates on `canManageBarcodes()`), and
// a rule inside a file that legitimately differs is a rule that drifts unnoticed.

/**
 * An item's additional barcodes: list, add, correct, remove.
 *
 * ⚠⚠ EACH CODE IS LOCKED UNTIL SOMEBODY UNLOCKS IT. Matt: *"Barcodes do need to be editable incase you
 * misstype it, but again have the same real time check. Barcodes should be 'Locked' to avoid
 * accidently changing the barcode."* A barcode is the string a scanner matches on, so a stray
 * keystroke in an always-editable box is a code that silently stops scanning. Unlocking is one
 * deliberate click.
 *
 * ⚠⚠ A CORRECTION IS ONE REQUEST, NOT DELETE-THEN-ADD. Done as two calls, a failure between them would
 * leave the item with NEITHER code; the server renames inside a transaction.
 */
export function ItemBarcodeList({ itemIdOne, busy }: { itemIdOne: string; busy: boolean }) {
  const [codes, setCodes] = useState<string[] | null>(null);
  /** Every code in the tenant — what makes the uniqueness check instant instead of a round trip. */
  const [allCodes, setAllCodes] = useState<string[]>([]);
  const [adding, setAdding] = useState("");
  /** The code currently unlocked for correction, and its draft value. */
  const [unlocked, setUnlocked] = useState<string | null>(null);
  const [draft, setDraft] = useState("");
  const [working, setWorking] = useState(false);
  const [error, setError] = useState("");
  /** Set by the debounced lookup when a typed code turns out to be some item's OWN barcode. */
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

  // ⚠ DEBOUNCED, and only for the half the local list cannot answer: is this code some other item's
  // OWN barcode? 350ms so a scanner — which types a whole code in one burst — fires one lookup rather
  // than one per character.
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
      // ⚠ Verbatim: `ApiError.message` carries the server's `detail`, which is a sentence written to
      // be read by whoever is trying to set the barcode up.
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
                    className="grow"
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
                  {/* ⚠ The lock IS the safety. It takes a deliberate click to make a scanning code
                      typeable — see the component note. */}
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

      {/* ⚠ COLLAPSED — Matt: *"have the text and add new barcode collapsed"*. The explanation earns its
          place once; it does not earn being re-read on every edit. */}
      <details className="barcode-add">
        <summary>Add another barcode</summary>
        <p className="muted small">
          Scanning any of these rings up this item. Useful when a supplier changes codes and you still
          have old stock on the shelf — keep both while the changeover lasts, then remove the old one.
        </p>
        <div className="toolbar">
          <input
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

      {/* ⚠ The live verdict, for whichever box is active. A WARNING still allows the save (the server
          trims); an ERROR disables it. */}
      {problem && (
        <p className={problem.level === "error" ? "error small" : "small barcode-warn"}>{problem.message}</p>
      )}
      {error !== "" && <p className="error small">{error}</p>}
    </div>
  );
}

/**
 * Everything that has happened to this item.
 *
 * ⚠⚠ MATT, 2026-08-20: *"At the bottom of an item listing when editing, can they be a history kept of
 * every change to this item, from initial creation etc, logging time, what was changed and who by?"*
 *
 * ⚠⚠ THE HISTORY BEGINS WHERE THE LOGGING DID, AND THE ROWS SAY SO. Item edits were audited for the
 * first time on 2026-08-20; for anything before that there is genuinely nothing to reconstruct, so the
 * server bookends the list with the item's own created/modified stamps and those rows state plainly
 * that the detail was never recorded. Better than a gap, and far better than a guess.
 *
 * ⚠ COLLAPSED, and only fetched once opened — an item dialog that loaded a history nobody looked at
 * would add a request to every edit.
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
      className="card store-card"
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
