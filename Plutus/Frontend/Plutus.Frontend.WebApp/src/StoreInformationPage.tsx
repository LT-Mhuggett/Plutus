import { useEffect, useState } from "react";
import {
  fetchBusiness,
  fetchStore,
  updateBusiness,
  updateStore,
  TILL_ID,
  type BusinessInfo,
  type StoreInfo,
} from "./api.ts";

/** Store Information — the webapp counterpart of NatApp's Store Options screen
 *  (name / VAT number / contact / address editable; logo + currency/date display
 *  are deferred — see notes at the bottom of the page). */
export default function StoreInformationPage() {
  const [business, setBusiness] = useState<BusinessInfo | null>(null);
  const [store, setStore] = useState<StoreInfo | null>(null);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);

  // edit state
  const [bName, setBName] = useState("");
  const [bAbbr, setBAbbr] = useState("");
  const [bVat, setBVat] = useState("");
  const [sAd1, setSAd1] = useState("");
  const [sAd2, setSAd2] = useState("");
  const [sCity, setSCity] = useState("");
  const [sPost, setSPost] = useState("");
  const [sCountry, setSCountry] = useState("");
  const [sContact, setSContact] = useState("");

  useEffect(() => {
    fetchBusiness()
      .then((b) => {
        setBusiness(b);
        setBName(b.name);
        setBAbbr(b.nameAbbr);
        setBVat(b.vatIN === "-" ? "" : b.vatIN);
      })
      .catch((e) => setError(String(e)));
    fetchStore()
      .then((s) => {
        setStore(s);
        setSAd1(s.adLine1 === "-" ? "" : s.adLine1);
        setSAd2(s.adLine2 ?? "");
        setSCity(s.city === "-" ? "" : s.city);
        setSPost(s.postCode === "-" ? "" : s.postCode);
        setSCountry(s.country === "-" ? "" : s.country);
        setSContact(s.contactNumber === "-" ? "" : s.contactNumber);
      })
      .catch((e) => setError(String(e)));
  }, []);

  async function saveBusiness(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      await updateBusiness({ name: bName.trim(), nameAbbr: bAbbr.trim() || "-", vatIN: bVat.trim() || "-" });
      setNotice("Business details saved.");
    } catch (err) {
      setError(String(err));
    } finally {
      setBusy(false);
    }
  }

  async function saveStore(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      await updateStore({
        adLine1: sAd1.trim() || "-",
        adLine2: sAd2.trim(),
        city: sCity.trim() || "-",
        postCode: sPost.trim() || "-",
        country: sCountry.trim() || "-",
        contactNumber: sContact.trim() || "-",
        fullAddress: "", // keep derived from the lines
      });
      setNotice("Store details saved.");
    } catch (err) {
      setError(String(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="panel">
      <h2>Store Information</h2>
      {error && <p className="error small">{error}</p>}
      {notice && <p className="small discount-note">{notice}</p>}
      {!business && !store && !error && <p className="muted">Loading…</p>}

      <div className="info-cards">
        {business && (
          <form className="info-card" onSubmit={saveBusiness}>
            <h3>Business</h3>
            <div className="form-grid one-col">
              <label>
                Name (appears on receipts)
                <input value={bName} onChange={(e) => setBName(e.target.value)} required disabled={busy} />
              </label>
              <label>
                Abbreviation
                <input value={bAbbr} onChange={(e) => setBAbbr(e.target.value)} maxLength={10} disabled={busy} />
              </label>
              <label>
                VAT number
                <input value={bVat} onChange={(e) => setBVat(e.target.value)} disabled={busy} />
              </label>
            </div>
            <button className="primary slim" type="submit" disabled={busy || !bName.trim()}>
              Save business
            </button>
          </form>
        )}

        {store && (
          <form className="info-card" onSubmit={saveStore}>
            <h3>Store</h3>
            <div className="form-grid one-col">
              <label>
                Address line 1
                <input value={sAd1} onChange={(e) => setSAd1(e.target.value)} disabled={busy} />
              </label>
              <label>
                Address line 2
                <input value={sAd2} onChange={(e) => setSAd2(e.target.value)} disabled={busy} />
              </label>
              <label>
                City
                <input value={sCity} onChange={(e) => setSCity(e.target.value)} disabled={busy} />
              </label>
              <label>
                Postcode
                <input value={sPost} onChange={(e) => setSPost(e.target.value)} disabled={busy} />
              </label>
              <label>
                Country
                <input value={sCountry} onChange={(e) => setSCountry(e.target.value)} disabled={busy} />
              </label>
              <label>
                Contact number
                <input value={sContact} onChange={(e) => setSContact(e.target.value)} disabled={busy} />
              </label>
            </div>
            <button className="primary slim" type="submit" disabled={busy}>
              Save store
            </button>
          </form>
        )}
      </div>

      <dl className="env-info">
        <dt>Store id</dt>
        <dd>{store?.id ?? "—"}</dd>
        <dt>Till id</dt>
        <dd className="mono small">{TILL_ID}</dd>
      </dl>
      <p className="muted small">
        Deferred from NatApp Store Options: logo upload (receipt logo) and currency/date display formats — the webapp
        currently uses UK formats throughout. Employee management lives under the 👥 users menu.
      </p>
    </section>
  );
}
