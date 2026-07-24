import { useEffect, useState } from "react";
import { createEmployee, fetchEmployees, setEmployeePassword, type Employee } from "./api.ts";

export default function EmployeesPage() {
  const [employees, setEmployees] = useState<Employee[] | null>(null);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [showAdd, setShowAdd] = useState(false);
  const [pwFor, setPwFor] = useState<Employee | null>(null);

  const refresh = () =>
    fetchEmployees()
      .then(setEmployees)
      .catch((e) => setError(String(e)));

  useEffect(() => {
    refresh();
  }, []);

  return (
    <section className="panel">
      <div className="panel-head">
        <h2>Users</h2>
        <button className="ghost" onClick={() => setShowAdd(true)}>
          Add user
        </button>
      </div>

      {error && <p className="error small">{error}</p>}
      {notice && <p className="small discount-note">{notice}</p>}
      {!employees && !error && <p className="muted">Loading…</p>}

      {employees && (
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>Email</th>
              <th>Mobile</th>
              <th>Active</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {employees.map((e) => (
              <tr key={e.id}>
                <td>
                  {e.fName} {e.lName}
                </td>
                <td>{e.email}</td>
                <td>{e.mobile === "-" ? "" : e.mobile}</td>
                <td>{e.active ? "✓" : "—"}</td>
                <td>
                  <button className="ghost small" onClick={() => setPwFor(e)}>
                    Set password
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {showAdd && (
        <AddUserDialog
          onClose={() => setShowAdd(false)}
          onDone={(msg) => {
            setShowAdd(false);
            setNotice(msg);
            refresh();
          }}
        />
      )}
      {pwFor && (
        <PasswordDialog
          employee={pwFor}
          onClose={() => setPwFor(null)}
          onDone={(msg) => {
            setPwFor(null);
            setNotice(msg);
          }}
        />
      )}
    </section>
  );
}

function AddUserDialog({ onClose, onDone }: { onClose: () => void; onDone: (msg: string) => void }) {
  const [fName, setFName] = useState("");
  const [lName, setLName] = useState("");
  const [email, setEmail] = useState("");
  const [mobile, setMobile] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      const id = await createEmployee({ fName: fName.trim(), lName: lName.trim(), email: email.trim(), mobile: mobile.trim() });
      if (password) await setEmployeePassword(id, email.trim(), password);
      onDone(`User ${fName} ${lName} created${password ? " with web login" : ""}.`);
    } catch (err) {
      setError(String(err));
      setBusy(false);
    }
  }

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && !busy && onClose()}>
      <form className="dialog" onSubmit={submit}>
        <h2>Add user</h2>
        <div className="form-grid">
          <label>
            First name
            <input value={fName} onChange={(e) => setFName(e.target.value)} required disabled={busy} />
          </label>
          <label>
            Last name
            <input value={lName} onChange={(e) => setLName(e.target.value)} required disabled={busy} />
          </label>
          <label>
            Email
            <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required disabled={busy} />
          </label>
          <label>
            Mobile
            <input value={mobile} onChange={(e) => setMobile(e.target.value)} disabled={busy} />
          </label>
          <label>
            Web password <span className="muted small">(optional, min 8 chars)</span>
            <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} minLength={8} disabled={busy} />
          </label>
        </div>
        {error && <p className="error small">{error}</p>}
        <div className="dialog-actions">
          <button type="button" className="ghost" onClick={onClose} disabled={busy}>
            Cancel
          </button>
          <button type="submit" className="primary" disabled={busy || !fName.trim() || !lName.trim() || !email.trim()}>
            {busy ? "Creating…" : "Create user"}
          </button>
        </div>
      </form>
    </div>
  );
}

function PasswordDialog({ employee, onClose, onDone }: { employee: Employee; onClose: () => void; onDone: (msg: string) => void }) {
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      await setEmployeePassword(employee.id, employee.email, password);
      onDone(`Password set for ${employee.fName} ${employee.lName}.`);
    } catch (err) {
      setError(String(err));
      setBusy(false);
    }
  }

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && !busy && onClose()}>
      <form className="dialog" onSubmit={submit}>
        <h2>Set password</h2>
        <p className="muted small">
          {employee.fName} {employee.lName} — {employee.email}
        </p>
        <label className="block-label">
          New password
          <input type="password" autoFocus value={password} onChange={(e) => setPassword(e.target.value)} minLength={8} required disabled={busy} />
        </label>
        {error && <p className="error small">{error}</p>}
        <div className="dialog-actions">
          <button type="button" className="ghost" onClick={onClose} disabled={busy}>
            Cancel
          </button>
          <button type="submit" className="primary" disabled={busy || password.length < 8}>
            {busy ? "Saving…" : "Save"}
          </button>
        </div>
      </form>
    </div>
  );
}
