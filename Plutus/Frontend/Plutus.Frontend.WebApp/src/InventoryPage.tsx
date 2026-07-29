import { useEffect, useMemo, useState } from "react";
import {
  createItem,
  createStock,
  fetchCategories,
  fetchItems,
  fetchTaxes,
  updateItem,
  type Category,
  type Item,
  type ItemInput,
  type Tax,
} from "./api.ts";
import { gbp } from "./money.ts";

const PAGE_SIZE = 25;

export default function InventoryPage() {
  const [items, setItems] = useState<Item[]>([]);
  const [cats, setCats] = useState<Category[]>([]);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [applied, setApplied] = useState("");
  const [state, setState] = useState<"loading" | "ready" | "error">("loading");
  const [error, setError] = useState("");
  const [editing, setEditing] = useState<Item | "new" | null>(null);
  const [notice, setNotice] = useState("");

  // WP4.3: show each item's category in the list (the dialog already assigns it; the list didn't).
  const catName = useMemo(() => new Map(cats.map((c) => [c.idOne, c.name])), [cats]);
  useEffect(() => { void fetchCategories().then(setCats).catch(() => undefined); }, []);

  function load() {
    setState("loading");
    fetchItems(page, PAGE_SIZE, applied)
      .then((data) => {
        setItems(data);
        setState("ready");
      })
      .catch((e) => {
        setError(String(e));
        setState("error");
      });
  }

  useEffect(load, [page, applied]);

  return (
    <section className="panel">
      <div className="panel-head">
        <h2>Inventory</h2>
        <button className="ghost" onClick={() => setEditing("new")}>
          Add item
        </button>
        <input
          className="search"
          placeholder="Search name, barcode, brand…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              setPage(1);
              setApplied(search.trim());
            }
          }}
        />
        <nav className="pager">
          <button onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page === 1 || state === "loading"}>
            ‹ Prev
          </button>
          <span>page {page}</span>
          <button onClick={() => setPage((p) => p + 1)} disabled={state === "loading" || items.length < PAGE_SIZE}>
            Next ›
          </button>
        </nav>
      </div>

      {notice && <p className="small discount-note">{notice}</p>}
      {state === "error" && <p className="error">Could not load inventory: {error}</p>}
      {state === "loading" && <p className="muted">Loading…</p>}
      {state === "ready" && items.length === 0 && <p className="muted">No items{applied ? ` matching “${applied}”` : ""}.</p>}
      {state === "ready" && items.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>Barcode / Id</th>
              <th>Name</th>
              <th>Brand</th>
              <th>Category</th>
              <th className="num">Price</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {items.map((i) => (
              <tr key={i.idOne}>
                <td className="mono">{i.idOne}</td>
                <td>{i.name}</td>
                <td>{i.brand === "NOT EXIST" || i.brand === "-" ? "" : i.brand}</td>
                <td>{catName.get(i.catId) ?? "—"}</td>
                <td className="num">{gbp(Math.round(i.price * 100))}</td>
                <td>
                  <button className="ghost small" onClick={() => setEditing(i)}>
                    Edit
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

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
