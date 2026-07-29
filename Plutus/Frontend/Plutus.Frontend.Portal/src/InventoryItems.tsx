import { useEffect, useMemo, useState } from "react";
import {
  createItem, fetchCatalogueItems, fetchCategories, fetchTaxes, gbp, updateItem,
  type CatalogueItem, type Category, type ItemInput, type Tax,
} from "./api.ts";

// WP4.2/4.3 portal item catalogue: add/edit items (name, brand, cost, price inc VAT with ex-VAT
// derived, tax band, category) — parity with the till's inventory dialog, reusing the guardrailed
// legacy api/Item so the VAT band check applies identically. The list is server-paged (the legacy
// Index has no total, so it's a Prev/Next pager, not the standard DataTable); the Category filter
// narrows the CURRENT page.

const PAGE_SIZE_DEFAULT = 25;

export default function InventoryItems() {
  const [items, setItems] = useState<CatalogueItem[]>([]);
  const [cats, setCats] = useState<Category[]>([]);
  const [page, setPage] = useState(1);
  const [size, setSize] = useState(PAGE_SIZE_DEFAULT);
  const [search, setSearch] = useState("");
  const [applied, setApplied] = useState("");
  const [catFilter, setCatFilter] = useState("");
  const [state, setState] = useState<"loading" | "ready" | "error">("loading");
  const [error, setError] = useState("");
  const [editing, setEditing] = useState<CatalogueItem | "new" | null>(null);
  const [notice, setNotice] = useState("");

  const catName = useMemo(() => new Map(cats.map((c) => [c.id, c.name])), [cats]);

  const load = () => {
    setState("loading");
    fetchCatalogueItems(page, size, applied)
      .then((data) => { setItems(data); setState("ready"); })
      .catch((e) => { setError(String(e instanceof Error ? e.message : e)); setState("error"); });
  };
  useEffect(load, [page, size, applied]);
  useEffect(() => { void fetchCategories().then(setCats).catch(() => undefined); }, []);

  const shown = catFilter ? items.filter((i) => i.catId === catFilter) : items;

  return (
    <section className="panel">
      <div className="toolbar">
        <h2 className="grow">Items</h2>
        <button className="primary small" onClick={() => setEditing("new")}>+ Add item</button>
      </div>
      <div className="toolbar">
        <input placeholder="Search name, barcode, brand…" value={search}
          onChange={(e) => setSearch(e.target.value)}
          onKeyDown={(e) => { if (e.key === "Enter") { setPage(1); setApplied(search.trim()); } }} />
        <button className="ghost small" onClick={() => { setPage(1); setApplied(search.trim()); }}>Search</button>
        <label>Category{" "}
          <select value={catFilter} onChange={(e) => setCatFilter(e.target.value)}>
            <option value="">All (this page)</option>
            {cats.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        </label>
        <label>Show{" "}
          <select value={size} onChange={(e) => { setSize(Number(e.target.value)); setPage(1); }}>
            <option value={25}>25</option><option value={50}>50</option><option value={100}>100</option>
          </select>
        </label>
      </div>

      {notice && <p className="small discount-note">{notice}</p>}
      {state === "error" && <p className="error">Could not load items: {error}</p>}
      {state === "loading" && <p className="muted">Loading…</p>}
      {state === "ready" && shown.length === 0 && <p className="muted">No items{applied ? ` matching “${applied}”` : ""}{catFilter ? " in this category on this page" : ""}.</p>}
      {state === "ready" && shown.length > 0 && (
        <table>
          <thead><tr>
            <th>Barcode / Id</th><th>Name</th><th>Brand</th><th>Category</th><th className="num">Price</th><th />
          </tr></thead>
          <tbody>
            {shown.map((i) => (
              <tr key={i.idOne}>
                <td className="mono small">{i.idOne}</td>
                <td>{i.name}</td>
                <td>{i.brand === "NOT EXIST" || i.brand === "-" ? "" : i.brand}</td>
                <td>{catName.get(i.catId) ?? "—"}</td>
                <td className="num">{gbp(Math.round(i.price * 100))}</td>
                <td><button className="ghost small" onClick={() => setEditing(i)}>Edit</button></td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <div className="toolbar">
        <button className="ghost small" disabled={page === 1 || state === "loading"} onClick={() => setPage((p) => Math.max(1, p - 1))}>← Prev</button>
        <span className="muted small">page {page}</span>
        <button className="ghost small" disabled={state === "loading" || items.length < size} onClick={() => setPage((p) => p + 1)}>Next →</button>
      </div>

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
