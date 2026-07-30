import { useEffect, useMemo, useState } from "react";
import {
  createItem,
  createStock,
  fetchCategories,
  fetchItemsPaged,
  fetchTaxes,
  updateItem,
  type Category,
  type Item,
  type ItemInput,
  type Tax,
} from "./api.ts";
import { gbp } from "./money.ts";
import DataTable from "./DataTable.tsx";

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
  const [notice, setNotice] = useState("");

  // WP4.3: show each item's category in the list (the dialog already assigns it; the list didn't).
  const catName = useMemo(() => new Map(cats.map((c) => [c.idOne, c.name])), [cats]);
  useEffect(() => { void fetchCategories().then(setCats).catch(() => undefined); }, []);

  function load() {
    setState("loading");
    // the legacy endpoint pages by 1-based page number, DataTable thinks in skip/take
    fetchItemsPaged(Math.floor(skip / take) + 1, take, search, catFilter)
      .then(({ rows, total: n }) => {
        setItems(rows);
        setTotal(n ?? skip + rows.length + (rows.length === take ? take : 0)); // pre-FE4.2 fallback
        setState("ready");
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
        <button className="ghost" onClick={() => setEditing("new")}>
          Add item
        </button>
        <label className="small">Category{" "}
          <select value={catFilter} onChange={(e) => { setSkip(0); setCatFilter(e.target.value); }}>
            <option value="">All</option>
            {cats.map((c) => <option key={c.idOne} value={c.idOne}>{c.name}</option>)}
          </select>
        </label>
      </div>

      {notice && <p className="small discount-note">{notice}</p>}
      {state === "error" && <p className="error">Could not load inventory: {error}</p>}
      <DataTable<Item>
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
        emptyText={state === "loading" ? "Loading…" : `No items${search ? ` matching “${search}”` : ""}.`}
      />

      {editing && (
        <ItemDialog
          item={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onDone={(msg) => {
            setEditing(null);
            setNotice(msg);
            load();
          }}
        />
      )}
    </section>
  );
}

function ItemDialog({ item, onClose, onDone }: { item: Item | null; onClose: () => void; onDone: (msg: string) => void }) {
  const [taxes, setTaxes] = useState<Tax[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [id, setId] = useState(item?.idOne ?? "");
  const [name, setName] = useState(item?.name ?? "");
  const [brand, setBrand] = useState(item?.brand === "-" ? "" : (item?.brand ?? ""));
  const [desc, setDesc] = useState(item?.desc ?? "");
  const [cost, setCost] = useState(item ? String(item.cost) : "");
  const [price, setPrice] = useState(item ? String(item.price) : "");
  const [taxId, setTaxId] = useState<number>(item?.taxId ?? 0);
  const [catId, setCatId] = useState("");
  const [stock, setStock] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

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
      <form className="dialog" onSubmit={submit}>
        <h2>{item ? "Edit item" : "Add item"}</h2>
        <div className="form-grid">
          <label>
            Barcode / id (max 20)
            <input value={id} onChange={(e) => setId(e.target.value)} maxLength={20} required disabled={busy || !!item} />
          </label>
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
          {!item && (
            <label>
              Initial stock (optional)
              <input inputMode="numeric" value={stock} onChange={(e) => setStock(e.target.value)} disabled={busy} />
            </label>
          )}
        </div>
        <p className="muted small">Ex-tax price: £{exPrice.toFixed(2)} (derived from the selected tax)</p>
        {error && <p className="error small">{error}</p>}
        <div className="dialog-actions">
          <button type="button" className="ghost" onClick={onClose} disabled={busy}>
            Cancel
          </button>
          <button type="submit" className="primary" disabled={busy || !id.trim() || !name.trim() || !price}>
            {busy ? "Saving…" : "Save"}
          </button>
        </div>
      </form>
    </div>
  );
}
