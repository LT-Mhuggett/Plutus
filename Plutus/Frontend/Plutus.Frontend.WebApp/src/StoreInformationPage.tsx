import { useEffect, useState } from "react";
import { fetchStoreInfo, TILL_ID, type StoreInfoView } from "./api.ts";

// WP6.1: Store Information is now READ-ONLY on the till — the management portal (Company /
// Locations) is the single source of truth. This shows the store's name, VAT, address, contact and
// opening hours from the v1 endpoint; editing happens in the portal.

const DAYS: [string, string][] = [
  ["mon", "Monday"], ["tue", "Tuesday"], ["wed", "Wednesday"], ["thu", "Thursday"],
  ["fri", "Friday"], ["sat", "Saturday"], ["sun", "Sunday"],
];

/** Render the opaque opening-hours JSON ({"mon":[{"open":"09:00","close":"17:30"}],…}) as a list;
 *  anything unparseable just isn't shown. */
function OpeningHours({ json }: { json: string | null }) {
  let parsed: Record<string, { open: string; close: string }[]> | null = null;
  try { parsed = json ? JSON.parse(json) : null; } catch { parsed = null; }
  if (!parsed) return <p className="muted small">Not set — add opening hours in the management portal.</p>;
  return (
    <dl className="env-info">
      {DAYS.map(([key, label]) => {
        const spans = parsed![key] ?? [];
        return (
          <div key={key} style={{ display: "contents" }}>
            <dt>{label}</dt>
            <dd>{spans.length === 0 ? <span className="muted">Closed</span> : spans.map((s) => `${s.open}–${s.close}`).join(", ")}</dd>
          </div>
        );
      })}
    </dl>
  );
}

export default function StoreInformationPage() {
  const [info, setInfo] = useState<StoreInfoView | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    fetchStoreInfo().then(setInfo).catch((e) => setError(String(e instanceof Error ? e.message : e)));
  }, []);

  const addressLines = info
    ? [info.adLine1, info.adLine2, info.city, info.postCode, info.country].map((l) => (l === "-" ? "" : l)).filter(Boolean)
    : [];

  return (
    <section className="panel">
      <h2>Store Information</h2>
      <p className="muted small">
        Read-only here — edit these details in the management portal under <strong>Company</strong> and <strong>Locations</strong>.
      </p>
      {error && <p className="error small">{error}</p>}
      {!info && !error && <p className="muted">Loading…</p>}

      {info && (
        <div className="info-cards">
          <div className="info-card">
            <h3>Business</h3>
            <dl className="env-info">
              <dt>Name</dt><dd>{info.businessName ?? "—"}</dd>
              <dt>VAT number</dt><dd>{info.vatNumber && info.vatNumber !== "-" ? info.vatNumber : "—"}</dd>
            </dl>
          </div>
          <div className="info-card">
            <h3>Store</h3>
            <dl className="env-info">
              <dt>Store name</dt><dd>{info.name ?? "—"}</dd>
              <dt>Address</dt><dd>{addressLines.length ? addressLines.join(", ") : "—"}</dd>
              <dt>Contact number</dt><dd>{info.contactNumber && info.contactNumber !== "-" ? info.contactNumber : "—"}</dd>
            </dl>
          </div>
          <div className="info-card">
            <h3>Opening hours</h3>
            <OpeningHours json={info.openingHoursJson} />
          </div>
        </div>
      )}

      <dl className="env-info">
        <dt>Store id</dt><dd>{info?.storeId ?? "—"}</dd>
        <dt>Till id</dt><dd className="mono small">{TILL_ID}</dd>
      </dl>
    </section>
  );
}
