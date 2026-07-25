import { useEffect, useState } from "react";
import { ApiError, gbp } from "./api.ts";

// WP5.4 portal pricing: global price editor (policy, HQ price incl. scheduling, store
// override, force-reset) + the per-store variance view.

interface PriceDetail {
  itemIdOne: string;
  name: string;
  legacyPricePence: number;
  policy: string;
  central: { pricePence: number; exPricePence: number; effectiveFromUtc: string }[];
  overrides: { storeId: number; pricePence: number; exPricePence: number; effectiveFromUtc: string }[];
}
interface VarianceRow { storeId: number; itemIdOne: string; name: string | null; policy: string; hqPence: number; storePence: number; deltaPence: number }

async function j<T>(method: string, url: string, body?: unknown): Promise<T> {
  const s = JSON.parse(localStorage.getItem("plutus.portal.session") ?? "null");
  const res = await fetch(url, {
    method,
    headers: { ...(s ? { Authorization: `Bearer ${s.token}` } : {}), ...(body !== undefined ? { "Content-Type": "application/json" } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!res.ok) {
    let detail = `${res.status}`;
    try { detail = (await res.json())?.detail ?? detail; } catch { /* keep */ }
    throw new ApiError(res.status, detail);
  }
  return res.status === 204 ? (undefined as T) : res.json();
}

const toPence = (s: string) => Math.round(parseFloat(s) * 100);

export default function PricesPage() {
  const [lookup, setLookup] = useState("");
  const [detail, setDetail] = useState<PriceDetail | null>(null);
  const [variance, setVariance] = useState<VarianceRow[]>([]);
  const [error, setError] = useState("");

  const refreshVariance = () =>
    j<VarianceRow[]>("GET", `/api/v1/prices/variance`).then(setVariance).catch((e) => setError(String(e)));
  useEffect(() => { void refreshVariance(); }, []);

  const open = (id: string) =>
    j<PriceDetail>("GET", `/api/v1/prices/${encodeURIComponent(id)}`)
      .then((d) => { setDetail(d); setError(""); })
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));

  return (
    <section className="panel">
      <div className="toolbar">
        <label>Item (barcode / id) <input value={lookup} onChange={(e) => setLookup(e.target.value)} placeholder="5011921068203" /></label>
        <button className="primary small" disabled={!lookup.trim()} onClick={() => void open(lookup.trim())}>Open price editor</button>
      </div>
      {error && <p className="error">{error}</p>}

      <h3>Store price variance vs HQ</h3>
      <table>
        <thead><tr><th>Store</th><th>Item</th><th>Name</th><th>Policy</th><th className="num">HQ</th><th className="num">Store</th><th className="num">Δ</th><th /></tr></thead>
        <tbody>
          {variance.map((v) => (
            <tr key={`${v.storeId}-${v.itemIdOne}`}>
              <td>{v.storeId}</td>
              <td className="mono small">{v.itemIdOne}</td>
              <td>{v.name}</td>
              <td>{v.policy}</td>
              <td className="num">{gbp(v.hqPence)}</td>
              <td className="num">{gbp(v.storePence)}</td>
              <td className="num">{v.deltaPence > 0 ? "+" : ""}{gbp(v.deltaPence)}</td>
              <td><button className="ghost small" onClick={() => void open(v.itemIdOne)}>Edit</button></td>
            </tr>
          ))}
          {variance.length === 0 && <tr><td colSpan={8} className="muted">No store deviates from the HQ price.</td></tr>}
        </tbody>
      </table>

      {detail && (
        <PriceDialog detail={detail} onClose={() => { setDetail(null); void refreshVariance(); }}
          onChanged={() => void open(detail.itemIdOne)} />
      )}
    </section>
  );
}

function PriceDialog({ detail, onClose, onChanged }: { detail: PriceDetail; onClose: () => void; onChanged: () => void }) {
  const [error, setError] = useState("");
  const [central, setCentral] = useState("");
  const [centralEx, setCentralEx] = useState("");
  const [schedule, setSchedule] = useState("");
  const [ovStore, setOvStore] = useState("1");
  const [ov, setOv] = useState("");
  const [ovEx, setOvEx] = useState("");

  const run = (p: Promise<unknown>) =>
    void p.then(onChanged).catch((e) => setError(String(e instanceof Error ? e.message : e)));

  const currentCentral = detail.central[0];

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog">
        <h3>{detail.itemIdOne} — {detail.name}</h3>
        {error && <p className="error small">{error}</p>}

        <div className="toolbar">
          <label>
            Policy{" "}
            <select value={detail.policy}
              onChange={(e) => run(j("PUT", `/api/v1/prices/${detail.itemIdOne}/policy`, { policy: e.target.value }))}>
              <option value="Central">Central (HQ only)</option>
              <option value="CentralWithOverride">Central with store override</option>
              <option value="Local">Local (store owns)</option>
            </select>
          </label>
          <span className="muted small grow">
            HQ price now: {currentCentral ? gbp(currentCentral.pricePence) : `${gbp(detail.legacyPricePence)} (legacy)`}
          </span>
          {detail.policy !== "Local" && detail.overrides.length > 0 && (
            <button className="ghost small"
              onClick={() => run(j("POST", `/api/v1/prices/${detail.itemIdOne}/force-reset`, {}))}>
              Force-reset overrides ({detail.overrides.length})
            </button>
          )}
        </div>

        <div className="toolbar">
          <label>HQ price £ <input className="short" inputMode="decimal" value={central} onChange={(e) => setCentral(e.target.value)} /></label>
          <label>ex-VAT £ <input className="short" inputMode="decimal" value={centralEx} onChange={(e) => setCentralEx(e.target.value)} /></label>
          <label>From (optional) <input type="datetime-local" value={schedule} onChange={(e) => setSchedule(e.target.value)} /></label>
          <button className="primary small" disabled={!central || !centralEx}
            onClick={() => run(j("POST", `/api/v1/prices/${detail.itemIdOne}/central`, {
              pricePence: toPence(central), exPricePence: toPence(centralEx),
              effectiveFromUtc: schedule ? new Date(schedule).toISOString() : null,
            }))}>
            Set HQ price
          </button>
        </div>

        {detail.policy !== "Central" && (
          <div className="toolbar">
            <label>Store <input className="short" inputMode="numeric" value={ovStore} onChange={(e) => setOvStore(e.target.value)} /></label>
            <label>Store price £ <input className="short" inputMode="decimal" value={ov} onChange={(e) => setOv(e.target.value)} /></label>
            <label>ex-VAT £ <input className="short" inputMode="decimal" value={ovEx} onChange={(e) => setOvEx(e.target.value)} /></label>
            <button className="primary small" disabled={!ov || !ovEx || !ovStore}
              onClick={() => run(j("POST", `/api/v1/prices/${detail.itemIdOne}/override`, {
                storeId: Number(ovStore), pricePence: toPence(ov), exPricePence: toPence(ovEx),
              }))}>
              Set store price
            </button>
          </div>
        )}

        <h4>HQ price history</h4>
        <table>
          <thead><tr><th>Effective from</th><th className="num">Price</th><th className="num">ex-VAT</th></tr></thead>
          <tbody>
            {detail.central.map((c, i) => (
              <tr key={i} className={i === 0 ? "" : "muted"}>
                <td>{new Date(c.effectiveFromUtc + "Z").toLocaleString("en-GB")}</td>
                <td className="num">{gbp(c.pricePence)}</td>
                <td className="num">{gbp(c.exPricePence)}</td>
              </tr>
            ))}
            {detail.central.length === 0 && <tr><td colSpan={3} className="muted">No entries — legacy price {gbp(detail.legacyPricePence)} applies.</td></tr>}
          </tbody>
        </table>

        {detail.overrides.length > 0 && (
          <>
            <h4>Live store prices</h4>
            <table>
              <thead><tr><th>Store</th><th className="num">Price</th><th>Since</th></tr></thead>
              <tbody>
                {detail.overrides.map((o, i) => (
                  <tr key={i}>
                    <td>{o.storeId}</td>
                    <td className="num">{gbp(o.pricePence)}</td>
                    <td>{new Date(o.effectiveFromUtc + "Z").toLocaleString("en-GB")}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </>
        )}

        <div className="dialog-actions"><button className="ghost" onClick={onClose}>Close</button></div>
      </div>
    </div>
  );
}
