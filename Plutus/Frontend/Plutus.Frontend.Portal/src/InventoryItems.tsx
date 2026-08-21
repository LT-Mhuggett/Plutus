import { useEffect, useMemo, useRef, useState } from "react";
import {
  bulkCount, bulkItems, createItem, fetchCatalogueItemsPaged, fetchCategories,
  fetchStockLevelsFor, fetchTaxes, findItemByBarcode, gbp, updateItem,
  type BulkAction, type BulkCriteria, type CatalogueItem, type Category, type ItemInput, type Tax,
} from "./api.ts";
import { ItemBarcodeList, ItemHistory } from "./ItemBarcodes.tsx";
import { canBulkEditInventory } from "./auth.ts";
import DialogX from "./DialogX.tsx";
import DataTable from "./DataTable.tsx";
import { useNav } from "./nav.tsx";
import { ask } from "./Ask.tsx";

// WP4.2/4.3 portal item catalogue: add/edit items (name, brand, cost, price inc VAT with ex-VAT
// derived, tax band, category) — parity with the till's inventory dialog, reusing the guardrailed
// legacy api/Item so the VAT band check applies identically. FE5.0: the Category filter is
// SERVER-side (it used to narrow only the fetched page, usually showing nothing). FE4.3: the list
// is now the standard DataTable in SERVER mode — 20k+ items stay server-windowed, and FE4.2's
// X-Pagination total turns the old "page N" guess into a true "X–Y of N".
// FE5.2/5.3/5.4: current-stock column, bulk edit (tick-list or whole-filter) behind
// inventory.bulk, and the Bin — a soft delete that keeps history.

export default function InventoryItems({ binView = false }: { binView?: boolean }) {
  const { focus } = useNav();
  const [items, setItems] = useState<CatalogueItem[]>([]);
  const [cats, setCats] = useState<Category[]>([]);
  const [skip, setSkip] = useState(0);
  const [take, setTake] = useState(25);
  const [total, setTotal] = useState(0);
  const [search, setSearch] = useState("");
  const [catFilter, setCatFilter] = useState("");
  const [state, setState] = useState<"loading" | "ready" | "error">("loading");
  const [error, setError] = useState("");
  const [editing, setEditing] = useState<CatalogueItem | "new" | null>(null);
  const [notice, setNotice] = useState("");
  // FE5.2 current stock, fetched once per visible page
  const [levels, setLevels] = useState<Map<string, { untracked: boolean; quantity: number | null }>>(new Map());
  // FE5.3 selection
  const [ticked, setTicked] = useState<Set<string>>(new Set());
  const [bulkBusy, setBulkBusy] = useState(false);
  const canBulk = canBulkEditInventory();

  const catName = useMemo(() => new Map(cats.map((c) => [c.id, c.name])), [cats]);
  const criteria = (): BulkCriteria => ({ search: search || undefined, matchAllWords: true, catId: catFilter || null, binned: binView });

  // FE5.1: a category clicked in the Categories manager deep-links here pre-filtered.
  useEffect(() => {
    if (focus && focus.startsWith("cat:")) { setCatFilter(focus.slice(4)); setSkip(0); }
  }, [focus]);

  const load = () => {
    setState("loading");
    setTicked(new Set());
    fetchCatalogueItemsPaged(Math.floor(skip / take) + 1, take, search, catFilter, binView)
      .then(({ rows, total: n }) => {
        setItems(rows);
        setTotal(n ?? skip + rows.length + (rows.length === take ? take : 0)); // pre-FE4.2 fallback
        setState("ready");
        // one batched call for the page's stock, never one per row
        if (rows.length > 0) {
          void fetchStockLevelsFor(rows.map((r) => r.idOne))
            .then((ls) => setLevels(new Map(ls.map((l) => [l.itemIdOne, { untracked: l.untracked, quantity: l.quantity }]))))
            .catch(() => setLevels(new Map()));
        } else setLevels(new Map());
      })
      .catch((e) => { setError(String(e instanceof Error ? e.message : e)); setState("error"); });
  };
  // debounced so typing in the search box doesn't fire a request per keystroke
  useEffect(() => {
    const t = setTimeout(load, search ? 300 : 0);
    return () => clearTimeout(t);
  }, [skip, take, search, catFilter, binView]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { void fetchCategories().then(setCats).catch(() => undefined); }, []);

  const toggle = (id: string) => setTicked((prev) => {
    const next = new Set(prev);
    if (next.has(id)) next.delete(id); else next.add(id);
    return next;
  });
  const allOnPageTicked = items.length > 0 && items.every((i) => ticked.has(i.idOne));

  /** Run a bulk action. `whole` = everything matching the current filter (server-resolved),
   *  otherwise just the ticked rows. Criteria mode always confirms with the SERVER's count. */
  async function runBulk(action: BulkAction, whole: boolean, extra?: { value?: string; categoryId?: string }) {
    setError(""); setNotice("");
    try {
      if (whole) {
        const { count, capped, max } = await bulkCount(criteria());
        if (count === 0) { setNotice("Nothing matches the current filter."); return; }
        if (capped) { setError(`That filter matches ${count} items — over the ${max} limit. Narrow it first.`); return; }
        if (!await ask.confirm({
          title: `${describe(action, extra, catName)}?`,
          body: (
            <>
              <p className="small">
                This applies to <strong>ALL {count} item{count === 1 ? "" : "s"}</strong> matching the
                current filter — not just the ones on screen.
              </p>
              <p className="muted small">
                There's no undo button, but the change is audited with the previous values.
              </p>
            </>
          ),
          confirmLabel: `Apply to ${count} item${count === 1 ? "" : "s"}`,
          danger: action === "bin",
        })) return;
      } else if (!await ask.confirm({
        title: `${describe(action, extra, catName)}?`,
        body: <p className="small">For the {ticked.size} selected item{ticked.size === 1 ? "" : "s"}.</p>,
        confirmLabel: `Apply to ${ticked.size}`,
        danger: action === "bin",
      })) {
        return;
      }
      setBulkBusy(true);
      const r = await bulkItems({
        action, value: extra?.value, categoryId: extra?.categoryId,
        ...(whole ? { criteria: criteria() } : { ids: [...ticked] }),
      });
      setNotice(`${r.affected} item${r.affected === 1 ? "" : "s"}: ${r.detail}.`);
      load();
    } catch (e) {
      setError(String(e instanceof Error ? e.message : e));
    } finally {
      setBulkBusy(false);
    }
  }

  return (
    <section className="panel">
      <div className="toolbar">
        <h2 className="grow">{binView ? "Bin" : "Items"}</h2>
        <label>Category{" "}
          <select value={catFilter} onChange={(e) => { setSkip(0); setCatFilter(e.target.value); }}>
            <option value="">All</option>
            {cats.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        </label>
        {!binView && <button className="primary small" onClick={() => setEditing("new")}>+ Add item</button>}
      </div>

      {binView && (
        <p className="muted small">
          Binned items are hidden from the till, the webstore feed and the normal Items list — but
          nothing is deleted, so past sales and reports still show them. Restore puts an item back
          on sale.
        </p>
      )}

      {canBulk && (
        <BulkBar
          binView={binView} cats={cats} busy={bulkBusy}
          tickedCount={ticked.size} filterActive={!!search || !!catFilter}
          onRun={runBulk}
        />
      )}

      {notice && <p className="small discount-note">{notice}</p>}
      {state === "error" && <p className="error">Could not load items: {error}</p>}
      <DataTable<CatalogueItem>
        columns={[
          ...(canBulk ? [{
            key: "tick",
            label: "",
            sortable: false,
            render: (i: CatalogueItem) => (
              <input type="checkbox" checked={ticked.has(i.idOne)} onChange={() => toggle(i.idOne)}
                aria-label={`Select ${i.name}`} />
            ),
          }] : []),
          { key: "idOne", label: "Barcode / Id", render: (i) => <span className="mono small">{i.idOne}</span> },
          { key: "name", label: "Name" },
          { key: "brand", label: "Brand", render: (i) => (i.brand === "NOT EXIST" || i.brand === "-" ? "" : i.brand) },
          { key: "catId", label: "Category", render: (i) => catName.get(i.catId) ?? "—" },
          {
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
        rowActions={(i) => binView
          ? <button className="ghost small" disabled={bulkBusy} onClick={async () => {
              setBulkBusy(true);
              try { await bulkItems({ action: "restore", ids: [i.idOne] }); setNotice(`${i.name} restored.`); load(); }
              catch (e) { setError(String(e instanceof Error ? e.message : e)); }
              finally { setBulkBusy(false); }
            }}>Restore</button>
          : <button className="ghost small" onClick={() => setEditing(i)}>Edit</button>}
        emptyText={state === "loading" ? "Loading…"
          : binView ? "The Bin is empty."
          : `No items${search ? ` matching “${search}”` : ""}${catFilter ? ` in ${catName.get(catFilter) ?? "this category"}` : ""}.`}
      />

      {canBulk && items.length > 0 && (
        <div className="toolbar">
          <button className="ghost small" onClick={() => setTicked(allOnPageTicked ? new Set() : new Set(items.map((i) => i.idOne)))}>
            {allOnPageTicked ? "Clear page selection" : `Select all ${items.length} on this page`}
          </button>
          {ticked.size > 0 && <span className="muted small">{ticked.size} selected</span>}
        </div>
      )}

      {editing && (
        <ItemDialog
          // keyed so "Open this item" REMOUNTS the dialog — field state is seeded from props at
          // mount, so a prop swap alone would keep the old form
          key={editing === "new" ? "new" : editing.idOne}
          item={editing === "new" ? null : editing} cats={cats}
          onClose={() => setEditing(null)}
          onOpenExisting={(i) => setEditing(i)}
          onDone={(msg) => { setEditing(null); setNotice(msg); load(); }}
        />
      )}
    </section>
  );
}

/** Human sentence for a bulk action, used in the confirmations so the operator reads what they're
 *  about to do rather than an action code. */
function describe(action: BulkAction, extra: { value?: string; categoryId?: string } | undefined,
                  catName: Map<string, string>): string {
  switch (action) {
    case "set-category": return `Move to category "${catName.get(extra?.categoryId ?? "") ?? "?"}"`;
    case "clear-category": return "Move to Uncategorised";
    case "set-brand": return `Set brand to "${extra?.value}"`;
    case "clear-brand": return "Clear the brand";
    case "bin": return "Move to the Bin (hidden from sale, nothing deleted)";
    case "restore": return "Restore from the Bin";
    case "set-untracked": return "Stop tracking stock (∞)";
    case "clear-untracked": return "Start tracking stock again";
  }
}

/** FE5.3 bulk toolbar. Both selection scopes are explicit buttons — "selected" acts on the
 *  tick-list, "all matching filter" is resolved SERVER-side and always confirms with the real
 *  count, so it can't quietly act on more (or fewer) rows than the operator believes. */
function BulkBar({ binView, cats, busy, tickedCount, filterActive, onRun }: {
  binView: boolean;
  cats: Category[];
  busy: boolean;
  tickedCount: number;
  filterActive: boolean;
  onRun: (action: BulkAction, whole: boolean, extra?: { value?: string; categoryId?: string }) => void;
}) {
  const [action, setAction] = useState<BulkAction>(binView ? "restore" : "set-category");
  const [categoryId, setCategoryId] = useState("");
  const [brand, setBrand] = useState("");
  useEffect(() => { if (!categoryId && cats[0]) setCategoryId(cats[0].id); }, [cats]); // eslint-disable-line react-hooks/exhaustive-deps

  const needsCategory = action === "set-category";
  const needsBrand = action === "set-brand";
  const ready = !needsCategory ? (!needsBrand || !!brand.trim()) : !!categoryId;
  const extra = { value: brand.trim() || undefined, categoryId: categoryId || undefined };

  const actions: { v: BulkAction; label: string }[] = binView
    ? [{ v: "restore", label: "Restore from Bin" }]
    : [
        { v: "set-category", label: "Set category" },
        { v: "clear-category", label: "Remove from category" },
        { v: "set-brand", label: "Set brand" },
        { v: "clear-brand", label: "Clear brand" },
        { v: "set-untracked", label: "Stop tracking stock" },
        { v: "clear-untracked", label: "Track stock again" },
        { v: "bin", label: "Move to Bin" },
      ];

  return (
    <div className="callout">
      <div className="toolbar">
        <strong className="small">Bulk edit</strong>
        <select value={action} onChange={(e) => setAction(e.target.value as BulkAction)} disabled={busy}>
          {actions.map((a) => <option key={a.v} value={a.v}>{a.label}</option>)}
        </select>
        {needsCategory && (
          <select value={categoryId} onChange={(e) => setCategoryId(e.target.value)} disabled={busy}>
            {cats.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        )}
        {needsBrand && (
          <input placeholder="brand" value={brand} onChange={(e) => setBrand(e.target.value)} disabled={busy} />
        )}
        <button className="ghost small" disabled={busy || !ready || tickedCount === 0}
          onClick={() => onRun(action, false, extra)}>
          Apply to {tickedCount} selected
        </button>
        <button className="ghost small" disabled={busy || !ready}
          title={filterActive ? "Every item matching the current search/category filter" : "EVERY item in the catalogue"}
          onClick={() => onRun(action, true, extra)}>
          Apply to all {filterActive ? "matching the filter" : "items"}…
        </button>
      </div>
      <p className="muted small">
        There's no undo button — but every bulk change is audited with the previous values, so a
        mistake can be unpicked. Prices and VAT bands are deliberately not bulk-editable.
      </p>
    </div>
  );
}

function ItemDialog({ item, cats, onClose, onDone, onOpenExisting }:
  { item: CatalogueItem | null; cats: Category[]; onClose: () => void; onDone: (msg: string) => void;
    onOpenExisting: (item: CatalogueItem) => void }) {
  const [taxes, setTaxes] = useState<Tax[]>([]);
  const [id, setId] = useState(item?.idOne ?? "");
  const [name, setName] = useState(item?.name ?? "");
  const [brand, setBrand] = useState(item?.brand === "-" ? "" : (item?.brand ?? ""));
  const [desc, setDesc] = useState(item?.desc ?? "");
  const [cost, setCost] = useState(item ? String(item.cost) : "");
  const [price, setPrice] = useState(item ? String(item.price) : "");
  const [taxId, setTaxId] = useState<number>(item?.taxId ?? 0);
  const [catId, setCatId] = useState(item?.catId ?? "");
  const [untracked, setUntracked] = useState(item?.stockUntracked ?? false); // FE5.5
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  // the item already holding the typed barcode — blocks the create until it's changed
  const [clash, setClash] = useState<CatalogueItem | null>(null);
  // latest field value, so a slow lookup can't flag a barcode since edited
  const idRef = useRef(id);

  /** Checked on blur AND again on submit — the operator hears about a clash as soon as they
   *  leave the barcode box, not after filling in the whole form. */
  async function checkBarcodeFree() {
    const candidate = id.trim();
    if (item || !candidate) return; // edits keep their barcode
    const existing = await findItemByBarcode(candidate);
    if (existing && idRef.current.trim() === candidate) setClash(existing);
  }

  useEffect(() => {
    fetchTaxes().then((t) => { setTaxes(t); if (!item && t.length) setTaxId(t[0].idOne); }).catch((e) => setError(String(e)));
    if (!item && cats.length) setCatId(cats[0].id);
  }, [item, cats]);

  const tax = taxes.find((t) => t.idOne === taxId);
  const priceNum = parseFloat(price) || 0;
  // taxes store the gross multiplier (1.2 = 20%): ex-VAT = inc / multiplier.
  const exPrice = tax && tax.rate > 0 ? priceNum / tax.rate : priceNum;

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true); setError("");
    setClash(null);

    // NatApp/web-till parity: a new item's barcode must be free. Without this the composite PK
    // rejects the insert and the portal showed a raw 500.
    if (!item) {
      const existing = await findItemByBarcode(id.trim());
      if (existing) { setClash(existing); setBusy(false); return; }
    }

    const input: ItemInput = {
      id: id.trim(), name: name.trim(), brand: brand.trim(), desc: desc.trim(),
      cost: parseFloat(cost) || 0, price: priceNum, exPrice: Math.round(exPrice * 100) / 100, taxId, catId,
      // carried through so an edit can't reset them (see itemBody)
      stockUntracked: untracked,
      binnedAtUtc: item?.binnedAtUtc ?? null,
    };
    try {
      if (item) { await updateItem(input); onDone(`Updated ${input.name}.`); }
      else { await createItem(input); onDone(`Created ${input.name}. Set stock levels in the Stock ledger.`); }
    } catch (err) { setError(String(err instanceof Error ? err.message : err)); setBusy(false); }
  };

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && !busy && onClose()}>
      <form className="dialog" onSubmit={submit}>
        <h3>{item ? "Edit item" : "Add item"}</h3>
        {/* ⚠ D4 rule 1 — a visible ✕. `disabled` while saving, for the same reason Cancel is. */}
        <DialogX onClose={onClose} disabled={busy} />
        {/* ⚠⚠ THE BARCODE BLOCK IS OUT OF THE GRID AND FIRST — Matt, 2026-08-20: *"move the 'Other
            barcodes for this item' under the current barcode section … If the product has more than
            one barcode show each one under the current 'Barcode/ID (Max 20)' title."* Full width so
            the list of codes and their controls have room; the rest of the fields stay in the grid
            below. */}
        {/* ⚠ `block-label`, NOT `block`. `label.block` is the portal's wrapper for a TEXTAREA (which
            is already `width: 100%`); an `<input>` inside it sits INLINE after the label text at its
            default width, which would make this one field look nothing like the grid fields directly
            beneath it. `block-label` is the grid's own label rule, outside the grid. */}
        <label className="block-label">Barcode / id (max 20)
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
        {item && <ItemBarcodeList itemIdOne={item.idOne} busy={busy} />}

        <div className="form-grid">
          <label>Name<input value={name} onChange={(e) => setName(e.target.value)} required disabled={busy} /></label>
          <label>Brand<input value={brand} onChange={(e) => setBrand(e.target.value)} disabled={busy} /></label>
          <label>Description<input value={desc} onChange={(e) => setDesc(e.target.value)} disabled={busy} /></label>
          <label>Cost (£)<input inputMode="decimal" value={cost} onChange={(e) => setCost(e.target.value)} disabled={busy} /></label>
          <label>Price inc VAT (£)<input inputMode="decimal" value={price} onChange={(e) => setPrice(e.target.value)} required disabled={busy} /></label>
          <label>Tax
            <select value={taxId} onChange={(e) => setTaxId(Number(e.target.value))} disabled={busy}>
              {taxes.map((t) => <option key={t.idOne} value={t.idOne}>{t.name}</option>)}
            </select>
          </label>
          <label>Category
            <select value={catId} onChange={(e) => setCatId(e.target.value)} disabled={busy}>
              {cats.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
          </label>
        </div>
        <label className="chk">
          <input type="checkbox" checked={untracked} onChange={(e) => setUntracked(e.target.checked)} disabled={busy} />
          Don't track stock for this item
        </label>
        <p className="muted small">
          For things that are effectively unlimited — carrier bags, back-issues. Sales are still
          recorded and reported; only the stock count is skipped, and the item shows ∞ instead of a
          number. Turning it back on resumes from the existing ledger level.
        </p>
        <p className="muted small">Ex-VAT price: £{exPrice.toFixed(2)} (derived from the selected tax band; the server rejects a band mismatch)</p>

        {/* The item's change history — collapsed, at the bottom, so it is there when somebody asks
            "who changed this price?" without being in the way of an ordinary edit. */}
        {item && <ItemHistory itemIdOne={item.idOne} />}

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

        {error && <p className="error small">{error}</p>}
        <div className="dialog-actions">
          <button type="button" className="ghost" onClick={onClose} disabled={busy}>Cancel</button>
          {/* blocked while a clash is showing; editing the barcode clears it */}
          <button type="submit" className="primary" disabled={busy || !!clash || !id.trim() || !name.trim() || !price || !catId}>{busy ? "Saving…" : "Save"}</button>
        </div>
      </form>
    </div>
  );
}

