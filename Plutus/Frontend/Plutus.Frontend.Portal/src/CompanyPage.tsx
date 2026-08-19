import { useEffect, useState } from "react";
import {
  fetchCompanies, updateCompany, fetchGatewayCatalogue, fetchGatewayConfig, setGatewayConfig,
  fetchMfaRequired, setMfaRequired,
  type Company, type CommerceProviderInfo, type GatewayConfig,
} from "./api.ts";
import PeriodsPage from "./PeriodsPage.tsx";
import CarrierBagsSection from "./CarrierBagsSection.tsx";

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
      {/* Ruling 2026-08-19 — carrier bags are defined here and every till offers them.
          ⚠ Here rather than under Locations (where themes and published reports live) for the reason
          that page states about itself: those configure a TILL and need its list of tills. A bag list
          is shop-wide, has no per-till anything, and is a money decision like the card surcharge
          above it — same tab, same `portal.company.manage` permission. */}
      <CarrierBagsSection />
      <SecuritySection />
      <PeriodsPage />
    </>
  );
}

/** Company → Security: the MFA/SSO requirement toggle. When on, everyone in this company is routed
 *  to Plutus secure sign-in (Keycloak) and forced to enrol an authenticator on next login. Gated on
 *  portal.company.manage (403 → hidden). */
function SecuritySection() {
  const [current, setCurrent] = useState<boolean | null>(null);
  const [mfa, setMfa] = useState(false);
  const [msg, setMsg] = useState("");
  const [error, setError] = useState("");
  const [denied, setDenied] = useState(false);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    fetchMfaRequired()
      .then((r) => { setCurrent(r.mfaRequired); setMfa(r.mfaRequired); })
      .catch(() => setDenied(true)); // 403 = can't manage company settings → hide
  }, []);

  if (denied) return null;
  const dirty = current !== null && mfa !== current;

  return (
    <section className="panel">
      <h2>Security &amp; sign-in</h2>
      <p className="muted small">Control how your team signs in to Plutus.</p>
      {error && <p className="error">{error}</p>}
      {msg && <p className="muted small">{msg}</p>}
      <label className="toolbar" style={{ gap: 8, alignItems: "center" }}>
        <input type="checkbox" checked={mfa} disabled={current === null}
          onChange={(e) => { setMfa(e.target.checked); setMsg(""); }} />
        <span><strong>Require multi-factor authentication (MFA)</strong> for everyone in this company</span>
      </label>
      <div className="panel" style={{ background: "#f8fafc", marginTop: 8 }}>
        <p className="small" style={{ margin: 0 }}><strong>What happens when you turn this on</strong></p>
        <ul className="small muted" style={{ marginTop: 4, marginBottom: 0 }}>
          <li>Everyone in your company is <strong>emailed a heads-up</strong> as soon as you turn this on.</li>
          <li>Everyone is then taken to Plutus secure sign-in instead of the password box.</li>
          <li>On their next login each person is <strong>forced to set up an authenticator app</strong> (Google Authenticator, Microsoft Authenticator, 1Password, …) — they scan a QR code once.</li>
          <li>After that, every sign-in needs their password <em>and</em> a 6-digit code from that app.</li>
          <li>The password-only login is disabled for your company while this is on.</li>
        </ul>
      </div>
      {dirty && (
        <div className="toolbar" style={{ marginTop: 8 }}>
          <button className="primary small" disabled={busy} onClick={() => {
            setBusy(true);
            void setMfaRequired(mfa)
              .then(() => { setCurrent(mfa); setMsg(mfa ? "MFA is now required for your company." : "MFA requirement removed."); setError(""); })
              .catch((e) => setError(String(e)))
              .finally(() => setBusy(false));
          }}>{mfa ? "Turn MFA on" : "Turn MFA off"}</button>
          <button className="ghost small" onClick={() => { if (current !== null) setMfa(current); setMsg(""); }}>Cancel</button>
        </div>
      )}
    </section>
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
  // Held as the operator types them: percent as "1.69", flat as pounds "0.20". Converted to
  // bp/pence at save so the wire stays integer money.
  const [surchargePct, setSurchargePct] = useState("0");
  const [surchargeFlat, setSurchargeFlat] = useState("0");
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
    setSurchargePct(((current.surchargeBp ?? 0) / 100).toString());
    setSurchargeFlat(((current.surchargeFlatPence ?? 0) / 100).toFixed(2));
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
      <h3>Card surcharge</h3>
      <p className="muted small">
        A fee added when a customer pays by card, as a percentage of the basket plus a fixed
        amount — the same shape as your provider's own fee (for example 1.69% + 20p). Leave both
        at 0 for no surcharge.
      </p>
      <p className="muted small">
        <strong>⚠ Know the law before turning this on.</strong> In the UK it has been <strong>illegal
        to surcharge consumers</strong> paying with personal debit or credit cards (and services
        like PayPal) since 13 January 2018. Surcharging is only lawful for <em>commercial/corporate</em> cards,
        and then no more than your actual cost of taking the payment. Other countries have their
        own rules. You are responsible for charging this lawfully.
      </p>
      <p className="muted small">
        <strong>VAT is applicable — and it is worked out for you.</strong> A card surcharge is not
        VAT-free: HMRC treats it as part of the payment for the goods themselves, so it carries
        VAT <em>at the rate of what's in the basket</em> — 20% on standard-rated goods, none on
        zero-rated goods, a blend on a mixed basket. Enter the fee you want to charge
        <em> including</em> any VAT; the till calculates the right VAT on every sale and it flows
        into your VAT figures automatically. Do not add VAT on top yourself.
      </p>
      <div className="toolbar" style={{ flexWrap: "wrap" }}>
        <label>Percentage (%)
          <input type="number" min="0" max="10" step="0.01" value={surchargePct}
            onChange={(e) => setSurchargePct(e.target.value)} />
        </label>
        <label>Fixed amount (£)
          <input type="number" min="0" max="5" step="0.01" value={surchargeFlat}
            onChange={(e) => setSurchargeFlat(e.target.value)} />
        </label>
      </div>
      <div className="toolbar">
        <button className="primary small" onClick={() =>
          void setGatewayConfig({
            provider, config,
            surchargeBp: Math.round((parseFloat(surchargePct) || 0) * 100),
            surchargeFlatPence: Math.round((parseFloat(surchargeFlat) || 0) * 100),
          })
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
