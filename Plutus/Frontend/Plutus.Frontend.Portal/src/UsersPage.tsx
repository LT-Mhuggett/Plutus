import { useEffect, useState } from "react";
import DataTable from "./DataTable.tsx";
import {
  assignRole, createUser, deactivateUser, fetchAssignments, fetchCompanies, fetchEffectivePermissions,
  fetchRoles, fetchUsers, unassignRole,
  type Assignment, type PortalUser, type Role,
} from "./api.ts";

/** Users & roles (WP3.1/3.2): who holds which role at which scope; the permission
 *  catalogue itself is fixed in code. */
export default function UsersPage() {
  const [users, setUsers] = useState<PortalUser[]>([]);
  const [roles, setRoles] = useState<Role[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [open, setOpen] = useState<PortalUser | null>(null);
  const [error, setError] = useState("");
  const [creating, setCreating] = useState(false);
  const [form, setForm] = useState({ fName: "", lName: "", email: "", password: "" });

  const refresh = () =>
    Promise.all([fetchUsers(), fetchRoles()])
      .then(([u, r]) => { setUsers(u); setRoles(r); })
      .catch((e) => setError(String(e)));

  useEffect(() => {
    void refresh();
    fetchCompanies().then((c) => setCompanyId(c[0]?.id ?? "")).catch(() => undefined);
  }, []);

  async function submitCreate(e: React.FormEvent) {
    e.preventDefault();
    setError("");
    try {
      await createUser({ ...form, password: form.password || undefined });
      setCreating(false);
      setForm({ fName: "", lName: "", email: "", password: "" });
      await refresh();
    } catch (err) {
      setError(String(err instanceof Error ? err.message : err));
    }
  }

  return (
    <section className="panel">
      <div className="toolbar">
        <h2 className="grow">Users</h2>
        <button className="primary" onClick={() => setCreating(true)}>Add user</button>
      </div>
      {error && <p className="error">{error}</p>}

      <DataTable<PortalUser>
        columns={[
          { key: "fName", label: "Name", render: (u) => <span className={u.active ? undefined : "muted"}>{u.fName} {u.lName}</span> },
          { key: "email", label: "Email" },
          { key: "roles", label: "Roles", sortable: false, render: (u) => u.roles.join(", ") || "—" },
          { key: "active", label: "Status", render: (u) => (u.active ? "active" : <span className="muted">deactivated</span>) },
        ]}
        rows={users} getKey={(u) => u.id} initialSortKey="fName"
        search={(u) => `${u.fName} ${u.lName} ${u.email} ${u.roles.join(" ")}`}
        searchPlaceholder="Search name / email / role…"
        rowActions={(u) => (
          <>
            <button className="ghost small" onClick={() => setOpen(u)}>Roles</button>{" "}
            {u.active && (
              <button className="ghost small" onClick={() => void deactivateUser(u.id).then(refresh)}>Deactivate</button>
            )}
          </>
        )}
        emptyText="No users."
      />

      {creating && (
        <div className="overlay" onClick={(e) => e.target === e.currentTarget && setCreating(false)}>
          <form className="dialog" onSubmit={submitCreate}>
            <h3>Add user</h3>
            <label>First name <input required value={form.fName} onChange={(e) => setForm({ ...form, fName: e.target.value })} /></label>
            <label>Last name <input value={form.lName} onChange={(e) => setForm({ ...form, lName: e.target.value })} /></label>
            <label>Email <input type="email" required value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} /></label>
            <label>Password (optional — sets a web login) <input type="password" minLength={8} value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} /></label>
            <div className="dialog-actions">
              <button type="button" className="ghost" onClick={() => setCreating(false)}>Cancel</button>
              <button className="primary">Create</button>
            </div>
          </form>
        </div>
      )}

      {open && <RolesDialog user={open} roles={roles} companyId={companyId} onClose={() => { setOpen(null); void refresh(); }} />}
    </section>
  );
}

function RolesDialog({ user, roles, companyId, onClose }: { user: PortalUser; roles: Role[]; companyId: string; onClose: () => void }) {
  const [assignments, setAssignments] = useState<Assignment[]>([]);
  const [effective, setEffective] = useState<string[]>([]);
  const [roleId, setRoleId] = useState(roles[0]?.id ?? "");
  const [error, setError] = useState("");

  const refresh = () =>
    Promise.all([
      fetchAssignments(user.id),
      companyId ? fetchEffectivePermissions(user.id, `company:${companyId}`) : Promise.resolve({ permissions: [] }),
    ])
      .then(([a, p]) => { setAssignments(a); setEffective(p.permissions.map((x) => x.display)); })
      .catch((e) => setError(String(e)));

  useEffect(() => { void refresh(); }, [user.id]); // eslint-disable-line react-hooks/exhaustive-deps

  async function add() {
    setError("");
    try {
      await assignRole(user.id, roleId, companyId ? `company:${companyId}` : "tenant");
      await refresh();
    } catch (e) {
      setError(String(e instanceof Error ? e.message : e));
    }
  }

  // Only offer roles the user does NOT already hold at the scope we'd assign into (company, or
  // tenant when no company) — no duplicate assignments. Scope match is case-insensitive.
  const targetType = companyId ? "company" : "tenant";
  const targetId = (companyId ?? "").toLowerCase();
  const heldRoleIds = new Set(
    assignments
      .filter((a) => a.scopeType.toLowerCase() === targetType && (a.scopeId ?? "").toLowerCase() === targetId)
      .map((a) => a.roleId),
  );
  const available = roles.filter((r) => !heldRoleIds.has(r.id));

  // Keep the selection valid as assignments change (after adding one it drops off the list).
  useEffect(() => {
    setRoleId((prev) => (available.some((r) => r.id === prev) ? prev : available[0]?.id ?? ""));
  }, [assignments, roles]); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog">
        <h3>{user.fName} {user.lName} — roles</h3>
        {error && <p className="error small">{error}</p>}

        <table>
          <thead><tr><th>Role</th><th>Scope</th><th>Window</th><th /></tr></thead>
          <tbody>
            {assignments.map((a) => (
              <tr key={a.id}>
                <td>{a.roleName}</td>
                <td>{a.scopeType}{a.scopeId ? `:${a.scopeId.slice(0, 8)}` : ""}</td>
                <td>{a.windowStartLocal ? `${a.windowStartLocal}–${a.windowEndLocal}` : "always"}</td>
                <td><button className="ghost small" onClick={() => void unassignRole(user.id, a.id).then(refresh)}>Remove</button></td>
              </tr>
            ))}
            {assignments.length === 0 && <tr><td colSpan={4} className="muted">No role assignments.</td></tr>}
          </tbody>
        </table>

        <div className="toolbar">
          <select value={roleId} onChange={(e) => setRoleId(e.target.value)} disabled={available.length === 0}>
            {available.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
          </select>
          <button className="primary small" disabled={!roleId} onClick={() => void add()}>Assign at company scope</button>
          {available.length === 0 && <span className="muted small">All roles already assigned at this scope.</span>}
        </div>

        <h4>Effective permissions (company scope)</h4>
        <p className="small mono">{effective.join("  ") || "none"}</p>

        <div className="dialog-actions">
          <button className="ghost" onClick={onClose}>Close</button>
        </div>
      </div>
    </div>
  );
}
