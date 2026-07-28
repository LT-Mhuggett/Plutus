import { useEffect, useState } from "react";
import {
  fetchCompanies, updateCompany, fetchGatewayCatalogue, fetchGatewayConfig, setGatewayConfig,
  type Company, type CommerceProviderInfo, type GatewayConfig,
} from "./api.ts";
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
      <PaymentGatewaySection />
      <PeriodsPage />
    </>
  );
}

/** 17.2: this company's card-payment setup. "Standalone terminal" is the default — take payment
 *  on your own chip & pin machine and confirm approval on the till (no integration). Selecting an
 *  integrated provider stores its keys (write-only) ready for when the integration is wired; the
 *  till keeps the standalone confirm flow until then, so selling never blocks. */
function PaymentGatewaySection() {
  const [catalogue, setCatalogue] = useState<CommerceProviderInfo[]>([]);
  const [current, setCurrent] = useState<GatewayConfig | null>(null);
  const [provider, setProvider] = useState("standalone");
  const [config, setConfig] = useState<Record<string, string>>({});
  const [msg, setMsg] = useState("");
  const [error, setError] = useState("");
  const [denied, setDenied] = useState(false);

  useEffect(() => {
    fetchGatewayCatalogue().then(setCatalogue).catch((e) => setError(String(e)));
    // 403 here just means this user can't manage company settings — hide, don't error.
    fetchGatewayConfig().then(setCurrent).catch(() => setDenied(true));
  }, []);
  useEffect(() => {
    if (!current) return;
    setProvider(current.provider); setConfig(current.config);
  }, [current]);

  if (denied) return null;
  const info = catalogue.find((p) => p.key === provider);

  return (
    <section className="panel">
      <h2>Card payments</h2>
      <p className="muted small">How this company takes card payments. <strong>Standalone terminal</strong> keeps today's flow — take payment on your chip &amp; pin machine, then confirm on the till. Pick an integrated provider to store its keys ready for integration.</p>
      {error && <p className="error">{error}</p>}
      {msg && <p className="muted small">{msg}</p>}
      <div className="toolbar" style={{ flexWrap: "wrap" }}>
        <label>Provider
          <select value={provider} onChange={(e) => { setProvider(e.target.value); setConfig({}); }}>
            {catalogue.map((p) => <option key={p.key} value={p.key}>{p.label}</option>)}
          </select>
        </label>
      </div>
      {info && <p className="muted small">{info.blurb}</p>}
      {info && info.fields.length > 0 && (
        <div className="toolbar" style={{ flexWrap: "wrap" }}>
          {info.fields.map((f) => (
            <label key={f.name}>{f.label}{f.required ? " *" : ""}
              <input type={f.secret ? "password" : "text"} value={config[f.name] ?? ""}
                placeholder={f.secret ? "(unchanged)" : ""}
                onChange={(e) => setConfig({ ...config, [f.name]: e.target.value })} />
            </label>
          ))}
        </div>
      )}
      <div className="toolbar">
        <button className="primary small" onClick={() =>
          void setGatewayConfig({ provider, config })
            .then(() => { setMsg("Saved."); setError(""); }).catch((e) => setError(String(e)))}>
          Save payment setup
        </button>
      </div>
    </section>
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
