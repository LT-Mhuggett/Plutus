import { useEffect, useState } from "react";
import DataTable from "./DataTable.tsx";
import { useNav } from "./nav.tsx";
import { ask } from "./Ask.tsx";
import {
  createCategory, deleteCategory, fetchCategories, reassignCategory, renameCategory,
  type Category,
} from "./api.ts";

// WP4.4 category manager (webstore-critical). The missing editor: category CRUD existed in the API
// but nothing consumed it. Delete is guarded server-side (409 while items reference it, or if it's
// the last category); this UI turns that into a "reassign, then delete" flow so no item is ever
// orphaned or cascade-deleted.

export default function CategoryManager() {
  const { go } = useNav();
  const [cats, setCats] = useState<Category[]>([]);
  const [error, setError] = useState("");
  const [editing, setEditing] = useState<Category | "new" | null>(null);
  const [reassign, setReassign] = useState<Category | null>(null);

  const load = () =>
    fetchCategories().then((c) => { setCats(c); setError(""); })
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));
  useEffect(() => { void load(); }, []);

  const onDelete = async (c: Category) => {
    if (c.itemCount > 0) { setReassign(c); return; }        // blocked → open the reassign flow
    if (!await ask.confirm({
      title: `Delete the category “${c.name}”?`,
      body: <p className="small">It holds no items, so nothing is affected.</p>,
      confirmLabel: "Delete category",
      danger: true,
    })) return;
    try { await deleteCategory(c.id); await load(); }
    catch (e) { setError(String(e instanceof Error ? e.message : e)); }
  };

  return (
    <section className="panel">
      <div className="toolbar">
        <h2 className="grow">Categories</h2>
        <button className="primary small" onClick={() => setEditing("new")}>+ New category</button>
      </div>
      <p className="muted small">
        Every item belongs to a category — the webstore groups products by it. Deleting a category is
        blocked while it still holds items; move them to another category first.
      </p>
      {error && <p className="error">{error}</p>}
      <DataTable<Category>
        columns={[
          { key: "name", label: "Category" },
          {
            // FE5.1: the count is the way in — click it to see those items, pre-filtered.
            key: "itemCount", label: "Items", numeric: true,
            render: (c) => c.itemCount > 0
              ? <button className="linklike" title={`Show the ${c.itemCount} items in ${c.name}`}
                  onClick={() => go("Inventory", `cat:${c.id}`)}>{c.itemCount}</button>
              : <span className="muted">0</span>,
          },
        ]}
        rows={cats} getKey={(c) => c.id} search={(c) => c.name} initialSortKey="name"
        rowActions={(c) => (
          <>
            {c.itemCount > 0 && (
              <><button className="ghost small" onClick={() => go("Inventory", `cat:${c.id}`)}>View items</button>{" "}</>
            )}
            <button className="ghost small" onClick={() => setEditing(c)}>Rename</button>{" "}
            <button className="ghost small" onClick={() => void onDelete(c)}>Delete</button>
          </>
        )}
        emptyText="No categories yet."
      />
      {editing && (
        <CategoryDialog
          cat={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onDone={() => { setEditing(null); void load(); }}
        />
      )}
      {reassign && (
        <ReassignDialog
          from={reassign} all={cats}
          onClose={() => setReassign(null)}
          onDone={() => { setReassign(null); void load(); }}
        />
      )}
    </section>
  );
}

function CategoryDialog({ cat, onClose, onDone }: { cat: Category | null; onClose: () => void; onDone: () => void }) {
  const [name, setName] = useState(cat?.name ?? "");
  const [description, setDescription] = useState(cat?.description ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true); setError("");
    try {
      if (cat) await renameCategory(cat.id, name.trim(), description.trim() || undefined);
      else await createCategory(name.trim(), description.trim() || undefined);
      onDone();
    } catch (err) { setError(String(err instanceof Error ? err.message : err)); setBusy(false); }
  };

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && !busy && onClose()}>
      <form className="dialog" onSubmit={submit}>
        <h3>{cat ? "Rename category" : "New category"}</h3>
        <div className="form-grid">
          <label>Name<input value={name} onChange={(e) => setName(e.target.value)} required disabled={busy} /></label>
          <label>Description (optional)<input value={description} onChange={(e) => setDescription(e.target.value)} disabled={busy} /></label>
        </div>
        {error && <p className="error small">{error}</p>}
        <div className="dialog-actions">
          <button type="button" className="ghost" onClick={onClose} disabled={busy}>Cancel</button>
          <button type="submit" className="primary" disabled={busy || !name.trim()}>{busy ? "Saving…" : "Save"}</button>
        </div>
      </form>
    </div>
  );
}

function ReassignDialog({ from, all, onClose, onDone }: { from: Category; all: Category[]; onClose: () => void; onDone: () => void }) {
  const others = all.filter((c) => c.id !== from.id);
  const [toId, setToId] = useState(others[0]?.id ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  const run = async (thenDelete: boolean) => {
    setBusy(true); setError("");
    try {
      await reassignCategory(from.id, toId);
      if (thenDelete) await deleteCategory(from.id);
      onDone();
    } catch (err) { setError(String(err instanceof Error ? err.message : err)); setBusy(false); }
  };

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && !busy && onClose()}>
      <div className="dialog">
        <h3>“{from.name}” holds {from.itemCount} item{from.itemCount === 1 ? "" : "s"}</h3>
        <p className="muted small">Move them to another category. You can keep “{from.name}” or delete it once it's empty.</p>
        {others.length === 0
          ? <p className="error small">There's no other category to move items into — create one first.</p>
          : (
            <label>Move items to{" "}
              <select value={toId} onChange={(e) => setToId(e.target.value)} disabled={busy}>
                {others.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </select>
            </label>
          )}
        {error && <p className="error small">{error}</p>}
        <div className="dialog-actions">
          <button className="ghost" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="ghost" disabled={busy || !toId} onClick={() => void run(false)}>Move only</button>
          <button className="primary" disabled={busy || !toId} onClick={() => void run(true)}>{busy ? "Working…" : "Move & delete"}</button>
        </div>
      </div>
    </div>
  );
}
