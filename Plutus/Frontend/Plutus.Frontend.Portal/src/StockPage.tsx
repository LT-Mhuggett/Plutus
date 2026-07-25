import { useEffect, useState } from "react";
import { ApiError } from "./api.ts";

// WP5.2 stock screens: central view (all locations) / per-store view (location filter),
// per-item movements drill, manual adjustment, stock-take count, transfers + in-transit.

interface LevelRow { stockLocationId: string; location: string; itemIdOne: string; name: string | null; quantity: number }
interface LocationRow { id: string; storeId: number; type: string; name: string }
interface MovementRow { id: string; type: string; qtyDelta: number; reason: string | null; refId: string | null; atUtc: string }
interface TransferRow { id: string; fromLocationId: string; toLocationId: string; itemIdOne: string; qty: number; status: string; createdAtUtc: string }

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

export default function StockPage() {
  const [locations, setLocations] = useState<LocationRow[]>([]);
  const [locationId, setLocationId] = useState(""); // "" = central view
  const [search, setSearch] = useState("");
  const [levels, setLevels] = useState<LevelRow[]>([]);
  const [transfers, setTransfers] = useState<TransferRow[]>([]);
  const [drill, setDrill] = useState<LevelRow | null>(null);
  const [error, setError] = useState("");

  const refresh = () =>
    Promise.all([
      j<LevelRow[]>("GET", `/api/v1/stock/levels?take=200${locationId ? `&locationId=${locationId}` : ""}${search ? `&search=${encodeURIComponent(search)}` : ""}`),
      j<LocationRow[]>("GET", `/api/v1/stock/locations`),
      j<TransferRow[]>("GET", `/api/v1/stock/transfers?status=InTransit`),
    ])
      .then(([lv, lo, tr]) => { setLevels(lv); setLocations(lo); setTransfers(tr); setError(""); })
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));

  useEffect(() => { void refresh(); }, [locationId, search]); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <section className="panel">
      <div className="toolbar">
        <label>
          View{" "}
          <select value={locationId} onChange={(e) => setLocationId(e.target.value)}>
            <option value="">Central (all locations)</option>
            {locations.map((l) => <option key={l.id} value={l.id}>{l.name} ({l.type})</option>)}
          </select>
        </label>
        <label>Search <input placeholder="barcode / id" value={search} onChange={(e) => setSearch(e.target.value)} /></label>
        <NewLocation defaultStoreId={locations[0]?.storeId ?? 1} onCreated={refresh} />
      </div>
      {error && <p className="error">{error}</p>}

      {transfers.length > 0 && (
        <>
          <h3>In transit</h3>
          <table>
            <thead><tr><th>Item</th><th className="num">Qty</th><th>From → To</th><th>Since</th><th /></tr></thead>
            <tbody>
              {transfers.map((t) => (
                <tr key={t.id}>
                  <td>{t.itemIdOne}</td>
                  <td className="num">{t.qty}</td>
                  <td className="small">{locations.find((l) => l.id === t.fromLocationId)?.name} → {locations.find((l) => l.id === t.toLocationId)?.name}</td>
                  <td>{new Date(t.createdAtUtc + "Z").toLocaleString("en-GB")}</td>
                  <td>
                    <button className="ghost small" onClick={() => void j("POST", `/api/v1/stock/transfers/${t.id}/receive`).then(refresh).catch((e) => setError(String(e)))}>Receive</button>{" "}
                    <button className="ghost small" onClick={() => void j("POST", `/api/v1/stock/transfers/${t.id}/cancel`).then(refresh).catch((e) => setError(String(e)))}>Cancel</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}

      <table>
        <thead><tr><th>Item</th><th>Name</th><th>Location</th><th className="num">On hand</th><th /></tr></thead>
        <tbody>
          {levels.map((l) => (
            <tr key={`${l.stockLocationId}-${l.itemIdOne}`}>
              <td className="mono small">{l.itemIdOne}</td>
              <td>{l.name ?? <span className="muted">?</span>}</td>
              <td>{l.location}</td>
              <td className="num">{l.quantity}</td>
              <td><button className="ghost small" onClick={() => setDrill(l)}>Detail</button></td>
            </tr>
          ))}
          {levels.length === 0 && <tr><td colSpan={5} className="muted">No stock rows.</td></tr>}
        </tbody>
      </table>

      {drill && <ItemDialog level={drill} locations={locations} onClose={() => { setDrill(null); void refresh(); }} />}
    </section>
  );
}

/** WP11.3: create a stock location (notably a warehouse). */
function NewLocation({ defaultStoreId, onCreated }: { defaultStoreId: number; onCreated: () => void }) {
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  const [type, setType] = useState("Warehouse");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  if (!open) return <button className="ghost small" onClick={() => setOpen(true)}>+ New location</button>;
  return (
    <span className="new-location">
      <input placeholder="e.g. Back Warehouse" value={name} onChange={(e) => setName(e.target.value)} />
      <select value={type} onChange={(e) => setType(e.target.value)}>
        <option value="Warehouse">Warehouse</option>
        <option value="Store">Store</option>
      </select>
      <button className="primary small" disabled={busy || !name.trim()} onClick={async () => {
        setBusy(true); setError("");
        try {
          await j("POST", `/api/v1/stock/locations`, { storeId: defaultStoreId, type, name: name.trim() });
          setOpen(false); setName(""); onCreated();
        } catch (e) { setError(String(e instanceof Error ? e.message : e)); } finally { setBusy(false); }
      }}>Create</button>
      <button className="ghost small" onClick={() => setOpen(false)}>Cancel</button>
      {error && <span className="error small">{error}</span>}
    </span>
  );
}

function ItemDialog({ level, locations, onClose }: { level: LevelRow; locations: LocationRow[]; onClose: () => void }) {
  const [movements, setMovements] = useState<MovementRow[]>([]);
  const [error, setError] = useState("");
  const [adjustQty, setAdjustQty] = useState("");
  const [adjustReason, setAdjustReason] = useState("");
  const [counted, setCounted] = useState("");
  const [transferQty, setTransferQty] = useState("");
  const [transferTo, setTransferTo] = useState("");

  const refresh = () =>
    j<MovementRow[]>("GET", `/api/v1/stock/movements?itemIdOne=${encodeURIComponent(level.itemIdOne)}&locationId=${level.stockLocationId}&take=50`)
      .then(setMovements)
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));
  useEffect(() => { void refresh(); }, []); // eslint-disable-line react-hooks/exhaustive-deps

  const run = (p: Promise<unknown>) =>
    void p.then(refresh).catch((e) => setError(String(e instanceof Error ? e.message : e)));

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog">
        <h3>{level.itemIdOne} — {level.name ?? "?"} @ {level.location}</h3>
        {error && <p className="error small">{error}</p>}

        <div className="toolbar">
          <label>Adjust ± <input className="short" inputMode="numeric" value={adjustQty} onChange={(e) => setAdjustQty(e.target.value)} /></label>
          <label>Reason <input value={adjustReason} onChange={(e) => setAdjustReason(e.target.value)} /></label>
          <button className="ghost small" disabled={!adjustQty || !adjustReason}
            onClick={() => run(j("POST", `/api/v1/stock/movements`, { stockLocationId: level.stockLocationId, itemIdOne: level.itemIdOne, type: "Adjustment", qty: Number(adjustQty), reason: adjustReason }))}>
            Post adjustment
          </button>
        </div>
        <div className="toolbar">
          <label>Counted <input className="short" inputMode="numeric" value={counted} onChange={(e) => setCounted(e.target.value)} /></label>
          <button className="ghost small" disabled={counted === ""}
            onClick={() => run(j("POST", `/api/v1/stock/takes`, { stockLocationId: level.stockLocationId, counts: [{ itemIdOne: level.itemIdOne, counted: Number(counted) }] }))}>
            Record count
          </button>
          <label>Transfer <input className="short" inputMode="numeric" value={transferQty} onChange={(e) => setTransferQty(e.target.value)} /></label>
          <select value={transferTo} onChange={(e) => setTransferTo(e.target.value)}>
            <option value="">to…</option>
            {locations.filter((l) => l.id !== level.stockLocationId).map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
          </select>
          <button className="ghost small" disabled={!transferQty || !transferTo}
            onClick={() => run(j("POST", `/api/v1/stock/transfers`, { fromLocationId: level.stockLocationId, toLocationId: transferTo, itemIdOne: level.itemIdOne, qty: Number(transferQty), reason: "portal transfer" }))}>
            Dispatch
          </button>
        </div>

        <table>
          <thead><tr><th>When</th><th>Type</th><th className="num">Δ</th><th>Reason / ref</th></tr></thead>
          <tbody>
            {movements.map((m) => (
              <tr key={m.id}>
                <td>{new Date(m.atUtc + "Z").toLocaleString("en-GB")}</td>
                <td>{m.type}</td>
                <td className="num">{m.qtyDelta > 0 ? `+${m.qtyDelta}` : m.qtyDelta}</td>
                <td className="small">{m.reason ?? (m.refId ? `sale/transfer ${m.refId.slice(0, 8)}…` : "")}</td>
              </tr>
            ))}
          </tbody>
        </table>

        <div className="dialog-actions"><button className="ghost" onClick={onClose}>Close</button></div>
      </div>
    </div>
  );
}
