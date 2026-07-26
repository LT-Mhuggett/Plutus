import { useEffect, useState } from "react";
import {
  bindSku, createItemFromSku, fetchAlignment, fetchSkuMap, fetchWebstoreProducts, fetchWebstores,
  gbp, ignoreSku, refreshWebstoreProducts, retryParkedOrders,
  type AlignmentResp, type SkuMapRow, type WebstoreConn, type WebstoreProductsResp,
} from "./api.ts";
import { SortTh, useSort } from "./sortable.tsx";

/** Phase 6 webstore connector: connection status, the WP6.2 SKU review queue (bind / ignore /
 *  create-as-item + retry parked orders), the WP6.4 catalogue view (cached — never hits the live
 *  site at render), and the alignment report (prices on BOTH platforms are display, not errors). */
export default function WebstorePage() {
  const [conns, setConns] = useState<WebstoreConn[]>([]);
  const [error, setError] = useState("");
  const [sub, setSub] = useState<"queue" | "catalogue" | "alignment">("queue");

  useEffect(() => { fetchWebstores().then(setConns).catch((e) => setError(String(e))); }, []);
  const conn = conns[0];   // one connection today; the list API is ready for more

  return (
    <section className="panel">
      <h2>Webstore</h2>
      {error && <p className="error">{error}</p>}
      {!conn && !error && <p className="muted">No webstore connected yet.</p>}
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
              <span className="stat-value small">{conn.ordersCursorUtc ? new Date(conn.ordersCursorUtc + "Z").toLocaleString("en-GB") : "—"}</span></div>
            <div className="stat"><span className="stat-label">Catalogue swept</span>
              <span className="stat-value small">{conn.lastFullProductSweepUtc ? new Date(conn.lastFullProductSweepUtc + "Z").toLocaleString("en-GB") : "pending first sweep"}</span></div>
          </div>

          <div className="subtabs">
            {(["queue", "catalogue", "alignment"] as const).map((t) => (
              <button key={t} className={t === sub ? "subtab active" : "subtab"} onClick={() => setSub(t)}>
                {t === "queue" ? `Review queue${conn.pendingSkus ? ` (${conn.pendingSkus})` : ""}` : t === "catalogue" ? "Webstore catalogue" : "Alignment"}
              </button>
            ))}
          </div>

          {sub === "queue" && <ReviewQueue id={conn.id} />}
          {sub === "catalogue" && <Catalogue id={conn.id} />}
          {sub === "alignment" && <Alignment id={conn.id} />}
        </>
      )}
    </section>
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

  const s = useSort(rows, "updatedAtUtc", "desc");
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
      <table>
        <thead><tr>
          <SortTh label="SKU" k="sku" {...s} />
          <th>Webstore says</th>
          <SortTh label="Seen" k="seenCount" {...s} />
          <SortTh label="Status" k="status" {...s} />
          <th />
        </tr></thead>
        <tbody>
          {s.sorted.map((r) => (
            <tr key={r.id}>
              <td className="mono small">{r.sku}</td>
              <td className="small">{r.web ? <>{r.web.name} · {gbp(r.web.pricePence)} · {r.web.status}</> : <span className="muted">not in cache yet</span>}</td>
              <td className="num">{r.seenCount}</td>
              <td>{r.status}{r.boundItemIdOne ? <span className="muted small"> → {r.boundItemIdOne}</span> : null}</td>
              <td>
                {r.status === "Pending" && (
                  <>
                    <button className="ghost small" onClick={() => { setBindFor(r); setBarcode(""); }}>Bind…</button>{" "}
                    <button className="ghost small" title="Create a till item from the webstore's name/price"
                      onClick={() => act(createItemFromSku(id, r.id), `Created item ${r.sku}.`)}>Create item</button>{" "}
                    <button className="ghost small" onClick={() => act(ignoreSku(id, r.id), `Ignored ${r.sku}.`)}>Ignore</button>
                  </>
                )}
              </td>
            </tr>
          ))}
          {rows.length === 0 && <tr><td colSpan={5} className="muted">Nothing here.</td></tr>}
        </tbody>
      </table>

      {bindFor && (
        <div className="overlay" onClick={(e) => e.target === e.currentTarget && setBindFor(null)}>
          <div className="dialog">
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
  const [take, setTake] = useState(50);
  const [skip, setSkip] = useState(0);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const refresh = () =>
    fetchWebstoreProducts(id, { status: status || undefined, linked: linked || undefined, skip, take })
      .then(setResp).catch((e) => setError(String(e)));
  useEffect(() => { void refresh(); }, [status, linked, skip, take]); // eslint-disable-line react-hooks/exhaustive-deps

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
        <label>Show
          <select value={take} onChange={(e) => { setSkip(0); setTake(Number(e.target.value)); }}>
            <option value={25}>25</option><option value={50}>50</option><option value={100}>100</option>
          </select>
        </label>
        <span className="grow" />
        <span className="muted small">
          {resp?.lastRefreshed ? `Last refreshed ${new Date(resp.lastRefreshed + "Z").toLocaleString("en-GB")}` : "not swept yet"}
        </span>
        <button className="ghost small" disabled={busy} onClick={() => {
          setBusy(true); setError("");
          refreshWebstoreProducts(id).then(() => refresh()).catch((e) => setError(String(e))).finally(() => setBusy(false));
        }}>{busy ? "Refreshing…" : "Refresh now"}</button>
      </div>
      {error && <p className="error small">{error}</p>}
      <table>
        <thead><tr><th>Name</th><th>SKU</th><th className="num">Web price</th><th className="num">Stock</th><th>Status</th><th>Till item</th><th /></tr></thead>
        <tbody>
          {resp?.rows.map((p) => (
            <tr key={p.wooProductId} className={p.status === "deleted" ? "muted" : ""}>
              <td>{p.name}</td>
              <td className="mono small">{p.sku ?? <span className="muted">none</span>}</td>
              <td className="num">{gbp(p.pricePence)}{p.regularPricePence != null && p.regularPricePence !== p.pricePence && (
                <span className="muted small"> (was {gbp(p.regularPricePence)})</span>)}</td>
              <td className="num">{p.stockQuantity ?? "—"} <span className="muted small">{p.stockStatus ?? ""}</span></td>
              <td>{p.status}</td>
              <td>{p.linkedItem ? <span className="chip ok">linked</span> : <span className="chip bad">unlinked</span>}</td>
              <td>{p.permalink && <a className="small" href={p.permalink} target="_blank" rel="noreferrer">view on site</a>}</td>
            </tr>
          ))}
          {resp && resp.rows.length === 0 && <tr><td colSpan={7} className="muted">No products match.</td></tr>}
        </tbody>
      </table>
      {resp && resp.total > take && (
        <div className="toolbar">
          <button className="ghost small" disabled={skip === 0} onClick={() => setSkip(Math.max(0, skip - take))}>‹ Prev</button>
          <span className="muted small">{skip + 1}–{Math.min(skip + take, resp.total)} of {resp.total}</span>
          <button className="ghost small" disabled={skip + take >= resp.total} onClick={() => setSkip(skip + take)}>Next ›</button>
        </div>
      )}
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

      <h3>Prices on both platforms (largest differences first)</h3>
      <table>
        <thead><tr><th>SKU</th><th>Name (web)</th><th className="num">Web price</th><th className="num">Till price</th><th className="num">Difference</th></tr></thead>
        <tbody>
          {data.priceDiffers.map((r) => (
            <tr key={r.sku}>
              <td className="mono small">{r.sku}</td><td>{r.webName}</td>
              <td className="num">{gbp(r.webPricePence)}</td><td className="num">{gbp(r.tillPricePence)}</td>
              <td className="num">{r.priceDiffPence > 0 ? "+" : ""}{gbp(r.priceDiffPence)}</td>
            </tr>
          ))}
          {data.priceDiffers.length === 0 && <tr><td colSpan={5} className="muted">All matched prices agree.</td></tr>}
        </tbody>
      </table>

      <h3>Name drift</h3>
      <table>
        <thead><tr><th>SKU</th><th>Webstore name</th><th>Till name</th></tr></thead>
        <tbody>
          {data.nameDrift.map((r) => (
            <tr key={r.sku}><td className="mono small">{r.sku}</td><td>{r.webName}</td><td>{r.tillName}</td></tr>
          ))}
          {data.nameDrift.length === 0 && <tr><td colSpan={3} className="muted">No drift.</td></tr>}
        </tbody>
      </table>

      <h3>On the webstore but not in the till catalogue ({data.webOnly.length})</h3>
      <table>
        <thead><tr><th>SKU</th><th>Name</th><th className="num">Web price</th><th>Status</th></tr></thead>
        <tbody>
          {data.webOnly.slice(0, 200).map((r, i) => (
            <tr key={i}><td className="mono small">{r.sku ?? "—"}</td><td>{r.name}</td><td className="num">{gbp(r.pricePence)}</td><td>{r.status}</td></tr>
          ))}
          {data.webOnly.length === 0 && <tr><td colSpan={4} className="muted">None.</td></tr>}
        </tbody>
      </table>
      <p className="muted small">{data.tillOnlyCount.toLocaleString("en-GB")} till items are not on the webstore (normal — the web range is curated).</p>
    </div>
  );
}
