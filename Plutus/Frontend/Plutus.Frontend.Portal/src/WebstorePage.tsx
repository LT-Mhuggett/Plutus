import { useEffect, useState } from "react";
import {
  bindSku, createItemFromSku, createWebstoreConnection, disconnectWebstore, downloadCsv,
  fetchAlignment, fetchConnectorHealth, fetchOutboundLog, fetchSkuMap, fetchWebstoreProducts, fetchWebstores, gbp,
  ignoreSku, refreshWebstoreProducts, retryParkedOrders, setOutboundMode,
  type AlignmentResp, type AlignmentRow, type ConnectorRow, type OutboundLogResp, type OutboundLogRow,
  type SkuMapRow, type WebstoreConn, type WebstoreProductRow, type WebstoreProductsResp,
} from "./api.ts";
import DataTable from "./DataTable.tsx";
import { ask } from "./Ask.tsx";
import DialogX from "./DialogX.tsx";
import { apiDateTime } from "./apiTime.ts";

// FE4.3 row aliases for the alignment tables (the API groups them under AlignmentResp).
type PriceDiffRow = AlignmentRow;
type NameDriftRow = AlignmentRow;
type WebOnlyRow = AlignmentResp["webOnly"][number];

/** Phase 6 webstore connector: connection status, the WP6.2 SKU review queue (bind / ignore /
 *  create-as-item + retry parked orders), the WP6.4 catalogue view (cached — never hits the live
 *  site at render), and the alignment report (prices on BOTH platforms are display, not errors). */
export default function WebstorePage() {
  const [conns, setConns] = useState<WebstoreConn[]>([]);
  const [error, setError] = useState("");
  const [sub, setSub] = useState<"queue" | "catalogue" | "alignment" | "outbound">("queue");
  const [health, setHealth] = useState<ConnectorRow | null>(null);

  useEffect(() => { fetchWebstores().then(setConns).catch((e) => setError(String(e))); }, []);
  // WP17.1 connector health for this tenant (the "woo" row, if any).
  useEffect(() => { fetchConnectorHealth().then((rows) => setHealth(rows.find((r) => r.connector === "woo") ?? null)).catch(() => undefined); }, []);
  const conn = conns[0];   // one connection today; the list API is ready for more

  return (
    <section className="panel">
      <h2>Webstore</h2>
      {error && <p className="error">{error}</p>}
      {!conn && !error && <ConnectForm />}
      {conn && (
        <>
          <div className="stat-row">
            <div className="stat"><span className="stat-label">Connection</span>
              <span className="stat-value">{conn.name}</span>
              <span className="muted small">{conn.enabled ? "enabled" : "disabled"} · {conn.url}</span></div>
            <div className="stat"><span className="stat-label">Pending SKUs</span>
              <span className="stat-value">{conn.pendingSkus}</span>
              <span className="muted small">awaiting review</span></div>
            <div className="stat"><span className="stat-label">Orders synced to</span>
              <span className="stat-value small">{conn.ordersCursorUtc ? apiDateTime(conn.ordersCursorUtc) : "—"}</span></div>
            <div className="stat"><span className="stat-label">Catalogue swept</span>
              <span className="stat-value small">{conn.lastFullProductSweepUtc ? apiDateTime(conn.lastFullProductSweepUtc) : "pending first sweep"}</span></div>
            {health && (
              <div className="stat"><span className="stat-label">Connector health</span>
                <span className="stat-value small" style={{ color: health.silent || health.errorStreak > 0 ? "#dc2626" : "#16a34a" }}>
                  {health.silent ? "silent" : health.errorStreak > 0 ? `${health.errorStreak} error(s)` : "healthy"}
                </span>
                <span className="muted small">last poll {health.lastPollAtUtc ? apiDateTime(health.lastPollAtUtc) : "—"}</span></div>
            )}
            <div className="stat">
              <button className="ghost small" onClick={async () => {
                if (!await ask.confirm({
                  title: `Disconnect “${conn.name}”?`,
                  body: (
                    <>
                      <p className="small">Its webhooks are removed from the site and syncing stops.</p>
                      <p className="muted small">Sales already ingested are kept — nothing is deleted.</p>
                    </>
                  ),
                  confirmLabel: "Disconnect",
                  danger: true,
                })) return;
                void disconnectWebstore(conn.id).then(() => fetchWebstores().then(setConns)).catch((e) => setError(String(e)));
              }}>Disconnect</button>
            </div>
          </div>

          <div className="subtabs">
            {(["queue", "catalogue", "alignment", "outbound"] as const).map((t) => (
              <button key={t} className={t === sub ? "subtab active" : "subtab"} onClick={() => setSub(t)}>
                {t === "queue" ? `Review queue${conn.pendingSkus ? ` (${conn.pendingSkus})` : ""}`
                  : t === "catalogue" ? "Webstore catalogue" : t === "alignment" ? "Alignment" : "Outbound"}
              </button>
            ))}
          </div>

          {sub === "queue" && <ReviewQueue id={conn.id} />}
          {sub === "catalogue" && <Catalogue id={conn.id} />}
          {sub === "alignment" && <Alignment id={conn.id} />}
          {sub === "outbound" && <Outbound id={conn.id} />}
        </>
      )}
    </section>
  );
}

/** WP6.1 one-click connect: name + site URL → redirect to the store's OWN WordPress login +
 *  WooCommerce approval screen. Keys come back server-to-server; webhooks self-provision. */
function ConnectForm() {
  const [name, setName] = useState("");
  const [url, setUrl] = useState("https://");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const justConnected = new URLSearchParams(window.location.search).get("connected") === "1";
  return (
    <div className="card">
      {justConnected && <p className="callout">Connection approved — the store is provisioning. Refresh in a few seconds.</p>}
      <h3>Connect a WooCommerce webstore</h3>
      <p className="muted small">
        You'll be sent to the store's own WordPress login to approve the connection — no keys to
        copy, nothing to install on the site.
      </p>
      <div className="toolbar">
        <label>Name <input placeholder="e.g. Kapow Comics Web" value={name} onChange={(e) => setName(e.target.value)} /></label>
        <label className="grow">Site URL <input placeholder="https://www.example.co.uk" value={url} onChange={(e) => setUrl(e.target.value)} /></label>
        <button className="primary" disabled={busy || !name.trim() || !url.startsWith("https://")}
          onClick={() => {
            setBusy(true); setError("");
            createWebstoreConnection(name.trim(), url.trim())
              .then((r) => { window.location.href = r.authorizeUrl; })
              .catch((e) => { setError(String(e)); setBusy(false); });
          }}>
          {busy ? "Redirecting…" : "Connect"}
        </button>
      </div>
      {error && <p className="error small">{error}</p>}
    </div>
  );
}

function ReviewQueue({ id }: { id: string }) {
  const [rows, setRows] = useState<SkuMapRow[]>([]);
  const [status, setStatus] = useState("Pending");
  const [error, setError] = useState("");
  const [msg, setMsg] = useState("");
  const [bindFor, setBindFor] = useState<SkuMapRow | null>(null);
  const [barcode, setBarcode] = useState("");

  const refresh = () => fetchSkuMap(id, status || undefined).then(setRows).catch((e) => setError(String(e)));
  useEffect(() => { void refresh(); }, [status]); // eslint-disable-line react-hooks/exhaustive-deps

  const act = (p: Promise<unknown>, done: string) => {
    setError(""); setMsg("");
    p.then(() => { setMsg(done); setBindFor(null); return refresh(); }).catch((e) => setError(String(e)));
  };

  return (
    <div>
      <div className="toolbar">
        <label>Status
          <select value={status} onChange={(e) => setStatus(e.target.value)}>
            <option>Pending</option><option>Bound</option><option>Ignored</option><option value="">All</option>
          </select>
        </label>
        <span className="grow" />
        <button className="ghost small" onClick={() =>
          act(retryParkedOrders(id).then((r) => setMsg(`Retry: ${r.recorded} recorded, ${r.still} still parked.`)), "")}>
          Retry parked orders
        </button>
      </div>
      {error && <p className="error small">{error}</p>}
      {msg && <p className="callout small">{msg}</p>}
      <DataTable<SkuMapRow>
        columns={[
          { key: "sku", label: "SKU", render: (r) => <span className="mono small">{r.sku}</span> },
          {
            key: "web", label: "Webstore says", sortable: false,
            render: (r) => <span className="small">{r.web ? <>{r.web.name} · {gbp(r.web.pricePence)} · {r.web.status}</> : <span className="muted">not in cache yet</span>}</span>,
          },
          { key: "seenCount", label: "Seen", numeric: true },
          { key: "status", label: "Status", render: (r) => <>{r.status}{r.boundItemIdOne ? <span className="muted small"> → {r.boundItemIdOne}</span> : null}</> },
        ]}
        rows={rows} getKey={(r) => r.id} initialSortKey="seenCount" initialSortDir="desc"
        search={(r) => `${r.sku} ${r.web?.name ?? ""} ${r.status} ${r.boundItemIdOne ?? ""}`}
        searchPlaceholder="Search SKU / name / status…"
        rowActions={(r) => r.status === "Pending" ? (
          <>
            <button className="ghost small" onClick={() => { setBindFor(r); setBarcode(""); }}>Bind…</button>{" "}
            <button className="ghost small" title="Create a till item from the webstore's name/price"
              onClick={() => act(createItemFromSku(id, r.id), `Created item ${r.sku}.`)}>Create item</button>{" "}
            <button className="ghost small" onClick={() => act(ignoreSku(id, r.id), `Ignored ${r.sku}.`)}>Ignore</button>
          </>
        ) : null}
        emptyText="Nothing here."
      />

      {bindFor && (
        <div className="overlay" onClick={(e) => e.target === e.currentTarget && setBindFor(null)}>
          <div className="dialog">
            <DialogX onClose={() => setBindFor(null)} />
            <h3>Bind {bindFor.sku}</h3>
            <p className="muted small">Enter the existing catalogue item's barcode this web SKU corresponds to.</p>
            <label>Barcode <input autoFocus value={barcode} onChange={(e) => setBarcode(e.target.value)} /></label>
            <div className="dialog-actions">
              <button className="ghost" onClick={() => setBindFor(null)}>Cancel</button>
              <button className="primary" disabled={!barcode.trim()}
                onClick={() => act(bindSku(id, bindFor.id, barcode.trim()), `Bound ${bindFor.sku} → ${barcode.trim()}.`)}>Bind</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function Catalogue({ id }: { id: string }) {
  const [resp, setResp] = useState<WebstoreProductsResp | null>(null);
  const [status, setStatus] = useState("");
  const [linked, setLinked] = useState("");
  const [take, setTake] = useState(25);
  const [skip, setSkip] = useState(0);
  const [search, setSearch] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const refresh = () =>
    fetchWebstoreProducts(id, { status: status || undefined, linked: linked || undefined, search: search || undefined, skip, take })
      .then(setResp).catch((e) => setError(String(e)));
  // debounced — the DataTable search box fires per keystroke
  useEffect(() => {
    const t = setTimeout(() => void refresh(), search ? 300 : 0);
    return () => clearTimeout(t);
  }, [status, linked, search, skip, take]); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <div>
      <div className="toolbar">
        <label>Status
          <select value={status} onChange={(e) => { setSkip(0); setStatus(e.target.value); }}>
            <option value="">All</option><option value="publish">Published</option>
            <option value="draft">Draft</option><option value="deleted">Deleted (gone from site)</option>
          </select>
        </label>
        <label>Linked to till item
          <select value={linked} onChange={(e) => { setSkip(0); setLinked(e.target.value); }}>
            <option value="">All</option><option value="yes">Linked</option><option value="no">Unlinked</option>
          </select>
        </label>
        <span className="grow" />
        <span className="muted small">
          {resp?.lastRefreshed ? `Last refreshed ${apiDateTime(resp.lastRefreshed)}` : "not swept yet"}
        </span>
        <button className="ghost small" disabled={busy} onClick={() => {
          setBusy(true); setError("");
          refreshWebstoreProducts(id).then(() => refresh()).catch((e) => setError(String(e))).finally(() => setBusy(false));
        }}>{busy ? "Refreshing…" : "Refresh now"}</button>
      </div>
      {error && <p className="error small">{error}</p>}
      <DataTable<WebstoreProductRow>
        columns={[
          { key: "name", label: "Name", render: (p) => <span className={p.status === "deleted" ? "muted" : undefined}>{p.name}</span> },
          { key: "sku", label: "SKU", render: (p) => <span className="mono small">{p.sku ?? <span className="muted">none</span>}</span> },
          {
            key: "pricePence", label: "Web price", numeric: true,
            render: (p) => <>{gbp(p.pricePence)}{p.regularPricePence != null && p.regularPricePence !== p.pricePence && (
              <span className="muted small"> (was {gbp(p.regularPricePence)})</span>)}</>,
          },
          { key: "stockQuantity", label: "Stock", numeric: true, render: (p) => <>{p.stockQuantity ?? "—"} <span className="muted small">{p.stockStatus ?? ""}</span></> },
          { key: "status", label: "Status" },
          { key: "linkedItem", label: "Till item", render: (p) => (p.linkedItem ? <span className="chip ok">linked</span> : <span className="chip bad">unlinked</span>) },
        ]}
        rows={resp?.rows ?? []} getKey={(p) => String(p.wooProductId)}
        server={{
          total: resp?.total ?? 0, skip, take, search,
          onSearch: (s) => { setSkip(0); setSearch(s); },
          onPage: (s, t) => { setSkip(s); setTake(t); },
        }}
        searchPlaceholder="Search name / SKU…"
        rowActions={(p) => p.permalink ? <a className="small" href={p.permalink} target="_blank" rel="noreferrer">view on site</a> : null}
        emptyText="No products match."
      />
    </div>
  );
}

function Outbound({ id }: { id: string }) {
  const [data, setData] = useState<OutboundLogResp | null>(null);
  const [error, setError] = useState("");
  const [msg, setMsg] = useState("");
  const refresh = () => fetchOutboundLog(id).then(setData).catch((e) => setError(String(e)));
  useEffect(() => { void refresh(); }, [id]); // eslint-disable-line react-hooks/exhaustive-deps

  const switchMode = async (mode: string) => {
    if (mode === "live" && !await ask.confirm({
      title: "Go LIVE with outbound writes?",
      body: (
        <>
          <p className="small">
            Plutus will start <strong>writing</strong> stock levels and draft products to the webstore.
          </p>
          <p className="muted small">
            Only do this after reviewing the dry-run journal below, and with the WRITE REST key
            configured. You can switch back to “off” at any time — it's a kill switch.
          </p>
        </>
      ),
      confirmLabel: "Go live",
      danger: true,
    })) return;
    setError(""); setMsg("");
    setOutboundMode(id, mode).then(() => { setMsg(`Outbound mode set to ${mode}.`); return refresh(); })
      .catch((e) => setError(String(e)));
  };

  if (error && !data) return <p className="error">{error}</p>;
  if (!data) return <p className="muted">Loading…</p>;
  return (
    <div>
      <div className="toolbar">
        <label>Outbound mode
          <select value={data.mode || "off"} onChange={(e) => void switchMode(e.target.value)}>
            <option value="off">Off (kill switch)</option>
            <option value="dry-run">Dry-run — journal only, send nothing</option>
            <option value="live">LIVE — write to the webstore</option>
          </select>
        </label>
        <span className="muted small grow">
          Dry-run journals exactly what WOULD be sent. Review it for a week of real trading before going live (plan WP6.3).
        </span>
        <span className="muted small">{data.pendingDry} dry-run entries</span>
      </div>
      {error && <p className="error small">{error}</p>}
      {msg && <p className="callout small">{msg}</p>}
      <DataTable<OutboundLogRow>
        columns={[
          { key: "createdAtUtc", label: "When", render: (r) => <span className="small">{apiDateTime(r.createdAtUtc)}</span> },
          { key: "kind", label: "Kind" },
          { key: "lane", label: "Lane" },
          { key: "itemIdOne", label: "Item", render: (r) => <span className="mono small">{r.itemIdOne}</span> },
          { key: "fromValue", label: "From", numeric: true, render: (r) => r.fromValue ?? "—" },
          { key: "toValue", label: "To", render: (r) => r.toValue ?? "—" },
          { key: "mode", label: "Mode" },
          { key: "result", label: "Result", render: (r) => <span className={r.result.startsWith("failed") ? "error small" : "small"}>{r.result}</span> },
        ]}
        rows={data.rows} getKey={(r) => String(r.id)} initialSortKey="createdAtUtc" initialSortDir="desc"
        search={(r) => `${r.kind} ${r.lane} ${r.itemIdOne} ${r.mode} ${r.result}`}
        searchPlaceholder="Search item / kind / result…"
        emptyText="Nothing journaled yet — set mode to dry-run and the next sale / poll cycle starts writing entries."
      />
    </div>
  );
}

function Alignment({ id }: { id: string }) {
  const [data, setData] = useState<AlignmentResp | null>(null);
  const [error, setError] = useState("");
  useEffect(() => { fetchAlignment(id).then(setData).catch((e) => setError(String(e))); }, [id]);
  if (error) return <p className="error">{error}</p>;
  if (!data) return <p className="muted">Loading…</p>;
  return (
    <div>
      <div className="stat-row">
        <div className="stat"><span className="stat-label">Matched by SKU</span><span className="stat-value">{data.matched}</span></div>
        <div className="stat"><span className="stat-label">Name drift</span><span className="stat-value">{data.nameDrift.length}</span></div>
        <div className="stat"><span className="stat-label">Price differs</span><span className="stat-value">{data.priceDiffers.length}</span><span className="muted small">legitimate — web ≠ shelf</span></div>
        <div className="stat"><span className="stat-label">Web-only products</span><span className="stat-value">{data.webOnly.length}</span></div>
      </div>

      <div className="toolbar">
        <span className="grow" />
        <button className="ghost small" onClick={() =>
          void downloadCsv(`/api/v1/webstores/${id}/alignment.csv`, "webstore-alignment.csv")}>
          Export CSV
        </button>
      </div>
      <h3>Prices on both platforms</h3>
      <DataTable<PriceDiffRow>
        columns={[
          { key: "sku", label: "SKU", render: (r) => <span className="mono small">{r.sku}</span> },
          { key: "webName", label: "Name (web)" },
          { key: "webPricePence", label: "Web price", numeric: true, render: (r) => gbp(r.webPricePence) },
          { key: "tillPricePence", label: "Till price", numeric: true, render: (r) => gbp(r.tillPricePence) },
          { key: "priceDiffPence", label: "Difference", numeric: true, render: (r) => `${r.priceDiffPence > 0 ? "+" : ""}${gbp(r.priceDiffPence)}` },
        ]}
        rows={data.priceDiffers} getKey={(r) => r.sku} initialSortKey="priceDiffPence" initialSortDir="desc"
        search={(r) => `${r.sku} ${r.webName}`}
        searchPlaceholder="Search SKU / name…"
        emptyText="All matched prices agree."
      />

      <h3>Name drift</h3>
      <DataTable<NameDriftRow>
        columns={[
          { key: "sku", label: "SKU", render: (r) => <span className="mono small">{r.sku}</span> },
          { key: "webName", label: "Webstore name" },
          { key: "tillName", label: "Till name" },
        ]}
        rows={data.nameDrift} getKey={(r) => r.sku} initialSortKey="sku"
        search={(r) => `${r.sku} ${r.webName} ${r.tillName}`}
        searchPlaceholder="Search SKU / name…"
        emptyText="No drift."
      />

      {/* FE4.3: paging replaces the old hard slice(0, 200) — the full list is now reachable. */}
      <h3>On the webstore but not in the till catalogue ({data.webOnly.length})</h3>
      <DataTable<WebOnlyRow>
        columns={[
          { key: "sku", label: "SKU", render: (r) => <span className="mono small">{r.sku ?? "—"}</span> },
          { key: "name", label: "Name" },
          { key: "pricePence", label: "Web price", numeric: true, render: (r) => gbp(r.pricePence) },
          { key: "status", label: "Status" },
        ]}
        rows={data.webOnly} getKey={(r) => `${r.sku ?? "nosku"}-${r.name}`} initialSortKey="name"
        search={(r) => `${r.sku ?? ""} ${r.name} ${r.status}`}
        searchPlaceholder="Search SKU / name / status…"
        emptyText="None."
      />
      <p className="muted small">{data.tillOnlyCount.toLocaleString("en-GB")} till items are not on the webstore (normal — the web range is curated).</p>
    </div>
  );
}
