import { useEffect, useState } from "react";
import { fetchStoreInfo, TILL_ID, type StoreInfoView } from "./api.ts";
import { dayText, parseOpeningHours, unknownDayKeys } from "./openingHours.ts";

// WP6.1: Store Information is now READ-ONLY on the till — the management portal (Company /
// Locations) is the single source of truth. This shows the store's name, VAT, address, contact and
// opening hours from the v1 endpoint; editing happens in the portal.

/**
 * The week, as the portal set it.
 *
 * ⚠⚠ THREE OUTCOMES, THREE MESSAGES — 2026-08-17. This used to print *"Not set — add opening hours in
 * the management portal"* for a blank field, a malformed one and a shape it did not recognise alike,
 * so Matt's *"webtill does not show the opening hours set in the portal"* could not be diagnosed from
 * the screen at all. **"Not set" now means nothing is stored**, and anything else says what is wrong
 * and where. The parsing lives in `openingHours.ts` so it can be tested and so MAUI can share it.
 */
function OpeningHours({ json }: { json: string | null }) {
  const hours = parseOpeningHours(json);

  if (hours.state === "unset")
    return <p className="muted small">Not set — add opening hours in the management portal.</p>;

  if (hours.state === "unreadable")
    return (
      <>
        {/* ⚠ It names the FIELD and the FAULT. "Something went wrong" would send somebody back to the
            portal to retype hours that are already there, into the box that is already wrong. */}
        <p className="error small">
          The portal has opening hours for this store, but this till can't read them: {hours.detail}.
        </p>
        <p className="muted small">
          Fix them in <strong>Locations → this store → Opening hours</strong>. If the advanced JSON box
          was used, switching back to the simple editor and re-ticking the days will rewrite it cleanly.
        </p>
      </>
    );

  const leftovers = unknownDayKeys(json);
  return (
    <>
      <dl className="env-info">
        {hours.week.map((d) => (
          <div key={d.key} style={{ display: "contents" }}>
            <dt>{d.label}</dt>
            <dd>{d.spans.length === 0 ? <span className="muted">Closed</span> : dayText(d)}</dd>
          </div>
        ))}
      </dl>
      {/* ⚠ Named rather than dropped: a key nobody reads is a setting somebody thinks is in effect. */}
      {leftovers.length > 0 && (
        <p className="muted small">Ignored (not a day): {leftovers.join(", ")}.</p>
      )}
    </>
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
