import { useEffect, useState } from "react";
import { ApiError, gbp } from "./api.ts";
import { accessToken } from "./auth.ts";
import DataTable from "./DataTable.tsx";
import DiscountRulesSection from "./DiscountRulesSection.tsx";
import DialogX from "./DialogX.tsx";
import { apiDateTime } from "./apiTime.ts";

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
interface PriceListRow { itemIdOne: string; name: string; category: string | null; policy: string; hqPence: number; overrides: number }
interface PriceList { total: number; skip: number; take: number; rows: PriceListRow[] }

async function j<T>(method: string, url: string, body?: unknown): Promise<T> {
  const token = accessToken();
  const res = await fetch(url, {
    method,
    headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(body !== undefined ? { "Content-Type": "application/json" } : {}) },
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
  // WP3.6 browsable price list (server-paged) so the page opens on the whole catalogue.
  const [list, setList] = useState<PriceList | null>(null);
  const [skip, setSkip] = useState(0);
  const [take, setTake] = useState(25);
  const [search, setSearch] = useState("");
  const [deviationsOnly, setDeviationsOnly] = useState(false);

  const refreshVariance = () =>
    j<VarianceRow[]>("GET", `/api/v1/prices/variance`).then(setVariance).catch((e) => setError(String(e)));
  const refreshList = () =>
    j<PriceList>("GET", `/api/v1/prices/list?skip=${skip}&take=${take}${search ? `&search=${encodeURIComponent(search)}` : ""}`)
      .then(setList).catch((e) => setError(String(e)));
  useEffect(() => { void refreshVariance(); }, []);
  useEffect(() => { void refreshList(); /* eslint-disable-next-line */ }, [skip, take, search]);

  const open = (id: string) =>
    j<PriceDetail>("GET", `/api/v1/prices/${encodeURIComponent(id)}`)
      .then((d) => { setDetail(d); setError(""); })
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));

  return (
    <>
      {/* ⚠ AT THE TOP — Matt, 2026-08-20: *"can discounts be moved to the top of the page and be
          called 'Discount Settings'"*. It sat under the price list, which on a shop with a full
          catalogue meant scrolling past every item to reach it. It stays COLLAPSED, so being first
          costs nothing to somebody who came here for prices. */}
      <DiscountRulesSection />
      {/* ⚠⚠ COLLAPSIBLE, LIKE DISCOUNT SETTINGS ABOVE IT (2026-08-21). Matt: *"Also Prices, can it be
          collapsed like discount settings please."*

          ⚠ AND IT OPENS BY DEFAULT (`open`), which Discount Settings does not — this is the Prices
          page, and a page whose main content is shut on arrival reads as broken. Collapsing it is for
          getting it out of the way to reach the discounts, not for hiding it. */}
      <details className="panel store-card" open>
      <summary><strong>Prices</strong></summary>
      <div className="toolbar">
        <label>Quick open (barcode / id) <input value={lookup} onChange={(e) => setLookup(e.target.value)} placeholder="5011921068203" /></label>
        <button className="primary small" disabled={!lookup.trim()} onClick={() => void open(lookup.trim())}>Open price editor</button>
        <span className="grow" />
        <label><input type="checkbox" checked={deviationsOnly} onChange={(e) => setDeviationsOnly(e.target.checked)} /> Only where stores deviate</label>
      </div>
      {error && <p className="error">{error}</p>}

      {!deviationsOnly && (
        <DataTable<PriceListRow>
          columns={[
            { key: "name", label: "Item", render: (r) => <><span className="mono small">{r.itemIdOne}</span> {r.name}</> },
            { key: "category", label: "Category", render: (r) => r.category ?? "—" },
            { key: "policy", label: "Policy" },
            { key: "hqPence", label: "HQ price", numeric: true, render: (r) => gbp(r.hqPence) },
            { key: "overrides", label: "Store prices", numeric: true },
          ]}
          rows={list?.rows ?? []} getKey={(r) => r.itemIdOne}
          server={{ total: list?.total ?? 0, skip, take, search, onSearch: (s) => { setSearch(s); setSkip(0); }, onPage: (sk, tk) => { setSkip(sk); setTake(tk); } }}
          rowActions={(r) => <button className="ghost small" onClick={() => void open(r.itemIdOne)}>Edit</button>}
          emptyText="No items."
        />
      )}

      {deviationsOnly && (<>
      <h3>Store price variance vs HQ</h3>
      <DataTable<VarianceRow>
        columns={[
          { key: "storeId", label: "Store", numeric: true },
          { key: "itemIdOne", label: "Item", render: (v) => <span className="mono small">{v.itemIdOne}</span> },
          { key: "name", label: "Name" },
          { key: "policy", label: "Policy" },
          { key: "hqPence", label: "HQ", numeric: true, render: (v) => gbp(v.hqPence) },
          { key: "storePence", label: "Store", numeric: true, render: (v) => gbp(v.storePence) },
          { key: "deltaPence", label: "Δ", numeric: true, render: (v) => `${v.deltaPence > 0 ? "+" : ""}${gbp(v.deltaPence)}` },
        ]}
        rows={variance} getKey={(v) => `${v.storeId}-${v.itemIdOne}`}
        initialSortKey="deltaPence" initialSortDir="desc"
        search={(v) => `${v.storeId} ${v.itemIdOne} ${v.name} ${v.policy}`}
        searchPlaceholder="Search item / name / policy…"
        rowActions={(v) => <button className="ghost small" onClick={() => void open(v.itemIdOne)}>Edit</button>}
        emptyText="No store deviates from the HQ price."
      />
      </>)}

      {detail && (
        <PriceDialog detail={detail} onClose={() => { setDetail(null); void refreshVariance(); void refreshList(); }}
          onChanged={() => void open(detail.itemIdOne)} />
      )}
      </details>
    </>
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
        <DialogX onClose={onClose} />
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
                <td>{apiDateTime(c.effectiveFromUtc)}</td>
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
                    <td>{apiDateTime(o.effectiveFromUtc)}</td>
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
