import { useEffect, useState } from "react";
import {
  createTill, fetchCompanies, fetchStores, fetchTills, putReceiptTemplate, renameTill, revokeTill,
  updateCompany, updateStore, type Company, type ReceiptTemplate, type StoreRow, type TillRow,
} from "./api.ts";

/** Stores & tills admin: company details, store addresses + opening hours, and the till
 *  fleet with enrolment codes (WP1.2 flow) + revoke. */
export default function StoresPage() {
  const [companies, setCompanies] = useState<Company[]>([]);
  const [stores, setStores] = useState<StoreRow[]>([]);
  const [tills, setTills] = useState<TillRow[]>([]);
  const [error, setError] = useState("");
  const [issued, setIssued] = useState<{ tillId: string; code: string; expires: string } | null>(null);
  const [busy, setBusy] = useState(false);

  const refresh = () =>
    Promise.all([fetchCompanies(), fetchStores(), fetchTills()])
      .then(([c, s, t]) => { setCompanies(c); setStores(s); setTills(t); })
      .catch((e) => setError(String(e)));

  useEffect(() => { void refresh(); }, []);

  async function newTill(storeId: number, name: string) {
    setBusy(true);
    setError("");
    try {
      const r = await createTill(storeId, name.trim());
      setIssued({ tillId: r.tillId, code: r.enrolmentCode, expires: r.expiresAtUtc });
      await refresh();
    } catch (e) {
      setError(String(e instanceof Error ? e.message : e));
    } finally {
      setBusy(false);
    }
  }

  async function rename(id: string, name: string) {
    setError("");
    try {
      await renameTill(id, name);
      await refresh();
    } catch (e) {
      setError(String(e instanceof Error ? e.message : e));
    }
  }

  return (
    <section className="panel">
      {error && <p className="error">{error}</p>}

      <h2>Company</h2>
      {companies.map((c) => (
        <CompanyRow key={c.id} company={c} onSaved={refresh} />
      ))}

      <h2>Stores</h2>
      {stores.map((s) => (
        <StoreCard key={s.id} store={s} onSaved={refresh} />
      ))}

      <h2>Tills</h2>
      <table>
        <thead><tr><th>Name</th><th>Store</th><th>Devices</th><th>Last online</th><th /></tr></thead>
        <tbody>
          {tills.map((t) => (
            <tr key={t.id}>
              <td><TillNameCell till={t} onRename={rename} /></td>
              <td>{t.storeId}</td>
              <td>
                {t.devices.length === 0 && <span className="muted">none</span>}
                {t.devices.map((d) => (
                  <span key={d.id} className={`chip ${d.status === "Active" ? "ok" : "bad"}`}>
                    {d.id.slice(0, 8)}… {d.status} (seq {d.lastSeenSeq})
                  </span>
                ))}
              </td>
              <td>{new Date(t.lastOnline + "Z").toLocaleString("en-GB")}</td>
              <td>
                {t.devices.some((d) => d.status === "Active") && (
                  <button className="ghost small" disabled={busy} onClick={() => void revokeTill(t.id).then(refresh)}>
                    Revoke devices
                  </button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {stores.map((s) => (
        <NewTillRow key={s.id} storeId={s.id} busy={busy} onCreate={newTill} />
      ))}

      {issued && (
        <div className="enrol-code">
          <div className="grow">
            <span className="muted small">Enrolment code for till {issued.tillId.slice(0, 8)}… — enter it on the
              till at Settings → Till device (single-use, expires {new Date(issued.expires).toLocaleString("en-GB")}).</span>
            <div className="enrol-code-value mono">{issued.code}</div>
          </div>
          <button className="ghost small" onClick={() => void navigator.clipboard?.writeText(issued.code)}>Copy</button>
          <button className="ghost small" onClick={() => setIssued(null)}>Dismiss</button>
        </div>
      )}
    </section>
  );
}

/** Inline till rename: click the name to edit, Enter/blur saves (409 surfaces via onRename). */
function TillNameCell({ till, onRename }: { till: TillRow; onRename: (id: string, name: string) => Promise<void> }) {
  const [editing, setEditing] = useState(false);
  const [name, setName] = useState(till.name);
  if (!editing) {
    return (
      <button className="link-name" title={till.id} onClick={() => { setName(till.name); setEditing(true); }}>
        {till.name}
      </button>
    );
  }
  const commit = async () => {
    setEditing(false);
    if (name.trim() && name.trim() !== till.name) await onRename(till.id, name.trim());
  };
  return (
    <input
      autoFocus
      value={name}
      maxLength={80}
      onChange={(e) => setName(e.target.value)}
      onBlur={() => void commit()}
      onKeyDown={(e) => { if (e.key === "Enter") void commit(); if (e.key === "Escape") setEditing(false); }}
    />
  );
}

/** New till + enrolment code, with a required unique name. */
function NewTillRow({ storeId, busy, onCreate }: { storeId: number; busy: boolean; onCreate: (storeId: number, name: string) => Promise<void> }) {
  const [name, setName] = useState("");
  return (
    <div className="toolbar">
      <label>New till (store {storeId})
        <input value={name} maxLength={80} placeholder="e.g. Front Desk" onChange={(e) => setName(e.target.value)} />
      </label>
      <button
        className="primary small"
        disabled={busy || !name.trim()}
        onClick={async () => { await onCreate(storeId, name); setName(""); }}
      >
        Create till + code
      </button>
    </div>
  );
}

function CompanyRow({ company, onSaved }: { company: Company; onSaved: () => Promise<void> | void }) {
  const [name, setName] = useState(company.name);
  const [vat, setVat] = useState(company.vatIN);
  const [busy, setBusy] = useState(false);
  const dirty = name !== company.name || vat !== company.vatIN;
  return (
    <div className="toolbar">
      <label>Name <input value={name} onChange={(e) => setName(e.target.value)} /></label>
      <label>VAT no. <input value={vat} onChange={(e) => setVat(e.target.value)} /></label>
      {dirty && (
        <button
          className="primary small"
          disabled={busy}
          onClick={async () => {
            setBusy(true);
            await updateCompany(company.id, { name, vatIN: vat }).finally(() => setBusy(false));
            await onSaved();
          }}
        >
          Save
        </button>
      )}
    </div>
  );
}

function StoreCard({ store, onSaved }: { store: StoreRow; onSaved: () => Promise<void> | void }) {
  const [edit, setEdit] = useState<StoreRow>(store);
  const [busy, setBusy] = useState(false);
  const dirty = JSON.stringify(edit) !== JSON.stringify(store);
  return (
    <div className="card">
      <div className="toolbar">
        <span className="muted small">Store {store.id}</span>
        <label>Address <input value={edit.adLine1} onChange={(e) => setEdit({ ...edit, adLine1: e.target.value })} /></label>
        <label>City <input value={edit.city} onChange={(e) => setEdit({ ...edit, city: e.target.value })} /></label>
        <label>Postcode <input value={edit.postCode} onChange={(e) => setEdit({ ...edit, postCode: e.target.value })} /></label>
        <label>Phone <input value={edit.contactNumber} onChange={(e) => setEdit({ ...edit, contactNumber: e.target.value })} /></label>
      </div>
      <label className="block">
        Opening hours (JSON — e.g. {"{\"mon\":[{\"open\":\"09:00\",\"close\":\"17:30\"}]}"})
        <textarea
          rows={2}
          value={edit.openingHoursJson ?? ""}
          onChange={(e) => setEdit({ ...edit, openingHoursJson: e.target.value || null })}
        />
      </label>
      {dirty && (
        <button
          className="primary small"
          disabled={busy}
          onClick={async () => {
            setBusy(true);
            await updateStore(store.id, {
              adLine1: edit.adLine1, city: edit.city, postCode: edit.postCode,
              contactNumber: edit.contactNumber, openingHoursJson: edit.openingHoursJson,
            }).finally(() => setBusy(false));
            await onSaved();
          }}
        >
          Save store
        </button>
      )}
      <ReceiptTemplateEditor store={store} onSaved={onSaved} />
    </div>
  );
}

/** WP11.2: per-store receipt template — header/footer lines (one per row) + toggles. */
function ReceiptTemplateEditor({ store, onSaved }: { store: StoreRow; onSaved: () => Promise<void> | void }) {
  const initial: ReceiptTemplate = (() => {
    try { return store.receiptTemplateJson ? JSON.parse(store.receiptTemplateJson) : {}; } catch { return {}; }
  })();
  const [tpl, setTpl] = useState<ReceiptTemplate>(initial);
  const [busy, setBusy] = useState(false);
  const dirty = JSON.stringify(tpl) !== JSON.stringify(initial);
  const linesToText = (a?: string[]) => (a ?? []).join("\n");
  const textToLines = (s: string) => s.split("\n").map((l) => l.trimEnd()).filter((l, i, arr) => l !== "" || i < arr.length);

  return (
    <details className="receipt-tpl">
      <summary className="muted small">Receipt template</summary>
      <label className="block">Header lines (one per line)
        <textarea rows={2} value={linesToText(tpl.headerLines)}
          onChange={(e) => setTpl({ ...tpl, headerLines: textToLines(e.target.value) })} />
      </label>
      <label className="block">Footer lines (e.g. returns policy)
        <textarea rows={2} value={linesToText(tpl.footerLines)}
          onChange={(e) => setTpl({ ...tpl, footerLines: textToLines(e.target.value) })} />
      </label>
      <label className="chk"><input type="checkbox" checked={tpl.showBarcode !== false}
        onChange={(e) => setTpl({ ...tpl, showBarcode: e.target.checked })} /> Show sale barcode</label>
      <label className="chk"><input type="checkbox" checked={!!tpl.showOperator}
        onChange={(e) => setTpl({ ...tpl, showOperator: e.target.checked })} /> Show operator name</label>
      <label className="chk"><input type="checkbox" checked={!!tpl.showVatNumber}
        onChange={(e) => setTpl({ ...tpl, showVatNumber: e.target.checked })} /> Show VAT number</label>
      {dirty && (
        <button className="primary small" disabled={busy} onClick={async () => {
          setBusy(true);
          await putReceiptTemplate(store.id, tpl).finally(() => setBusy(false));
          await onSaved();
        }}>Save receipt template</button>
      )}
    </details>
  );
}
