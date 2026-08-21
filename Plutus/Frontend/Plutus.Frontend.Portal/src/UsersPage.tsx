import { useEffect, useMemo, useState } from "react";
import DataTable from "./DataTable.tsx";
import {
  assignRole, createUser, fetchAssignments, fetchCompanies, fetchEffectivePermissions,
  fetchPermissions, fetchRoles, fetchUserAudit, fetchUsers, removeUser, restoreUser,
  sendPasswordReset, setUserPassword, unassignRole,
  type Assignment, type AuditRow, type PermissionInfo, type PortalUser, type Role,
} from "./api.ts";
import DialogX from "./DialogX.tsx";
import { apiDateTime, apiDay, apiMs } from "./apiTime.ts";

/** Users & roles (WP3.1/3.2 + FE9): who holds which role at which scope, what each role actually
 *  grants, and per-user access. The permission catalogue itself is fixed in code — this surface
 *  explains and assigns it. FE9 adds password set/reset, guarded removal, the roles reference and
 *  the collapsed per-user access matrix. */
export default function UsersPage() {
  const [users, setUsers] = useState<PortalUser[]>([]);
  const [roles, setRoles] = useState<Role[]>([]);
  const [perms, setPerms] = useState<PermissionInfo[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [open, setOpen] = useState<PortalUser | null>(null);
  const [pwFor, setPwFor] = useState<PortalUser | null>(null);
  const [removeFor, setRemoveFor] = useState<PortalUser | null>(null);
  const [activityFor, setActivityFor] = useState<PortalUser | null>(null);
  const [includeRemoved, setIncludeRemoved] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [creating, setCreating] = useState(false);
  const [form, setForm] = useState({ fName: "", lName: "", email: "", password: "" });

  const refresh = () =>
    Promise.all([fetchUsers(includeRemoved), fetchRoles()])
      .then(([u, r]) => { setUsers(u); setRoles(r); })
      .catch((e) => setError(String(e)));

  useEffect(() => { void refresh(); }, [includeRemoved]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => {
    fetchCompanies().then((c) => setCompanyId(c[0]?.id ?? "")).catch(() => undefined);
    fetchPermissions().then(setPerms).catch(() => undefined);
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

  const act = async (p: Promise<unknown>, done: string) => {
    setError(""); setNotice("");
    try { await p; setNotice(done); await refresh(); }
    catch (e) { setError(String(e instanceof Error ? e.message : e)); }
  };

  const ago = (iso: string | null) => {
    if (!iso) return <span className="muted">never</span>;
    const days = Math.floor((Date.now() - apiMs(iso)) / 86400_000);
    const label = apiDay(iso);
    // dormant accounts are the ones worth removing — flag them
    return <span className="small" title={apiDateTime(iso)}>
      {label}{days >= 90 && <span className="muted"> ({days}d)</span>}
    </span>;
  };

  return (
    <section className="panel">
      <div className="toolbar">
        <h2 className="grow">Users</h2>
        <label className="chk">
          <input type="checkbox" checked={includeRemoved} onChange={(e) => setIncludeRemoved(e.target.checked)} />
          Show removed
        </label>
        <button className="primary" onClick={() => setCreating(true)}>Add user</button>
      </div>
      {error && <p className="error">{error}</p>}
      {notice && <p className="callout small">{notice}</p>}

      <DataTable<PortalUser>
        columns={[
          { key: "fName", label: "Name", render: (u) => <span className={u.active ? undefined : "muted"}>{u.fName} {u.lName}</span> },
          { key: "email", label: "Email" },
          { key: "roles", label: "Roles", sortable: false, render: (u) => u.roles.join(", ") || "—" },
          {
            key: "hasLogin", label: "Login",
            render: (u) => u.hasLogin ? <span className="chip ok">yes</span> : <span className="chip" title="Staff record with no web login yet — send an invite">none</span>,
          },
          { key: "lastLoginAtUtc", label: "Last login", render: (u) => ago(u.lastLoginAtUtc) },
          { key: "active", label: "Status", render: (u) => (u.active ? "active" : <span className="muted">removed</span>) },
        ]}
        rows={users} getKey={(u) => u.id} initialSortKey="fName"
        search={(u) => `${u.fName} ${u.lName} ${u.email} ${u.roles.join(" ")}`}
        searchPlaceholder="Search name / email / role…"
        rowActions={(u) => u.active ? (
          <>
            <button className="ghost small" onClick={() => setOpen(u)}>Access</button>{" "}
            <button className="ghost small" onClick={() => setPwFor(u)}>Password</button>{" "}
            <button className="ghost small" onClick={() => setActivityFor(u)}>Activity</button>{" "}
            <button className="ghost small" onClick={() => setRemoveFor(u)}>Remove</button>
          </>
        ) : (
          <>
            {/* a removed user's trail is exactly what you want to read afterwards — keep it reachable */}
            <button className="ghost small" onClick={() => setActivityFor(u)}>Activity</button>{" "}
            <button className="ghost small" onClick={() => void act(restoreUser(u.id), `${u.fName} restored — re-grant their roles and a login.`)}>Restore</button>
          </>
        )}
        emptyText="No users."
      />

      <RolesReference roles={roles} perms={perms} />

      {creating && (
        <div className="overlay" onClick={(e) => e.target === e.currentTarget && setCreating(false)}>
          <form className="dialog" onSubmit={submitCreate}>
            <DialogX onClose={() => setCreating(false)} />
            <h3>Add user</h3>
            <label>First name <input required value={form.fName} onChange={(e) => setForm({ ...form, fName: e.target.value })} /></label>
            <label>Last name <input value={form.lName} onChange={(e) => setForm({ ...form, lName: e.target.value })} /></label>
            <label>Email <input type="email" required value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} /></label>
            <label>Password (optional — sets a web login) <input type="password" minLength={8} value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} /></label>
            <p className="muted small">Leave the password blank and use <strong>Password → Send link</strong> afterwards to let them choose their own.</p>
            <div className="dialog-actions">
              <button type="button" className="ghost" onClick={() => setCreating(false)}>Cancel</button>
              <button className="primary">Create</button>
            </div>
          </form>
        </div>
      )}

      {pwFor && <PasswordDialog user={pwFor} onClose={() => setPwFor(null)} onDone={(m) => { setPwFor(null); setNotice(m); void refresh(); }} />}
      {removeFor && (
        <RemoveDialog
          user={removeFor}
          onClose={() => setRemoveFor(null)}
          onDone={(m) => { setRemoveFor(null); setNotice(m); void refresh(); }}
          onShowActivity={() => { const u = removeFor; setRemoveFor(null); setActivityFor(u); }}
        />
      )}
      {activityFor && <ActivityDialog user={activityFor} onClose={() => setActivityFor(null)} />}
      {open && <AccessDialog user={open} roles={roles} perms={perms} companyId={companyId} onClose={() => { setOpen(null); void refresh(); }} />}
    </section>
  );
}

/** FE9.1: set a password directly, or email a single-use link (an invite when they have no login). */
function PasswordDialog({ user, onClose, onDone }:
  { user: PortalUser; onClose: () => void; onDone: (msg: string) => void }) {
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  const run = async (p: Promise<unknown>, done: string) => {
    setBusy(true); setError("");
    try { await p; onDone(done); }
    catch (e) { setError(String(e instanceof Error ? e.message : e)); setBusy(false); }
  };

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && !busy && onClose()}>
      <div className="dialog">
        <DialogX onClose={onClose} disabled={busy} />
        <h3>{user.fName} {user.lName} — password</h3>
        {!user.hasLogin && (
          <p className="callout small">
            This person has no web login yet. Either set a password for them, or send an invite so they choose their own.
          </p>
        )}

        <h4>Email a link</h4>
        <p className="muted small">
          Sends a single-use link, valid 48 hours, to <strong>{user.email}</strong>. Any previous link — and any
          password you set below — stops working.
        </p>
        <div className="toolbar">
          <button className="ghost" disabled={busy} onClick={async () => {
            setBusy(true); setError("");
            try {
              const r = await sendPasswordReset(user.id);
              // The API tells us whether the mailer actually accepted it. No Email provider is
              // configured yet on this platform, so be explicit rather than implying it arrived.
              onDone(r.sent
                ? `${r.isInvite ? "Invite" : "Reset link"} emailed to ${user.email} — valid 48 hours, single use.`
                : `⚠ Link created but NOT emailed: no Email provider is enabled (Platform → Notifications). ` +
                  `Nothing reached ${user.email} — set a password directly instead and tell them out of band.`);
            } catch (e) {
              setError(String(e instanceof Error ? e.message : e)); setBusy(false);
            }
          }}>
            {user.hasLogin ? "Send reset link" : "Send invite"}
          </button>
        </div>

        <h4>Or set it directly</h4>
        <p className="muted small">
          Give them a temporary password to change themselves. Note: existing sign-ins keep working
          until their 12-hour token expires — a password change stops the NEXT sign-in, it doesn't
          eject a live session.
        </p>
        <div className="toolbar">
          <input type="password" minLength={8} placeholder="at least 8 characters" value={password}
            onChange={(e) => setPassword(e.target.value)} disabled={busy} />
          <button className="primary" disabled={busy || password.length < 8} onClick={() => void run(
            setUserPassword(user.id, password), `Password set for ${user.fName}.`)}>
            Set password
          </button>
        </div>

        {error && <p className="error small">{error}</p>}
        <div className="dialog-actions"><button className="ghost" onClick={onClose} disabled={busy}>Close</button></div>
      </div>
    </div>
  );
}

/** FE9.2: the guardrail — typing the person's name arms the button, and the dialog spells out
 *  exactly what removal does (and what it deliberately does NOT do). */
function RemoveDialog({ user, onClose, onDone, onShowActivity }:
  { user: PortalUser; onClose: () => void; onDone: (msg: string) => void; onShowActivity: () => void }) {
  const [typed, setTyped] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const fullName = `${user.fName} ${user.lName}`.trim();
  const armed = typed.trim().toLowerCase() === fullName.toLowerCase();

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && !busy && onClose()}>
      <div className="dialog">
        <DialogX onClose={onClose} disabled={busy} />
        <h3>Remove {fullName}?</h3>
        <p className="small">This will:</p>
        <ul className="small">
          <li>revoke their web login{user.hasLogin ? "" : " (they don't have one)"}</li>
          <li>remove <strong>all {user.roles.length} role{user.roles.length === 1 ? "" : "s"}</strong>, so they lose every permission</li>
          <li>hide them from user lists and pickers</li>
        </ul>
        <p className="small">
          Their record is <strong>kept</strong>, so past sales, reports and the audit trail still show who did what —
          nothing is deleted. You can restore them later (roles and login are re-granted deliberately, not automatically).
        </p>
        <p className="small">
          Not sure? <button type="button" className="ghost small" onClick={onShowActivity}>Review their activity</button>{" "}
          first — it lists every setting, price and role they have changed.
        </p>
        <label>Type <strong>{fullName}</strong> to confirm
          <input value={typed} onChange={(e) => setTyped(e.target.value)} disabled={busy} autoFocus />
        </label>
        {error && <p className="error small">{error}</p>}
        <div className="dialog-actions">
          <button className="ghost" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="primary" disabled={!armed || busy} onClick={async () => {
            setBusy(true); setError("");
            try {
              const r = await removeUser(user.id);
              onDone(`${fullName} removed — ${r.rolesRemoved} role(s) dropped${r.loginRevoked ? ", login revoked" : ""}. History kept.`);
            } catch (e) {
              setError(String(e instanceof Error ? e.message : e)); setBusy(false);
            }
          }}>
            Remove user
          </button>
        </div>
      </div>
    </div>
  );
}

/** FE9.5: this person's audit slice — the last 200 things they changed. Deliberately reached from
 *  the Remove dialog too: "is this dormant account safe to remove?" is really "what did they touch?".
 *  Only ADMIN actions are audited (role grants, price overrides, bulk edits…), not ordinary sales —
 *  so an empty list means "changed no settings", not "did no work". The copy says so. */
function ActivityDialog({ user, onClose }: { user: PortalUser; onClose: () => void }) {
  const [rows, setRows] = useState<AuditRow[] | null>(null);
  const [error, setError] = useState("");
  const fullName = `${user.fName} ${user.lName}`.trim();

  useEffect(() => {
    fetchUserAudit(user.id, 200)
      .then(setRows)
      .catch((e) => { setError(String(e instanceof Error ? e.message : e)); setRows([]); });
  }, [user.id]);

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog wide">
        <DialogX onClose={onClose} />
        <h3>Activity — {fullName}</h3>
        <p className="muted small">
          Administrative changes only (roles, prices, inventory edits, tills, settings). Everyday
          selling is in the sales reports, not here — an empty list means they changed no settings.
        </p>
        {error && <p className="error small">{error}</p>}
        {rows === null ? <p className="muted">Loading…</p> : (
          <DataTable<AuditRow>
            columns={[
              {
                key: "atUtc", label: "When",
                render: (a) => <span className="small" title={a.atUtc}>{apiDateTime(a.atUtc)}</span>,
              },
              { key: "action", label: "Action", render: (a) => <span className="mono small">{a.action}</span> },
              { key: "entityType", label: "On", render: (a) => a.entityType ?? "—" },
              {
                key: "entityId", label: "Which", sortable: false,
                render: (a) => <span className="mono small">{a.entityId ?? "—"}</span>,
              },
              {
                key: "detailJson", label: "Detail", sortable: false,
                // the JSON is the prior/new values the writer chose to record — shown raw on purpose,
                // because a per-action pretty-printer would hide the fields that matter in a dispute
                render: (a) => a.detailJson
                  ? <span className="mono small" style={{ wordBreak: "break-all" }}>{a.detailJson}</span>
                  : <span className="muted">—</span>,
              },
            ]}
            rows={rows} getKey={(a) => String(a.id)} initialSortKey="atUtc" initialSortDir="desc"
            search={(a) => `${a.action} ${a.entityType ?? ""} ${a.entityId ?? ""} ${a.detailJson ?? ""}`}
            searchPlaceholder="Search action / entity / detail…"
            emptyText="No administrative changes recorded for this user."
          />
        )}
        <div className="dialog-actions"><button className="ghost" onClick={onClose}>Close</button></div>
      </div>
    </div>
  );
}

/** FE9.3: "what does each role give access to?" — one collapsed row per role, expanding to its
 *  grants grouped by surface, each with a plain-English description. Read-only: built-in roles are
 *  seeder-managed. */
function RolesReference({ roles, perms }: { roles: Role[]; perms: PermissionInfo[] }) {
  const groups = useMemo(() => {
    const set = new Set(perms.map((p) => p.group));
    for (const r of roles) for (const g of r.grants) set.add(g.group);
    return [...set].sort();
  }, [roles, perms]);

  return (
    <>
      <h3>Roles &amp; what they grant</h3>
      <p className="muted small">
        Reference — built-in roles are defined in Plutus and kept in step automatically. A role's
        permissions apply wherever the person signs in (portal or till) unless the permission is
        surface-specific.
      </p>
      {roles.length === 0 && <p className="muted">No roles.</p>}
      {roles.map((r) => (
        <details key={r.id} className="card">
          <summary>
            <strong>{r.name}</strong>{" "}
            {r.isBuiltIn && <span className="chip">built-in</span>}{" "}
            <span className="muted small">
              {r.memberCount} member{r.memberCount === 1 ? "" : "s"} · {r.grants.length} permission{r.grants.length === 1 ? "" : "s"}
            </span>
          </summary>
          {groups.filter((g) => r.grants.some((x) => x.group === g)).map((g) => (
            <div key={g}>
              <h4 className="small">{g}</h4>
              <table>
                <tbody>
                  {r.grants.filter((x) => x.group === g).map((x) => (
                    <tr key={x.code}>
                      <td className="mono small" style={{ whiteSpace: "nowrap" }}>{x.code}</td>
                      <td className="small">
                        {x.description}
                        {x.maxPence != null && <strong> Capped at £{(x.maxPence / 100).toFixed(2)}.</strong>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ))}
          {r.grants.length === 0 && <p className="muted small">This role grants nothing.</p>}
        </details>
      ))}
    </>
  );
}

/** FE9.4: one user's access — role assignments, plus a grouped matrix of what they can actually do
 *  and WHICH ROLE granted it ("why can Dave refund?"). Collapsed by default. */
function AccessDialog({ user, roles, perms, companyId, onClose }:
  { user: PortalUser; roles: Role[]; perms: PermissionInfo[]; companyId: string; onClose: () => void }) {
  const [assignments, setAssignments] = useState<Assignment[]>([]);
  const [effective, setEffective] = useState<{ code: string; maxPence: number | null }[]>([]);
  const [roleId, setRoleId] = useState(roles[0]?.id ?? "");
  const [error, setError] = useState("");

  const refresh = () =>
    Promise.all([
      fetchAssignments(user.id),
      companyId ? fetchEffectivePermissions(user.id, `company:${companyId}`) : Promise.resolve({ permissions: [] }),
    ])
      .then(([a, p]) => {
        setAssignments(a);
        setEffective(p.permissions.map((x) => ({ code: x.code, maxPence: x.maxPence })));
      })
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

  // FE9.4 attribution: which of the user's assigned roles grants each effective permission.
  const heldRoles = useMemo(
    () => roles.filter((r) => assignments.some((a) => a.roleId === r.id)),
    [roles, assignments],
  );
  const grantedBy = (code: string) =>
    heldRoles.filter((r) => r.grants.some((g) => g.code === code)).map((r) => r.name);

  const effectiveCodes = new Set(effective.map((e) => e.code));
  const groups = [...new Set(perms.map((p) => p.group))].sort();

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog">
        <DialogX onClose={onClose} />
        <h3>{user.fName} {user.lName} — access</h3>
        {error && <p className="error small">{error}</p>}

        <h4>Roles held</h4>
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
            {assignments.length === 0 && <tr><td colSpan={4} className="muted">No role assignments — this person can't do anything yet.</td></tr>}
          </tbody>
        </table>

        <div className="toolbar">
          <select value={roleId} onChange={(e) => setRoleId(e.target.value)} disabled={available.length === 0}>
            {available.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
          </select>
          <button className="primary small" disabled={!roleId} onClick={() => void add()}>Assign at company scope</button>
          {available.length === 0 && <span className="muted small">All roles already assigned at this scope.</span>}
        </div>

        {/* collapsed by default, per the FE9.4 requirement */}
        <details>
          <summary>
            <strong>What {user.fName} can do</strong>{" "}
            <span className="muted small">{effective.length} permission{effective.length === 1 ? "" : "s"} at company scope</span>
          </summary>
          <p className="muted small">
            ✓ = granted, and by which role. Time-windowed assignments are evaluated NOW, so an
            out-of-hours role shows nothing until its window opens.
          </p>
          {groups.map((g) => {
            const inGroup = perms.filter((p) => p.group === g);
            if (inGroup.length === 0) return null;
            return (
              <div key={g}>
                <h4 className="small">{g}</h4>
                <table>
                  <thead><tr><th /><th>Permission</th><th>Granted by</th></tr></thead>
                  <tbody>
                    {inGroup.map((p) => {
                      const has = effectiveCodes.has(p.code);
                      const cap = effective.find((e) => e.code === p.code)?.maxPence;
                      return (
                        <tr key={p.code} className={has ? undefined : "muted"}>
                          <td>{has ? "✓" : "—"}</td>
                          <td className="small" title={p.description}>
                            {p.code}
                            {has && cap != null && <span className="chip"> ≤ £{(cap / 100).toFixed(2)}</span>}
                          </td>
                          <td className="small">{has ? (grantedBy(p.code).join(", ") || "direct/seed") : ""}</td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            );
          })}
        </details>

        <div className="dialog-actions">
          <button className="ghost" onClick={onClose}>Close</button>
        </div>
      </div>
    </div>
  );
}
