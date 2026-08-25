import { useEffect, useMemo, useRef, useState } from "react";
import DialogX from "./DialogX.tsx";
import {
  createItem,
  createStock,
  fetchCategories,
  fetchItemsPaged,
  fetchStockLevelsFor,
  fetchTaxes,
  findItemByBarcode,
  postStockMovement,
  updateItem,
  type Category,
  type Item,
  type ItemInput,
  type Tax,
} from "./api.ts";
import { gbp } from "./money.ts";
import DataTable from "./DataTable.tsx";
import { takeNewItemBarcode } from "./newItemHandoff.ts";
import ItemBarcodeList, { ItemHistory } from "./ItemBarcodes.tsx";
import { canAdjustStock, canManageBarcodes, canManageItems, canViewItemHistory } from "./pipeline.ts";
import {
  adjustedMessage,
  amountHint,
  buildStockMovement,
  type StockDirection,
} from "./stockAdjust.ts";

export default function InventoryPage() {
  const [items, setItems] = useState<Item[]>([]);
  const [cats, setCats] = useState<Category[]>([]);
  // FE4.3: the standard DataTable in SERVER mode — the catalogue is 20k+ items, so the window
  // stays server-side; `total` comes from X-Pagination (FE4.2), giving a true "X–Y of N" where
  // the old hand-rolled pager could only say "page N" and guess whether Next was live.
  const [skip, setSkip] = useState(0);
  const [take, setTake] = useState(25);
  const [total, setTotal] = useState(0);
  const [search, setSearch] = useState("");
  const [catFilter, setCatFilter] = useState(""); // FE5.0: server-side category filter
  const [state, setState] = useState<"loading" | "ready" | "error">("loading");
  const [error, setError] = useState("");
  const [editing, setEditing] = useState<Item | "new" | null>(null);
  // 2026-08-25: the item whose stock count is being changed. Separate from `editing` on purpose —
  // changing a price and changing a count are different jobs held by different permissions.
  const [adjusting, setAdjusting] = useState<Item | null>(null);
  const [notice, setNotice] = useState("");
  // FE5.2 current stock for the visible page (one batched call)
  const [levels, setLevels] = useState<Map<string, { untracked: boolean; quantity: number | null }>>(new Map());

  // WP4.3: show each item's category in the list (the dialog already assigns it; the list didn't).
  const catName = useMemo(() => new Map(cats.map((c) => [c.idOne, c.name])), [cats]);
  useEffect(() => { void fetchCategories().then(setCats).catch(() => undefined); }, []);

  // Handoff from the till: an unknown scan opened this tab to create the item, so open the
  // Add dialog straight away with the scanned barcode already in it.
  const [newBarcode, setNewBarcode] = useState("");
  useEffect(() => {
    const handed = takeNewItemBarcode();
    // ⚠ `canManageItems()` HERE TOO, not only on the button. The handoff is a SEPARATE entry
    // point, and without this a cashier arriving from the till (or a deep link) would get the
    // Add dialog and a 403 on save — refused after typing, which is the worst possible order.
    if (handed && canManageItems()) { setNewBarcode(handed); setEditing("new"); }
  }, []);

  /**
   * Re-read on-hand stock for the rows on screen.
   *
   * ⚠⚠ CALLED AGAIN AFTER AN ADJUSTMENT, and that is not cosmetic. MAUI's twin says it plainly:
   * without the re-read "the operator writes off two, sees the same number, and does it again — and
   * the ledger takes both."
   *
   * ⚠ `onFailure` exists because the two callers want opposite things. On first load, blanking to "…"
   * is honest. On a post-adjustment refresh it is not: blanking empties every row's level, which also
   * withdraws the Adjust stock button from every row (it is gated on a known, tracked level) — so a
   * successful write would appear to break the page. There the previous numbers stand and the notice
   * already says what happened.
   */
  function loadLevels(rows: Item[], onFailure: "blank" | "keep" = "blank") {
    if (rows.length === 0) { setLevels(new Map()); return; }
    void fetchStockLevelsFor(rows.map((r) => r.idOne))
      .then((ls) => setLevels(new Map(ls.map((l) => [l.itemIdOne, { untracked: l.untracked, quantity: l.quantity }]))))
      .catch(() => { if (onFailure === "blank") setLevels(new Map()); });
  }

  function load() {
    setState("loading");
    // the legacy endpoint pages by 1-based page number, DataTable thinks in skip/take
    fetchItemsPaged(Math.floor(skip / take) + 1, take, search, catFilter)
      .then(({ rows, total: n }) => {
        setItems(rows);
        setTotal(n ?? skip + rows.length + (rows.length === take ? take : 0)); // pre-FE4.2 fallback
        setState("ready");
        loadLevels(rows);
      })
      .catch((e) => {
        setError(String(e));
        setState("error");
      });
  }

  // debounced so typing in the DataTable search box doesn't fire a request per keystroke
  useEffect(() => {
    const t = setTimeout(load, search ? 300 : 0);
    return () => clearTimeout(t);
  }, [skip, take, search, catFilter]); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <section className="panel">
      <div className="panel-head">
        <h2>Inventory</h2>
        {/* ⚠⚠ SUPERVISOR AND ABOVE (Matt, 2026-08-21). Hidden rather than disabled: a disabled
            "Add item" invites a cashier to ask why, and the honest answer is that it is not their job.
            ⚠ The server refuses regardless (`ItemController.Post`) — this only stops offering it. */}
        {canManageItems() && (
          <button className="ghost" onClick={() => setEditing("new")}>
            Add item
          </button>
        )}
        <label className="small">Category{" "}
          <select value={catFilter} onChange={(e) => { setSkip(0); setCatFilter(e.target.value); }}>
            <option value="">All</option>
            {cats.map((c) => <option key={c.idOne} value={c.idOne}>{c.name}</option>)}
          </select>
        </label>
      </div>

      {notice && <p className="small discount-note">{notice}</p>}
      {state === "error" && <p className="error">Could not load inventory: {error}</p>}
      {/* ⚠ Edit is offered only to a supervisor and above (Matt, 2026-08-21). No action at all
          for a cashier rather than a disabled one — the list stays fully readable, which is the
          point: looking up a price is everybody's job, changing one is not. */}
      <DataTable<Item>
        columns={[
          { key: "idOne", label: "Barcode / Id", render: (i) => <span className="mono small">{i.idOne}</span> },
          { key: "name", label: "Name" },
          { key: "brand", label: "Brand", render: (i) => (i.brand === "NOT EXIST" || i.brand === "-" ? "" : i.brand) },
          { key: "catId", label: "Category", render: (i) => catName.get(i.catId) ?? "—" },
          {
            // FE5.2: on-hand stock; ∞ for items whose stock deliberately isn't tracked.
            key: "stock", label: "Stock", numeric: true, sortable: false,
            render: (i) => {
              const l = levels.get(i.idOne);
              if (!l) return <span className="muted">…</span>;
              if (l.untracked) return <span title="Stock isn't tracked for this item">∞</span>;
              if (l.quantity == null) return <span className="muted" title="No stock record yet">—</span>;
              return <span className={l.quantity < 0 ? "error" : undefined}>{l.quantity}</span>;
            },
          },
          { key: "price", label: "Price", numeric: true, render: (i) => gbp(Math.round(i.price * 100)) },
        ]}
        rows={items}
        getKey={(i) => i.idOne}
        server={{
          total, skip, take, search,
          onSearch: (s) => { setSkip(0); setSearch(s); },
          onPage: (s, t) => { setSkip(s); setTake(t); },
        }}
        searchPlaceholder="Search name, barcode, brand…"
        rowActions={(canManageItems() || canAdjustStock()) ? (i) => {
          // ⚠ TWO DIFFERENT JOBS, TWO DIFFERENT PERMISSIONS. Edit is `pos.items.manage` /
          // `portal.prices.manage`; adjusting a count is `pos.stock.adjust` / `portal.stock.adjust`.
          // A Store Manager holds both, a Supervisor may hold only the second, so neither button may
          // imply the other.
          const l = levels.get(i.idOne);
          return (
            <>
              {canManageItems() && (
                <button className="ghost small" onClick={() => setEditing(i)}>Edit</button>
              )}{" "}
              {/* ⚠ Withheld until the level is KNOWN and TRACKED. An untracked item's count is
                  meaningless by design, so a movement against it writes a number nothing will ever
                  read — MAUI drops the same entry from its tap menu on `StockDisplay == "∞"`. And
                  while the batched level call is still in flight there is no current figure to put in
                  front of the operator, which the whole dialog is built around showing. */}
              {canAdjustStock() && l && !l.untracked && (
                <button className="ghost small" onClick={() => setAdjusting(i)}>Adjust stock…</button>
              )}
            </>
          );
        } : undefined}
        emptyText={state === "loading" ? "Loading…" : `No items${search ? ` matching “${search}”` : ""}.`}
      />

      {editing && (
        <ItemDialog
          // keyed so "Open this item" (duplicate barcode) REMOUNTS the dialog — the field
          // state is seeded from props at mount, so a prop swap alone would keep the old form.
          key={editing === "new" ? `new:${newBarcode}` : editing.idOne}
          item={editing === "new" ? null : editing}
          initialId={newBarcode}
          onClose={() => { setEditing(null); setNewBarcode(""); }}
          onOpenExisting={(i) => setEditing(i)}
          onDone={(msg) => {
            setEditing(null);
            setNewBarcode("");
            setNotice(msg);
            load();
          }}
        />
      )}

      {adjusting && (
        <StockAdjustDialog
          item={adjusting}
          current={levels.get(adjusting.idOne)?.quantity ?? null}
          onClose={() => setAdjusting(null)}
          onDone={(msg) => {
            setAdjusting(null);
            setNotice(msg);
            // ⚠ Levels only — NOT `load()`. The item's own fields did not change, and re-running the
            // paged query would also throw away the operator's place in a 20k-row list.
            loadLevels(items, "keep");
          }}
        />
      )}
    </section>
  );
}

/**
 * Change an item's stock count. The C2 twin of MAUI's `ViewAllViewModel.ExecuteAdjustStock`.
 *
 * ⚠ SAME ORDER AND SAME WORDING AS MAUI — direction, then how many, then why. MAUI asks in three
 * sequential prompts because that is what its dialog stack does; this is one form, because that is
 * what every other editor on this till is. ⚠ Under the 2026-08-19 look-and-feel ruling that
 * difference is deliberate and worth naming: the *steps*, the *labels* and the *guards* match, so an
 * operator moving between tills meets the same questions in the same order.
 *
 * ⚠ D4: a visible ✕ via `DialogX`, Escape cancels, the backdrop cancels — and all three are disabled
 * while a request is in flight, because closing mid-flight leaves the caller waiting on a promise
 * whose UI has gone.
 */
function StockAdjustDialog({
  item,
  current,
  onClose,
  onDone,
}: {
  item: Item;
  current: number | null;
  onClose: () => void;
  onDone: (message: string) => void;
}) {
  const [direction, setDirection] = useState<StockDirection>("writeOff");
  const [amount, setAmount] = useState("");
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  // ⚠ D4 rule 2 — Escape cancels. Same shape as `HelpPanel`.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape" && !busy) onClose(); };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose, busy]);

  async function save() {
    // ⚠ The rule refuses first, so a bad amount or a blank reason never becomes a round trip. The
    // server would refuse both anyway — being told so after a wait helps nobody.
    const built = buildStockMovement(direction, amount, reason);
    if (!built.ok) { setError(built.problem); return; }

    setBusy(true);
    setError("");
    try {
      await postStockMovement(item.idOne, built.movement);
      onDone(adjustedMessage(direction, Math.abs(built.movement.qty), item.name));
    } catch (e) {
      // ⚠ The server's sentence, verbatim — it names the rule that was broken.
      setError(e instanceof Error ? e.message : String(e));
      setBusy(false);
    }
  }

  const writeOff = direction === "writeOff";

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && !busy && onClose()}>
      <div className="dialog" role="dialog" aria-label="Adjust stock">
        <h2>Adjust stock</h2>
        <DialogX onClose={onClose} disabled={busy} />

        <p className="small">
          <strong>{item.name}</strong>
          <span className="mono small block">{item.idOne}</span>
        </p>

        {/* ⚠ THE DIRECTION IS CHOSEN, NEVER TYPED. The sign comes from this and nowhere else — a
            minus sign typed into "write off how many" would flip the choice just made, which is the
            one mistake this whole flow is arranged around. */}
        <label className="setting-row choice-row">
          <input
            type="radio"
            name="stock-direction"
            checked={writeOff}
            disabled={busy}
            onChange={() => { setDirection("writeOff"); setError(""); }}
          />
          <span className="grow">
            Write some off
            <span className="muted small block">Damaged, lost, expired, used in the shop.</span>
          </span>
        </label>
        <label className="setting-row choice-row">
          <input
            type="radio"
            name="stock-direction"
            checked={!writeOff}
            disabled={busy}
            onChange={() => { setDirection("add"); setError(""); }}
          />
          <span className="grow">
            Add some
            <span className="muted small block">Found, returned to stock, a delivery not booked in.</span>
          </span>
        </label>

        <div className="form-grid">
          <label>
            {writeOff ? "Write off how many?" : "Add how many?"}
            <input
              inputMode="numeric"
              autoFocus
              value={amount}
              disabled={busy}
              onChange={(e) => { setAmount(e.target.value); setError(""); }}
            />
          </label>
          <label>
            Why?
            <input
              maxLength={120}
              value={reason}
              disabled={busy}
              placeholder={writeOff ? "damaged in transit" : "found behind the counter"}
              onChange={(e) => { setReason(e.target.value); setError(""); }}
            />
          </label>
        </div>

        {/* ⚠⚠ THE GUARD SENTENCE. The server ADDS the delta, so an operator who reads the box as
            "the new total" doubles the stock and nothing errors. */}
        <p className="muted small">{amountHint(item.name, current)}</p>

        {error && <p className="error">{error}</p>}

        <div className="dialog-actions">
          <button className="ghost" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="primary" onClick={() => void save()} disabled={busy}>
            {busy ? "Saving…" : writeOff ? "Write off" : "Add"}
          </button>
        </div>
      </div>
    </div>
  );
}

function ItemDialog({
  item,
  initialId = "",
  onClose,
  onDone,
  onOpenExisting,
}: {
  item: Item | null;
  /** Barcode carried over from an unknown till scan (new items only). */
  initialId?: string;
  onClose: () => void;
  onDone: (msg: string) => void;
  onOpenExisting: (item: Item) => void;
}) {
  const [taxes, setTaxes] = useState<Tax[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [id, setId] = useState(item?.idOne ?? initialId);
  const [name, setName] = useState(item?.name ?? "");
  const [brand, setBrand] = useState(item?.brand === "-" ? "" : (item?.brand ?? ""));
  const [desc, setDesc] = useState(item?.desc ?? "");
  const [cost, setCost] = useState(item ? String(item.cost) : "");
  const [price, setPrice] = useState(item ? String(item.price) : "");
  const [taxId, setTaxId] = useState<number>(item?.taxId ?? 0);
  const [catId, setCatId] = useState("");
  const [stock, setStock] = useState("");
  const [untracked, setUntracked] = useState(item?.stockUntracked ?? false); // FE5.5
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  // the item already holding the typed barcode — blocks the create until it's changed
  const [clash, setClash] = useState<Item | null>(null);
  // latest field value, so a slow lookup can't flag a barcode the operator has since edited
  const idRef = useRef(id);
  // a barcode handed over from the till is already known to be unknown — but check it anyway,
  // since the operator may edit it before saving
  useEffect(() => { if (!item && initialId) void checkBarcodeFree(); }, []); // eslint-disable-line react-hooks/exhaustive-deps

  /** Duplicate check on blur — the operator hears about a clash as soon as they leave the
   *  barcode box, not after filling in the whole form. Submit re-checks authoritatively. */
  async function checkBarcodeFree() {
    const candidate = id.trim();
    if (item || !candidate) return; // edits keep their barcode; nothing to check
    const existing = await findItemByBarcode(candidate);
    if (existing && idRef.current.trim() === candidate) setClash(existing);
  }

  useEffect(() => {
    Promise.all([fetchTaxes(), fetchCategories()])
      .then(([t, c]) => {
        setTaxes(t);
        setCategories(c);
        if (!item && t.length) setTaxId(t[0].idOne);
        if (c.length) setCatId(item?.catId ?? c[0].idOne);
      })
      .catch((e) => setError(String(e)));
  }, [item]);

  const tax = taxes.find((t) => t.idOne === taxId);
  const priceNum = parseFloat(price) || 0;
  // taxes store the multiplier (1.2 = 20%): ex-tax price derives from it
  const exPrice = tax && tax.rate > 0 ? priceNum / tax.rate : priceNum;

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError("");
    setClash(null);

    // NatApp parity: a new item's barcode must be free before we try to create it. Without
    // this the composite PK rejects the insert and the till showed a raw "API 500".
    if (!item) {
      const existing = await findItemByBarcode(id.trim());
      if (existing) {
        setClash(existing);
        setBusy(false);
        return;
      }
    }

    const input: ItemInput = {
      id: id.trim(),
      name: name.trim(),
      brand: brand.trim(),
      desc: desc.trim(),
      cost: parseFloat(cost) || 0,
      price: priceNum,
      exPrice: Math.round(exPrice * 100) / 100,
      taxId,
      catId,
      // carried through so an edit can't reset them (see itemBody)
      stockUntracked: untracked,
      binnedAtUtc: item?.binnedAtUtc ?? null,
    };
    try {
      if (item) {
        await updateItem(input);
        onDone(`Updated ${input.name}.`);
      } else {
        await createItem(input);
        const qty = parseInt(stock, 10);
        if (qty > 0) await createStock(input.id, qty);
        onDone(`Created ${input.name}${qty > 0 ? ` with ${qty} in stock` : ""}.`);
      }
    } catch (err) {
      setError(String(err));
      setBusy(false);
    }
  }

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && !busy && onClose()}>
      {/* ⚠ `wide` (38rem) — Matt, 2026-08-20: *"the text areas do not fit"*. The default `.dialog` is
          26rem, which put a two-column form-grid in ~190px columns; the `minmax(0, 1fr)` fix in
          index.css stops them OVERFLOWING, and this gives them room to be readable rather than
          merely contained. It also makes space for the barcode list below. */}
      <form className="dialog wide" onSubmit={submit}>
        <h2>{item ? "Edit item" : "Add item"}</h2>
        <DialogX onClose={onClose} disabled={busy} />
        {/* ⚠ THE BARCODE BOX IS OUT OF THE GRID ON PURPOSE (Matt, 2026-08-20 — same change as the
            portal's). Its additional-barcode list has to sit directly under it to read as "…and these
            also scan to this item", and a full-width child inside a two-column grid cell either
            squeezes into half the dialog or breaks the columns. */}
        {/* ⚠ `block-label`, not `block`: it carries the grid's own label+input styling, so lifting the
            field out of the grid does not change how it LOOKS. */}
        <label className="block-label">
          Barcode / id (max 20)
          <input
            value={id}
            onChange={(e) => { setId(e.target.value); idRef.current = e.target.value; setClash(null); }}
            onBlur={() => void checkBarcodeFree()}
            maxLength={20}
            required
            disabled={busy || !!item}
            aria-invalid={!!clash}
          />
        </label>
        {/* ⚠ EDITS ONLY. A barcode row points at an item by `IdOne`, so there is nothing to point at
            until the item exists — and the id above is still editable while it doesn't. */}
        {item && canManageBarcodes() && <ItemBarcodeList itemIdOne={item.idOne} busy={busy} />}
        <div className="form-grid">
          <label>
            Name
            <input value={name} onChange={(e) => setName(e.target.value)} required disabled={busy} />
          </label>
          <label>
            Brand
            <input value={brand} onChange={(e) => setBrand(e.target.value)} disabled={busy} />
          </label>
          <label>
            Description
            <input value={desc} onChange={(e) => setDesc(e.target.value)} disabled={busy} />
          </label>
          <label>
            Cost (£)
            <input inputMode="decimal" value={cost} onChange={(e) => setCost(e.target.value)} disabled={busy} />
          </label>
          <label>
            Price inc tax (£)
            <input inputMode="decimal" value={price} onChange={(e) => setPrice(e.target.value)} required disabled={busy} />
          </label>
          <label>
            Tax
            <select value={taxId} onChange={(e) => setTaxId(Number(e.target.value))} disabled={busy}>
              {taxes.map((t) => (
                <option key={t.idOne} value={t.idOne}>
                  {t.name}
                </option>
              ))}
            </select>
          </label>
          <label>
            Category
            <select value={catId} onChange={(e) => setCatId(e.target.value)} disabled={busy}>
              {categories.map((c) => (
                <option key={c.idOne} value={c.idOne}>
                  {c.name}
                </option>
              ))}
            </select>
          </label>
          {!item && !untracked && (
            <label>
              Initial stock (optional)
              <input inputMode="numeric" value={stock} onChange={(e) => setStock(e.target.value)} disabled={busy} />
            </label>
          )}
        </div>
        <label className="setting-row">
          <span className="grow">
            Don't track stock for this item
            <span className="muted small block">
              For things that are effectively unlimited — carrier bags, back-issues. Sales are still
              recorded and reported; the stock count is skipped and the item shows ∞.
            </span>
          </span>
          <input type="checkbox" checked={untracked} onChange={(e) => setUntracked(e.target.checked)} disabled={busy} />
        </label>
        <p className="muted small">Ex-tax price: £{exPrice.toFixed(2)} (derived from the selected tax)</p>

        {clash && (
          <div className="clash-note" role="alert">
            <p className="clash-title">There is already an item with this barcode</p>
            <p className="small">
              This item has this barcode — <strong>{clash.name}</strong>
              {clash.binnedAtUtc && <span className="muted"> (in the bin)</span>}
            </p>
            <p className="small muted">
              Open it below, or change the barcode and save again — closing this window keeps the
              catalogue untouched.
            </p>
            <button type="button" className="link-btn" onClick={() => onOpenExisting(clash)}>
              Open “{clash.name}”
            </button>
          </div>
        )}

        {/* ⚠ Matt, 2026-08-20: *"At the bottom of an item listing when editing"* — the bottom, below
            the save buttons' concern, because it is a record to consult rather than a field to fill. */}
        {item && canViewItemHistory() && <ItemHistory itemIdOne={item.idOne} />}

        {error && <p className="error small">{error}</p>}
        <div className="dialog-actions">
          <button type="button" className="ghost" onClick={onClose} disabled={busy}>
            Cancel
          </button>
          {/* blocked while a clash is showing; editing the barcode clears it */}
          <button type="submit" className="primary" disabled={busy || !!clash || !id.trim() || !name.trim() || !price}>
            {busy ? "Saving…" : "Save"}
          </button>
        </div>
      </form>
    </div>
  );
}
