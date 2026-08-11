import { useEffect, useState } from "react";
import { ApiError } from "./api.ts";
import { accessToken } from "./auth.ts";
import DataTable from "./DataTable.tsx";

// WP5.2 stock screens: central view (all locations) / per-store view (location filter),
// per-item movements drill, manual adjustment, stock-take count, transfers + in-transit.

interface LevelRow { stockLocationId: string; location: string; itemIdOne: string; name: string | null; quantity: number }
interface LocationRow { id: string; storeId: number; type: string; name: string }
interface MovementRow { id: string; type: string; qtyDelta: number; reason: string | null; refId: string | null; atUtc: string }
interface TransferRow { id: string; fromLocationId: string; toLocationId: string; itemIdOne: string; qty: number; status: string; createdAtUtc: string }

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

interface StockResp { totalCatalogueItems: number; inStock: number; matched: number; skip: number; take: number; rows: LevelRow[] }

export default function StockPage() {
  const [locations, setLocations] = useState<LocationRow[]>([]);
  const [locationId, setLocationId] = useState(""); // "" = central view
  const [search, setSearch] = useState("");
  const [take, setTake] = useState(25);
  const [skip, setSkip] = useState(0);
  const [stock, setStock] = useState<StockResp | null>(null);
  const [transfers, setTransfers] = useState<TransferRow[]>([]);
  const [drill, setDrill] = useState<LevelRow | null>(null);
  const [error, setError] = useState("");

  const refresh = () =>
    Promise.all([
      j<StockResp>("GET", `/api/v1/stock/levels?skip=${skip}&take=${take}${locationId ? `&locationId=${locationId}` : ""}${search ? `&search=${encodeURIComponent(search)}` : ""}`),
      j<LocationRow[]>("GET", `/api/v1/stock/locations`),
      j<TransferRow[]>("GET", `/api/v1/stock/transfers?status=InTransit`),
    ])
      .then(([lv, lo, tr]) => { setStock(lv); setLocations(lo); setTransfers(tr); setError(""); })
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));

  useEffect(() => { setSkip(0); }, [locationId]);
  // debounced: the DataTable search box fires per keystroke
  useEffect(() => {
    const t = setTimeout(() => void refresh(), search ? 300 : 0);
    return () => clearTimeout(t);
  }, [locationId, search, take, skip]); // eslint-disable-line react-hooks/exhaustive-deps

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
        {/* FE4.3: search + page size now live in the DataTable below (server mode) */}
        <NewLocation defaultStoreId={locations[0]?.storeId ?? 1} onCreated={refresh} />
      </div>
      {stock && (
        <p className="muted small">
          {stock.inStock.toLocaleString()} in stock{locationId ? " at this location" : ""} · {stock.matched.toLocaleString()} with a stock record · {stock.totalCatalogueItems.toLocaleString()} products in the catalogue
        </p>
      )}
      {error && <p className="error">{error}</p>}

      {transfers.length > 0 && (
        <>
          <h3>In transit</h3>
          <DataTable<TransferRow>
            columns={[
              { key: "itemIdOne", label: "Item", render: (t) => <span className="mono small">{t.itemIdOne}</span> },
              { key: "qty", label: "Qty", numeric: true },
              {
                key: "fromLocationId", label: "From → To",
                render: (t) => <span className="small">{locations.find((l) => l.id === t.fromLocationId)?.name} → {locations.find((l) => l.id === t.toLocationId)?.name}</span>,
              },
              { key: "createdAtUtc", label: "Since", render: (t) => new Date(t.createdAtUtc + "Z").toLocaleString("en-GB") },
            ]}
            rows={transfers} getKey={(t) => t.id} initialSortKey="createdAtUtc" initialSortDir="desc"
            search={(t) => t.itemIdOne}
            searchPlaceholder="Search item…"
            rowActions={(t) => (
              <>
                <button className="ghost small" onClick={() => void j("POST", `/api/v1/stock/transfers/${t.id}/receive`).then(refresh).catch((e) => setError(String(e)))}>Receive</button>{" "}
                <button className="ghost small" onClick={() => void j("POST", `/api/v1/stock/transfers/${t.id}/cancel`).then(refresh).catch((e) => setError(String(e)))}>Cancel</button>
              </>
            )}
            emptyText="Nothing in transit."
          />
        </>
      )}

      {/* FE4.3: the exemplar server-paged list becomes the standard DataTable — same endpoint
          (skip/take/search), now with the shared toolbar, page-size select and "X–Y of N". */}
      <DataTable<LevelRow>
        columns={[
          { key: "itemIdOne", label: "Item", render: (l) => <span className="mono small">{l.itemIdOne}</span> },
          { key: "name", label: "Name", render: (l) => l.name ?? <span className="muted">?</span> },
          { key: "location", label: "Location" },
          { key: "quantity", label: "On hand", numeric: true },
        ]}
        rows={stock?.rows ?? []}
        getKey={(l) => `${l.stockLocationId}-${l.itemIdOne}`}
        server={{
          total: stock?.matched ?? 0, skip, take, search,
          onSearch: (s) => { setSkip(0); setSearch(s); },
          onPage: (s, t) => { setSkip(s); setTake(t); },
        }}
        searchPlaceholder="Search barcode / id…"
        rowActions={(l) => <button className="ghost small" onClick={() => setDrill(l)}>Detail</button>}
        emptyText="No stock rows."
      />

      <AdjustmentsReport locations={locations} />

      {drill && <ItemDialog level={drill} locations={locations} onClose={() => { setDrill(null); void refresh(); }} />}
    </section>
  );
}

interface AdjustmentRow {
  id: string; atUtc: string; type: string;
  itemIdOne: string; itemName: string | null;
  locationId: string; location: string;
  qtyDelta: number; reason: string | null;
  actorUserId: string | null; actor: string;
}
interface AdjustmentsResp { from: string; to: string; truncated: boolean; rows: AdjustmentRow[] }

const isoDay = (d: Date) => d.toISOString().slice(0, 10);

/**
 * The stock ADJUSTMENTS report — who changed a count, by how much, and why.
 *
 * Matt, 2026-08-11: "Writing off stock, where is this captured? I need a report on the portal
 * (that will then be reflected in all tills) that shows stock adjustments."
 *
 * It was always captured — every write-off has been a StockMovement row with its reason, actor and
 * timestamp since WP5.1. What did not exist was a way to READ it as a report: /stock/movements is a
 * per-item drill with no date range that returns raw GUIDs, so "who has been writing stock off this
 * month?" — the question shrinkage is found by — had no answer.
 *
 * It lives on the Stock page rather than in Reporting on purpose: this is where somebody already is
 * when they are thinking about stock, and a report nobody navigates to is the problem we just fixed
 * for drawer variances.
 */
function AdjustmentsReport({ locations }: { locations: LocationRow[] }) {
  const [from, setFrom] = useState(isoDay(new Date(Date.now() - 30 * 86400_000)));
  const [to, setTo] = useState(isoDay(new Date()));
  const [locationId, setLocationId] = useState("");
  const [data, setData] = useState<AdjustmentsResp | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    setError("");
    j<AdjustmentsResp>(
      "GET",
      `/api/v1/stock/adjustments?from=${from}&to=${to}&take=1000${locationId ? `&locationId=${locationId}` : ""}`,
    ).then(setData).catch((e) => setError(String(e)));
  }, [from, to, locationId]);

  // Written off vs added back, counted separately — a stock take that corrected +5 on one item and
  // −5 on another is not a nil event, and one netted number would report it as one.
  const out = (data?.rows ?? []).filter((r) => r.qtyDelta < 0).reduce((n, r) => n + -r.qtyDelta, 0);
  const back = (data?.rows ?? []).filter((r) => r.qtyDelta > 0).reduce((n, r) => n + r.qtyDelta, 0);

  return (
    <>
      <h3>Stock adjustments</h3>
      <p className="muted small">
        Every manual change to a count — write-offs and stock takes. Sales, returns, transfers and
        goods-in are movements too and are deliberately not here; they would bury these.
      </p>
      <div className="toolbar">
        <label>From <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label>
        <label>To <input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label>
        <label>
          Location{" "}
          <select value={locationId} onChange={(e) => setLocationId(e.target.value)}>
            <option value="">All locations</option>
            {locations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
          </select>
        </label>
      </div>
      {error && <p className="error">{error}</p>}
      {data && (
        <p className="muted small">
          {data.rows.length.toLocaleString()} adjustment{data.rows.length === 1 ? "" : "s"} ·{" "}
          <strong>{out.toLocaleString()}</strong> written off · <strong>{back.toLocaleString()}</strong> added back
          {/* Never let a capped page read as "that is all of them". */}
          {data.truncated && <span className="error"> · ⚠ capped — narrow the dates to see the rest</span>}
        </p>
      )}
      <DataTable<AdjustmentRow>
        columns={[
          { key: "atUtc", label: "When", render: (r) => new Date(r.atUtc + "Z").toLocaleString("en-GB") },
          { key: "type", label: "Type" },
          { key: "itemIdOne", label: "Item", render: (r) => <span className="mono small">{r.itemIdOne}</span> },
          { key: "itemName", label: "Name", render: (r) => r.itemName ?? <span className="muted">?</span> },
          { key: "location", label: "Location" },
          {
            key: "qtyDelta", label: "Change", numeric: true,
            // Negative in red: a count going DOWN is the direction that costs money.
            render: (r) => (
              <span className={r.qtyDelta < 0 ? "error" : undefined}>
                {r.qtyDelta > 0 ? `+${r.qtyDelta}` : r.qtyDelta}
              </span>
            ),
          },
          { key: "reason", label: "Reason", render: (r) => r.reason ?? <span className="muted">none given</span> },
          // "unknown" comes from the server for movements that carry no user — shown, never hidden.
          { key: "actor", label: "Who", render: (r) => r.actor === "unknown" ? <span className="muted">unknown</span> : r.actor },
        ]}
        rows={data?.rows ?? []}
        getKey={(r) => r.id}
        initialSortKey="atUtc"
        initialSortDir="desc"
        search={(r) => `${r.itemIdOne} ${r.itemName ?? ""} ${r.reason ?? ""} ${r.actor}`}
        searchPlaceholder="Search item / reason / who…"
        emptyText="No stock adjustments in this range."
      />
    </>
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

        {/* the movement ledger for one item — a genuine browsable history, so it gets the
            standard table even though it lives in a dialog */}
        <DataTable<MovementRow>
          columns={[
            { key: "atUtc", label: "When", render: (m) => new Date(m.atUtc + "Z").toLocaleString("en-GB") },
            { key: "type", label: "Type" },
            { key: "qtyDelta", label: "Δ", numeric: true, render: (m) => (m.qtyDelta > 0 ? `+${m.qtyDelta}` : String(m.qtyDelta)) },
            { key: "reason", label: "Reason / ref", render: (m) => <span className="small">{m.reason ?? (m.refId ? `sale/transfer ${m.refId.slice(0, 8)}…` : "")}</span> },
          ]}
          rows={movements} getKey={(m) => m.id} initialSortKey="atUtc" initialSortDir="desc"
          search={(m) => `${m.type} ${m.reason ?? ""}`}
          searchPlaceholder="Search type / reason…"
          emptyText="No movements recorded."
        />

        <div className="dialog-actions"><button className="ghost" onClick={onClose}>Close</button></div>
      </div>
    </div>
  );
}
