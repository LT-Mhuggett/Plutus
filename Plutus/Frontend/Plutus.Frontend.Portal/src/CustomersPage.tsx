import { useEffect, useState } from "react";
import { ApiError } from "./api.ts";
import { accessToken } from "./auth.ts";
import CustomerDialog from "./CustomerDialog.tsx";
import DataTable from "./DataTable.tsx";

// Phase 8 portal: the full customer book. The per-customer editor (details / credit / membership)
// is the shared CustomerDialog, also opened from the Loyalty tab (WP5.1). WP1.3: the list is now the
// standard DataTable (client mode — sort/search/paginate over the loaded set).

interface CustomerRow { id: string; name: string; email: string | null; phone: string | null }

async function j<T>(method: string, url: string, body?: unknown): Promise<T> {
  const token = accessToken();
  const res = await fetch(url, {
    method,
    headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(body !== undefined ? { "Content-Type": "application/json" } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!res.ok) {
    if (res.status === 403)
      throw new ApiError(403, "You need the ‘customers.manage’ permission to do this — ask an administrator.");
    let detail = `${res.status}`;
    try { detail = (await res.json())?.detail ?? detail; } catch { /* keep */ }
    throw new ApiError(res.status, detail);
  }
  return res.status === 204 ? (undefined as T) : res.json();
}

export default function CustomersPage() {
  const [rows, setRows] = useState<CustomerRow[]>([]);
  const [open, setOpen] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [form, setForm] = useState({ name: "", email: "", phone: "" });
  const [error, setError] = useState("");

  const refresh = () =>
    j<CustomerRow[]>("GET", `/api/v1/customers?take=500`)
      .then((r) => { setRows(r); setError(""); })
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));
  useEffect(() => { void refresh(); }, []); // eslint-disable-line react-hooks/exhaustive-deps

  async function submitCreate(e: React.FormEvent) {
    e.preventDefault();
    try {
      await j("POST", `/api/v1/customers`, { name: form.name, email: form.email || undefined, phone: form.phone || undefined });
      setCreating(false);
      setForm({ name: "", email: "", phone: "" });
      await refresh();
    } catch (err) {
      setError(String(err instanceof Error ? err.message : err));
    }
  }

  return (
    <section className="panel">
      <div className="toolbar" style={{ justifyContent: "space-between" }}>
        <h2>Customers</h2>
        <button className="primary" onClick={() => setCreating(true)}>Add customer</button>
      </div>
      {error && <p className="error">{error}</p>}

      <DataTable<CustomerRow>
        columns={[
          { key: "name", label: "Name" },
          { key: "email", label: "Email", render: (c) => c.email ?? "—" },
          { key: "phone", label: "Phone", render: (c) => c.phone ?? "—" },
        ]}
        rows={rows} getKey={(c) => c.id} initialSortKey="name"
        search={(c) => `${c.name} ${c.email ?? ""} ${c.phone ?? ""}`}
        searchPlaceholder="Search name / email / phone…"
        rowActions={(c) => <button className="ghost small" onClick={() => setOpen(c.id)}>Open</button>}
        emptyText="No customers."
      />

      {creating && (
        <div className="overlay" onClick={(e) => e.target === e.currentTarget && setCreating(false)}>
          <form className="dialog" onSubmit={submitCreate}>
            <h3>Add customer</h3>
            <label>Name <input required value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} /></label>
            <label>Email <input type="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} /></label>
            <label>Phone <input value={form.phone} onChange={(e) => setForm({ ...form, phone: e.target.value })} /></label>
            <div className="dialog-actions">
              <button type="button" className="ghost" onClick={() => setCreating(false)}>Cancel</button>
              <button className="primary">Create</button>
            </div>
          </form>
        </div>
      )}

      {open && <CustomerDialog id={open} onClose={() => { setOpen(null); void refresh(); }} />}
    </section>
  );
}
