import { useEffect, useMemo, useState } from "react";
import {
  createItem, fetchCatalogueItemsPaged, fetchCategories, fetchTaxes, gbp, updateItem,
  type CatalogueItem, type Category, type ItemInput, type Tax,
} from "./api.ts";
import DataTable from "./DataTable.tsx";

// WP4.2/4.3 portal item catalogue: add/edit items (name, brand, cost, price inc VAT with ex-VAT
// derived, tax band, category) — parity with the till's inventory dialog, reusing the guardrailed
// legacy api/Item so the VAT band check applies identically. FE5.0: the Category filter is
// SERVER-side (it used to narrow only the fetched page, usually showing nothing). FE4.3: the list
// is now the standard DataTable in SERVER mode — 20k+ items stay server-windowed, and FE4.2's
// X-Pagination total turns the old "page N" guess into a true "X–Y of N".

export default function InventoryItems() {
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

  const catName = useMemo(() => new Map(cats.map((c) => [c.id, c.name])), [cats]);

  const load = () => {
    setState("loading");
    fetchCatalogueItemsPaged(Math.floor(skip / take) + 1, take, search, catFilter)
      .then(({ rows, total: n }) => {
        setItems(rows);
        setTotal(n ?? skip + rows.length + (rows.length === take ? take : 0)); // pre-FE4.2 fallback
        setState("ready");
      })
      .catch((e) => { setError(String(e instanceof Error ? e.message : e)); setState("error"); });
  };
  // debounced so typing in the search box doesn't fire a request per keystroke
  useEffect(() => {
    const t = setTimeout(load, search ? 300 : 0);
    return () => clearTimeout(t);
  }, [skip, take, search, catFilter]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { void fetchCategories().then(setCats).catch(() => undefined); }, []);

  return (
    <section className="panel">
      <div className="toolbar">
        <h2 className="grow">Items</h2>
        <label>Category{" "}
          <select value={catFilter} onChange={(e) => { setSkip(0); setCatFilter(e.target.value); }}>
            <option value="">All</option>
            {cats.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        </label>
        <button className="primary small" onClick={() => setEditing("new")}>+ Add item</button>
      </div>

      {notice && <p className="small discount-note">{notice}</p>}
      {state === "error" && <p className="error">Could not load items: {error}</p>}
      <DataTable<CatalogueItem>
        columns={[
          { key: "idOne", label: "Barcode / Id", render: (i) => <span className="mono small">{i.idOne}</span> },
          { key: "name", label: "Name" },
          { key: "brand", label: "Brand", render: (i) => (i.brand === "NOT EXIST" || i.brand === "-" ? "" : i.brand) },
          { key: "catId", label: "Category", render: (i) => catName.get(i.catId) ?? "—" },
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
        rowActions={(i) => <button className="ghost small" onClick={() => setEditing(i)}>Edit</button>}
        emptyText={state === "loading" ? "Loading…"
          : `No items${search ? ` matching “${search}”` : ""}${catFilter ? ` in ${catName.get(catFilter) ?? "this category"}` : ""}.`}
      />

      {editing && (
        <ItemDialog
          item={editing === "new" ? null : editing} cats={cats}
          onClose={() => setEditing(null)}
          onDone={(msg) => { setEditing(null); setNotice(msg); load(); }}
        />
      )}
    </section>
  );
}

function ItemDialog({ item, cats, onClose, onDone }:
  { item: CatalogueItem | null; cats: Category[]; onClose: () => void; onDone: (msg: string) => void }) {
  const [taxes, setTaxes] = useState<Tax[]>([]);
  const [id, setId] = useState(item?.idOne ?? "");
  const [name, setName] = useState(item?.name ?? "");
  const [brand, setBrand] = useState(item?.brand === "-" ? "" : (item?.brand ?? ""));
  const [desc, setDesc] = useState(item?.desc ?? "");
  const [cost, setCost] = useState(item ? String(item.cost) : "");
  const [price, setPrice] = useState(item ? String(item.price) : "");
  const [taxId, setTaxId] = useState<number>(item?.taxId ?? 0);
  const [catId, setCatId] = useState(item?.catId ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

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
    const input: ItemInput = {
      id: id.trim(), name: name.trim(), brand: brand.trim(), desc: desc.trim(),
      cost: parseFloat(cost) || 0, price: priceNum, exPrice: Math.round(exPrice * 100) / 100, taxId, catId,
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
        <div className="form-grid">
          <label>Barcode / id (max 20)<input value={id} onChange={(e) => setId(e.target.value)} maxLength={20} required disabled={busy || !!item} /></label>
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
        <p className="muted small">Ex-VAT price: £{exPrice.toFixed(2)} (derived from the selected tax band; the server rejects a band mismatch)</p>
        {error && <p className="error small">{error}</p>}
        <div className="dialog-actions">
          <button type="button" className="ghost" onClick={onClose} disabled={busy}>Cancel</button>
          <button type="submit" className="primary" disabled={busy || !id.trim() || !name.trim() || !price || !catId}>{busy ? "Saving…" : "Save"}</button>
        </div>
      </form>
    </div>
  );
}
