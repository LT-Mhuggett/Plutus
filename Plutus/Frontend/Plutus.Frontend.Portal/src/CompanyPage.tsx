import { useEffect, useState } from "react";
import { fetchCompanies, updateCompany, type Company } from "./api.ts";
import PeriodsPage from "./PeriodsPage.tsx";

/** WP11.5: the Company tab — company details (moved off the old Stores & Tills page) with
 *  Financial periods absorbed as a section below (the standalone Periods tab is gone). */
export default function CompanyPage() {
  const [companies, setCompanies] = useState<Company[]>([]);
  const [error, setError] = useState("");
  const refresh = () => fetchCompanies().then(setCompanies).catch((e) => setError(String(e)));
  useEffect(() => { void refresh(); }, []);

  return (
    <>
      <section className="panel">
        <h2>Company</h2>
        {error && <p className="error">{error}</p>}
        {companies.map((c) => <CompanyEditor key={c.id} company={c} onSaved={refresh} />)}
        {companies.length === 0 && !error && <p className="muted">Loading…</p>}
      </section>
      <PeriodsPage />
    </>
  );
}

function CompanyEditor({ company, onSaved }: { company: Company; onSaved: () => Promise<void> | void }) {
  const [name, setName] = useState(company.name);
  const [vat, setVat] = useState(company.vatIN);
  const [busy, setBusy] = useState(false);
  const dirty = name !== company.name || vat !== company.vatIN;
  return (
    <div className="toolbar">
      <label>Name <input value={name} onChange={(e) => setName(e.target.value)} /></label>
      <label>VAT no. <input value={vat} onChange={(e) => setVat(e.target.value)} /></label>
      {dirty && (
        <button className="primary small" disabled={busy} onClick={async () => {
          setBusy(true);
          await updateCompany(company.id, { name, vatIN: vat }).finally(() => setBusy(false));
          await onSaved();
        }}>Save</button>
      )}
    </div>
  );
}
