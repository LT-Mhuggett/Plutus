import { useEffect, useRef, useState } from "react";
import {
  createStockLocation, createStore, createTill, decideDeviceRemoval, deleteTill, fetchCompanies, fetchStockLocations,
  fetchStores, fetchTills, fetchWebstores, gbp, moveTillToStore, putReceiptTemplate, reissueTillCode, renameTill,
  revokeTill, updateStore,
  type Company, type ReceiptTemplate, type StockLocationRow, type StoreRow, type TillRow, type WebstoreConn,
} from "./api.ts";
import Barcode39 from "./Barcode39.tsx";
import DataTable from "./DataTable.tsx";
import TillThemesSection from "./TillThemesSection.tsx";
import { useNav } from "./nav.tsx";
import { ask } from "./Ask.tsx";

/**
 * A till's device state. Only the LIVE device (Active, or PendingRemoval awaiting approval) is a
 * chip — revoked devices are dead history and were previously rendered one chip each, so a till
 * that had been re-enrolled twice read "Revoked Revoked Active", which looks like a fault rather
 * than a normal audit trail. Retired devices now collapse into a single muted count.
 *
 * Note there can be at most one live device per till (FE6.1's one-active-device rule), so the
 * common case is exactly one chip.
 */
/**
 * Parse a timestamp the API sent, whether or not it already says it is UTC.
 *
 * ⚠⚠ THIS EXISTS BECAUSE `+ "Z"` SHIPPED A BUG THE SAME DAY IT FIXED ONE. Matt, 2026-08-12, with a
 * screenshot: every till read **"Invalid Date"**.
 *
 * The pattern all over this file was `new Date(value + "Z")`, which assumes the server always sends
 * a BARE timestamp. It did, while "last online" came from a MySQL column — EF hands those back as
 * `DateTimeKind.Unspecified`, which System.Text.Json writes without a suffix. The fix on 2026-08-11
 * made the value come from `TillPresence` instead, which records `DateTime.UtcNow` — **Kind=Utc**,
 * which serialises **with a trailing `Z`**. So `+ "Z"` produced `…ZZ`, and `Date` gave up.
 *
 * ⚠ The tell was in the screenshot: the webstore row said "never" (no device, so null, so the empty
 * branch) while every row with a live till said "Invalid Date". Only the non-null path was broken.
 *
 * ⚠ So this stops guessing what the server meant. A value that already carries `Z` or a `+01:00`
 * offset is passed through untouched; a bare one is treated as UTC, which is what every timestamp
 * on this API is. The whole class of bug goes with it.
 */
function apiDate(value: string): Date {
  return new Date(/[Zz]$|[+-]\d\d:?\d\d$/.test(value) ? value : value + "Z");
}

/** FE3.0: what we know about a device's hardware agent, as a chip. Never reported → nothing (a
 *  native till, or a web till from before this feature). Reported without a version → the web till
 *  looked at its PC and found no agent installed. */
function AgentChip({ d }: { d: TillRow["devices"][number] }) {
  if (!d.agentReportedAtUtc) return null;
  const reported = apiDate(d.agentReportedAtUtc).toLocaleString("en-GB");
  if (!d.agentVersion) {
    return <span className="chip" title={`The till checked its PC and found no hardware agent (last checked ${reported}). Receipts print as PDF.`}>no agent</span>;
  }
  const printerBad = d.agentPrinterOnline === false;
  return (
    <span
      className={`chip ${printerBad ? "warn" : "ok"}`}
      title={`Plutus Till Agent v${d.agentVersion}${d.agentPrinterName ? ` · printer: ${d.agentPrinterName}` : ""} — reported ${reported}`}
    >
      agent v{d.agentVersion}{printerBad ? " · printer offline" : d.agentPrinterOnline ? " · printer ✓" : ""}
    </span>
  );
}

/**
 * Which BUILD this till is running, and whether it is speaking to us.
 *
 * ⚠ Every till has sent its own version on the 60s heartbeat since WP5 and nothing ever showed it,
 * so "is that till on the new build?" could only be answered by walking to it. That is the question
 * asked after every deploy, and the one that matters when a single till misbehaves.
 *
 * ⚠ A missing version means NOT HEARD FROM RECENTLY, never "old version" — presence is in-memory
 * and rebuilds itself within a minute of a backend restart, so an empty chip right after a deploy
 * of the BACKEND is expected and resolves itself.
 */
function VersionChip({ d }: { d: TillRow["devices"][number] }) {
  const seen = d.lastSeenUtc ? apiDate(d.lastSeenUtc).toLocaleString("en-GB") : null;

  if (!d.appVersion) {
    return (
      <span className="chip" title={seen ? `Last heard from ${seen}` : "This till has not reported since the backend last started."}>
        version unknown
      </span>
    );
  }

  // Offline with sales still queued is the one worth chasing — that is money sitting on a machine.
  const stranded = d.presence === "Offline" && (d.outboxDepth ?? 0) > 0;
  const tone = stranded ? "warn" : d.presence === "Online" ? "ok" : "";

  // ⚠ The CHIP shows a plain version — "v1.13.0", the same shape as the agent chip beside it. The
  // build hash goes in the TOOLTIP, not the label: it is the thing you want when two tills claim
  // the same version and behave differently, and clutter the rest of the time.
  const [plain, build] = d.appVersion.split("+");

  return (
    <span
      className={`chip ${tone}`}
      title={`Till software v${plain}${build ? ` (build ${build})` : ""} · ${d.presence}` +
        (seen ? ` · last heard ${seen}` : "") +
        ((d.outboxDepth ?? 0) > 0 ? ` · ${d.outboxDepth} sale(s) queued on the till` : "")}
    >
      v{plain}
      {d.presence !== "Online" ? ` · ${d.presence.toLowerCase()}` : ""}
      {stranded ? ` · ${d.outboxDepth} queued` : ""}
    </span>
  );
}

function DeviceChips({ devices }: { devices: TillRow["devices"] }) {
  const live = devices.filter((d) => d.status !== "Revoked");
  const retired = devices.length - live.length;
  if (devices.length === 0) return <span className="muted">none — needs enrolling</span>;
  return (
    <>
      {live.map((d) => (
        <span key={d.id}>
          <span className={`chip ${d.status === "Active" ? "ok" : "warn"}`}>
            {d.status === "PendingRemoval" ? "Pending removal" : d.status}
          </span>{" "}
          <VersionChip d={d} />{" "}
          <AgentChip d={d} />
        </span>
      ))}
      {live.length === 0 && <span className="chip bad" title="Every device for this till has been revoked — issue a New code to enrol one">not enrolled</span>}
      {retired > 0 && (
        <span className="muted small" title={`${retired} previously enrolled device${retired === 1 ? "" : "s"}, retired when this till was re-enrolled. Kept for the audit trail.`}>
          {" "}+{retired} retired
        </span>
      )}
    </>
  );
}

const DAYS: { key: string; label: string }[] = [
  { key: "mon", label: "Mon" }, { key: "tue", label: "Tue" }, { key: "wed", label: "Wed" },
  { key: "thu", label: "Thu" }, { key: "fri", label: "Fri" }, { key: "sat", label: "Sat" }, { key: "sun", label: "Sun" },
];

/** WP11.6 "Locations": physical locations at the top (summary + the three add actions), then
 *  Stores / Warehouses / Webstores as collapsible groups, all CLOSED by default. Company details
 *  moved to the Company tab (WP11.5). A store's own Store-type stock location shows INSIDE its
 *  card as its inventory bucket — never as a confusing flat "physical locations" row. */
export default function StoresPage() {
  const [companies, setCompanies] = useState<Company[]>([]);
  const [stores, setStores] = useState<StoreRow[]>([]);
  const [tills, setTills] = useState<TillRow[]>([]);
  const [locations, setLocations] = useState<StockLocationRow[]>([]);
  const [webstores, setWebstores] = useState<WebstoreConn[]>([]);
  const [error, setError] = useState("");
  const [issued, setIssued] = useState<{ tillId: string; code: string; expires: string } | null>(null);
  const [busy, setBusy] = useState(false);

  // A Dashboard pill (or any go("Locations", "warehouses")) opens + scrolls to the matching group.
  const { focus } = useNav();
  const groupRefs = {
    stores: useRef<HTMLDetailsElement>(null),
    tills: useRef<HTMLDetailsElement>(null),          // FE6.2 flat fleet view
    warehouses: useRef<HTMLDetailsElement>(null),
    webstores: useRef<HTMLDetailsElement>(null),
  };
  useEffect(() => {
    const r = focus ? groupRefs[focus as keyof typeof groupRefs]?.current : null;
    if (r) { r.open = true; r.scrollIntoView({ behavior: "smooth", block: "start" }); }
  }, [focus]);

  const refresh = () =>
    Promise.all([
      fetchCompanies(), fetchStores(), fetchTills(),
      fetchStockLocations().catch(() => [] as StockLocationRow[]),
      fetchWebstores().catch(() => [] as WebstoreConn[]),   // 403 when unentitled — group just shows none
    ])
      .then(([c, s, t, l, w]) => { setCompanies(c); setStores(s); setTills(t); setLocations(l); setWebstores(w); })
      .catch((e) => setError(String(e)));

  useEffect(() => { void refresh(); }, []);

  // FE6.2: the agent/printer chips are the till's LAST REPORT, not a live probe — a till that
  // has been updated (or switched off) shows stale until its browser next reports. Refresh
  // re-reads the fleet so an update can be confirmed without reloading the whole portal.
  const [tillsRefreshing, setTillsRefreshing] = useState(false);
  const [tillsRefreshedAt, setTillsRefreshedAt] = useState<Date | null>(null);
  async function refreshTills() {
    setTillsRefreshing(true);
    try {
      setTills(await fetchTills());
      setTillsRefreshedAt(new Date());
    } catch (e) { setError(String(e)); }
    finally { setTillsRefreshing(false); }
  }

  async function newTill(storeId: number, name: string) {
    setBusy(true);
    setError("");
    try {
      const r = await createTill(storeId, name.trim());
      setIssued({ tillId: r.tillId, code: r.enrolmentCode, expires: r.expiresAtUtc });
      await refresh();
    } catch (e) {
      setError(String(e instanceof Error ? e.message : e));
    } finally {
      setBusy(false);
    }
  }

  /** FE6.1: fresh code for an EXISTING till — the fix for tills whose browser lost its credential.
   *  Spelled out in the confirmation because it retires the till's current device. */
  async function reissue(t: TillRow) {
    const active = t.devices.some((d) => d.status === "Active");
    if (!await ask.confirm({
      title: `Issue a new enrolment code for “${t.name}”?`,
      body: (
        <>
          {active && (
            <p className="small">
              When the code is used, this till's <strong>current device stops trading</strong> — a
              till is one counter.
            </p>
          )}
          <p className="small">
            The till keeps its identity, so all of its sales history stays with it. Use this to
            re-enrol a till whose browser has lost its credential.
          </p>
        </>
      ),
      confirmLabel: "Issue code",
    })) return;
    setBusy(true); setError("");
    try {
      const r = await reissueTillCode(t.id);
      setIssued({ tillId: t.id, code: r.enrolmentCode, expires: r.expiresAtUtc });
      await refresh();
    } catch (e) {
      setError(String(e instanceof Error ? e.message : e));
    } finally { setBusy(false); }
  }

  /** FE6.2: move a till between stores — pick the destination from a list of store NAMES. */
  async function move(t: TillRow) {
    const options = stores.filter((s) => s.id !== t.storeId);
    if (options.length === 0) return;
    const chosen = await ask.choose({
      title: `Move “${t.name}”`,
      body: (
        <>
          <p className="small">
            Currently at <strong>{stores.find((s) => s.id === t.storeId)?.name ?? `Store ${t.storeId}`}</strong>.
            Choose where it should live:
          </p>
          <p className="muted small">
            The till keeps its identity and its sales, but reports bucket by the till's CURRENT
            store — so its past figures move with it.
          </p>
        </>
      ),
      options: options.map((s) => ({ value: String(s.id), label: s.name ?? `Store ${s.id}` })),
      confirmLabel: "Move till",
    });
    if (chosen === null) return;
    setBusy(true); setError("");
    try {
      await moveTillToStore(t.id, Number(chosen));
      await refresh();
    } catch (e) {
      setError(String(e instanceof Error ? e.message : e));
    } finally { setBusy(false); }
  }

  async function rename(id: string, name: string) {
    setError("");
    try {
      await renameTill(id, name);
      await refresh();
    } catch (e) {
      setError(String(e instanceof Error ? e.message : e));
    }
  }

  async function removeTill(t: TillRow) {
    if (!await ask.confirm({
      title: `Delete till “${t.name}”?`,
      body: <p className="small">This can't be undone. A till that has recorded sales is protected and will be refused.</p>,
      confirmLabel: "Delete till",
      danger: true,
    })) return;
    setError("");
    try {
      await deleteTill(t.id);
      await refresh();
    } catch (e) {
      // 409 = the till has recorded sales and is protected.
      setError(String(e instanceof Error ? e.message : e));
    }
  }

  return (
    <section className="panel">
      {error && <p className="error">{error}</p>}

      <h2>Locations</h2>
      <p className="muted small">
        {stores.length} store{stores.length === 1 ? "" : "s"} ·{" "}
        {tills.length} till{tills.length === 1 ? "" : "s"} ·{" "}
        {locations.filter((l) => l.type === "Warehouse").length} warehouse
        {locations.filter((l) => l.type === "Warehouse").length === 1 ? "" : "s"} ·{" "}
        {webstores.length} webstore{webstores.length === 1 ? "" : "s"}
      </p>
      <div className="toolbar">
        <AddStore companies={companies} onSaved={refresh} />
        <AddWarehouse stores={stores} onSaved={refresh} />
        <span className="muted small">+ Webstore — connect one in the <strong>Webstore</strong> tab.</span>
      </div>

      <details className="card store-card" ref={groupRefs.stores}>
        <summary><strong>Stores ({stores.length})</strong></summary>
        {stores.length === 0 && <p className="muted">No stores yet — add one above.</p>}
        {stores.map((s) => (
          <StoreCard
            key={s.id} store={s} defaultOpen={false} onSaved={refresh}
            tills={tills.filter((t) => t.storeId === s.id)} busy={busy}
            buckets={locations.filter((l) => l.storeId === s.id && l.type === "Store")}
            onRename={rename} onRemoveTill={removeTill} onNewTill={newTill}
            issued={issued} onDismissIssued={() => setIssued(null)}
            onRevoke={(id) => void revokeTill(id).then(refresh)}
            onDecide={(deviceId, approve) => void decideDeviceRemoval(deviceId, approve).then(refresh)}
            onReissue={reissue}
          />
        ))}
      </details>

      {/* FE6.2: the flat fleet view — every till, whichever store it belongs to, with the
          re-issue-code and move-store actions. A webstore's virtual till appears here too,
          badged, so the fleet listing is complete rather than quietly filtered. */}
      <details className="card store-card" ref={groupRefs.tills}>
        <summary><strong>Tills ({tills.length})</strong></summary>
        <p className="muted small">
          Every till across all stores. A till is one counter: enrolling a replacement browser
          retires the previous device automatically.
        </p>
        <div className="toolbar">
          <span className="grow muted small">
            Agent and printer chips show what each till <em>last reported</em> (it reports on
            change, and at least every few hours) — a till whose browser is closed keeps its last
            known values.
            {tillsRefreshedAt && ` Refreshed ${tillsRefreshedAt.toLocaleTimeString("en-GB")}.`}
          </span>
          <button className="ghost small" disabled={tillsRefreshing} onClick={() => void refreshTills()}>
            {tillsRefreshing ? "Refreshing…" : "Refresh"}
          </button>
        </div>
        {/* Creating a till from the fleet view, where the store is not implied by where you are
            standing — so it asks. */}
        <div className="toolbar">
          <NewTillAnywhere stores={stores} busy={busy} onCreate={newTill} />
        </div>
        <DataTable<TillRow>
          columns={[
            { key: "name", label: "Till", render: (t) => <>{t.name}{t.isWebstore && <span className="chip" title="Virtual till carrying webstore orders"> webstore</span>}</> },
            {
              key: "storeId", label: "Store",
              sort: (t) => stores.find((s) => s.id === t.storeId)?.name ?? `Store ${t.storeId}`,
              render: (t) => stores.find((s) => s.id === t.storeId)?.name ?? `Store ${t.storeId}`,
            },
            {
              key: "devices", label: "Devices", sortable: false,
              render: (t) => t.isWebstore
                ? <span className="muted small">n/a</span>
                : <DeviceChips devices={t.devices} />,
            },
            {
            // 26a0 null = never heard from. `new Date(null + "Z")` renders "Invalid Date", so the
            // empty case has to be handled explicitly rather than left to the formatter.
            key: "lastOnline", label: "Last online",
            render: (t) => t.lastOnline
              ? <span className="small">{apiDate(t.lastOnline).toLocaleString("en-GB")}</span>
              : <span className="muted small">never</span>,
          },
          ]}
          rows={tills} getKey={(t) => t.id} initialSortKey="name"
          search={(t) => `${t.name} ${t.id} ${stores.find((s) => s.id === t.storeId)?.name ?? ""}`}
          searchPlaceholder="Search till / store…"
          rowActions={(t) => t.isWebstore
            ? <span className="muted small" title="Disconnect it from the Webstore tab instead">managed by Webstore</span>
            : (
              <>
                <button className="ghost small" disabled={busy} title="Fresh single-use code for THIS till — keeps its history"
                  onClick={() => void reissue(t)}>New code</button>{" "}
                <button className="ghost small" disabled={busy || stores.length < 2} title={stores.length < 2 ? "Only one store" : "Move to another store"}
                  onClick={() => void move(t)}>Move</button>
              </>
            )}
          emptyText="No tills yet — use “+ New till” above."
        />
        {issued && (
          <div className="enrol-code">
            <div className="grow">
              <span className="muted small">
                Enrolment code for “{tills.find((t) => t.id === issued.tillId)?.name ?? "this till"}” — on the till device open
                the Plutus tab (or “Connect to Plutus” on first run) and enter it (single-use, expires {new Date(issued.expires).toLocaleString("en-GB")}).
                Enrolling with it retires that till's current device.
              </span>
              <div className="enrol-code-value mono">{issued.code}</div>
            </div>
            <button className="ghost small" onClick={() => void navigator.clipboard?.writeText(issued.code)}>Copy</button>
            <button className="ghost small" onClick={() => setIssued(null)}>Dismiss</button>
          </div>
        )}
      </details>

      <details className="card store-card" ref={groupRefs.warehouses}>
        <summary><strong>Warehouses ({locations.filter((l) => l.type === "Warehouse").length})</strong></summary>
        <DataTable<StockLocationRow>
          columns={[
            { key: "name", label: "Name" },
            {
              key: "storeId", label: "Backing store",
              render: (l) => stores.find((s) => s.id === l.storeId)?.name ?? `Store ${l.storeId}`,
            },
          ]}
          rows={locations.filter((l) => l.type === "Warehouse")} getKey={(l) => l.id} initialSortKey="name"
          search={(l) => l.name}
          searchPlaceholder="Search warehouse…"
          emptyText="No warehouses yet — add one above."
        />
      </details>

      <details className="card store-card" ref={groupRefs.webstores}>
        <summary><strong>Webstores ({webstores.length})</strong></summary>
        <DataTable<WebstoreConn>
          columns={[
            { key: "name", label: "Name" },
            { key: "url", label: "Site", render: (w) => <span className="small">{w.url}</span> },
            { key: "enabled", label: "Status", render: (w) => (w.enabled ? <span className="chip ok">connected</span> : <span className="chip bad">disabled</span>) },
            { key: "pendingSkus", label: "Pending SKUs", numeric: true },
            {
              // FE6.2: show which virtual till carries this webstore's orders — the answer to
              // "what is the 'Kapow Web' till?" without having to ask.
              key: "till", label: "Sells through", sortable: false,
              render: (w) => {
                const t = tills.find((x) => x.isWebstore && x.name.toLowerCase().startsWith(w.name.toLowerCase().slice(0, 6)));
                return t ? <span className="small">{t.name}</span> : <span className="muted small">virtual till</span>;
              },
            },
          ]}
          rows={webstores} getKey={(w) => w.id} initialSortKey="name"
          search={(w) => `${w.name} ${w.url}`}
          searchPlaceholder="Search webstore…"
          emptyText="No webstores connected — use the Webstore tab."
        />
        <p className="muted small">Full webstore workspace (review queue, catalogue, alignment, outbound) lives in the <strong>Webstore</strong> tab.</p>
      </details>

      {/* FE10: till colour schemes — defined here, resolved server-side, applied by tills
          within a minute. Deliberately in Locations: the assignment targets ARE this page's
          stores, tills, and groups of tills. */}
      <details className="card store-card">
        <summary><strong>Till themes</strong></summary>
        <TillThemesSection stores={stores} tills={tills} />
      </details>
    </section>
  );
}

/** "+ Warehouse / location" as a top-row action (the create form the old flat section had). */
function AddWarehouse({ stores, onSaved }: { stores: StoreRow[]; onSaved: () => Promise<void> | void }) {
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  const [storeId, setStoreId] = useState<number>(stores[0]?.id ?? 1);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => { if (stores[0]) setStoreId(stores[0].id); }, [stores]);
  if (!open) return <button className="ghost small" onClick={() => setOpen(true)}>+ Warehouse / location</button>;
  return (
    <span className="new-location">
      <input placeholder="unique, e.g. Back Warehouse" value={name} onChange={(e) => setName(e.target.value)} />
      <select value={storeId} onChange={(e) => setStoreId(Number(e.target.value))}>
        {stores.map((s) => <option key={s.id} value={s.id}>{s.name ?? `Store ${s.id}`}</option>)}
      </select>
      <button className="primary small" disabled={busy || !name.trim()} onClick={async () => {
        setBusy(true); setError("");
        try {
          await createStockLocation({ storeId, type: "Warehouse", name: name.trim() });
          setOpen(false); setName(""); await onSaved();
        } catch (e) { setError(String(e instanceof Error ? e.message : e)); } finally { setBusy(false); }
      }}>Create</button>
      <button className="ghost small" onClick={() => setOpen(false)}>Cancel</button>
      {error && <span className="error small">{error}</span>}
    </span>
  );
}

/** One store's tills — sortable, with rename/revoke/delete + create. */
function TillsTable({ tills, busy, onRename, onRemove, onRevoke, storeId, onNewTill, onDecide, onReissue, issued, onDismissIssued }: {
  tills: TillRow[]; busy: boolean; storeId: number;
  onRename: (id: string, name: string) => Promise<void>;
  onRemove: (t: TillRow) => void; onRevoke: (id: string) => void;
  onNewTill: (storeId: number, name: string) => Promise<void>;
  onDecide: (deviceId: string, approve: boolean) => void;
  onReissue: (t: TillRow) => Promise<void>;
  issued: { tillId: string; code: string; expires: string } | null;
  onDismissIssued: () => void;
}) {
  return (
    <>
      {/* FE4.3 (deferred to FE6): this store's tills on the standard table. */}
      <DataTable<TillRow>
        columns={[
          {
            key: "name", label: "Name",
            render: (t) => (
              <>
                <TillNameCell till={t} onRename={onRename} />
                {t.isWebstore && <span className="chip" title="Virtual till — carries this store's webstore (e.g. WooCommerce) orders. Managed from the Webstore tab."> webstore</span>}
              </>
            ),
          },
          {
            // 26a0 null = never heard from. `new Date(null + "Z")` renders "Invalid Date", so the
            // empty case has to be handled explicitly rather than left to the formatter.
            key: "lastOnline", label: "Last online",
            render: (t) => t.lastOnline
              ? <span className="small">{apiDate(t.lastOnline).toLocaleString("en-GB")}</span>
              : <span className="muted small">never</span>,
          },
          {
            key: "devices", label: "Devices", sortable: false,
            render: (t) => (
              <>
                {t.isWebstore
                  ? <span className="muted small">webstore channel — no enrolment</span>
                  : <DeviceChips devices={t.devices} />}
                {/* WP6.2: a device that asked to be un-enrolled — approve (revoke) or reject (keep). */}
                {t.devices.filter((d) => d.status === "PendingRemoval").map((d) => (
                  <span key={`act-${d.id}`} className="small" style={{ display: "inline-flex", gap: 4, marginLeft: 6 }}>
                    <button className="ghost small" disabled={busy} onClick={() => onDecide(d.id, true)} title="Approve removal — revokes this device">Approve</button>
                    <button className="ghost small" disabled={busy} onClick={() => onDecide(d.id, false)} title="Reject — device stays enrolled">Reject</button>
                  </span>
                ))}
              </>
            ),
          },
        ]}
        rows={tills} getKey={(t) => t.id} initialSortKey="name"
        search={(t) => t.name}
        searchPlaceholder="Search till…"
        rowActions={(t) => t.isWebstore ? (
          <span className="muted small" title="Disconnect it from the Webstore tab instead — deleting here would break order ingest.">managed by Webstore</span>
        ) : (
          <>
            {/* FE6.1: the missing operation — a new code for THIS till, rather than a new till. */}
            <button className="ghost small" disabled={busy} title="Fresh single-use code for this till (keeps its sales history)"
              onClick={() => void onReissue(t)}>New code</button>{" "}
            {t.devices.some((d) => d.status === "Active" || d.status === "PendingRemoval") && (
              <><button className="ghost small" disabled={busy} onClick={() => onRevoke(t.id)}>Revoke</button>{" "}</>
            )}
            <button className="ghost small" disabled={busy} onClick={() => onRemove(t)} title="Delete (only if no sales)">Delete</button>
          </>
        )}
        emptyText="No tills for this store yet."
      />
      <NewTillRow storeId={storeId} busy={busy} onCreate={onNewTill} />
      {/* the code shows HERE, next to the button that made it (it used to sit at the top of the
          page, off-screen when the store list is long) */}
      {issued && tills.some((t) => t.id === issued.tillId) && (
        <div className="enrol-code">
          <div className="grow">
            <span className="muted small">
              Enrolment code for “{tills.find((t) => t.id === issued.tillId)?.name}” — on the till device open
              the Plutus tab (or “Connect to Plutus” on first run) and enter it (single-use, expires {new Date(issued.expires).toLocaleString("en-GB")}).
            </span>
            <div className="enrol-code-value mono">{issued.code}</div>
          </div>
          <button className="ghost small" onClick={() => void navigator.clipboard?.writeText(issued.code)}>Copy</button>
          <button className="ghost small" onClick={onDismissIssued}>Dismiss</button>
        </div>
      )}
    </>
  );
}

/** Inline till rename: click the name to edit, Enter/blur saves (409 surfaces via onRename). */
function TillNameCell({ till, onRename }: { till: TillRow; onRename: (id: string, name: string) => Promise<void> }) {
  const [editing, setEditing] = useState(false);
  const [name, setName] = useState(till.name);
  if (!editing) {
    return (
      <button className="link-name" title={till.id} onClick={() => { setName(till.name); setEditing(true); }}>
        {till.name}
      </button>
    );
  }
  const commit = async () => {
    setEditing(false);
    if (name.trim() && name.trim() !== till.name) await onRename(till.id, name.trim());
  };
  return (
    <input
      autoFocus
      value={name}
      maxLength={80}
      onChange={(e) => setName(e.target.value)}
      onBlur={() => void commit()}
      onKeyDown={(e) => { if (e.key === "Enter") void commit(); if (e.key === "Escape") setEditing(false); }}
    />
  );
}

/**
 * "+ New till" from the FLEET view, where the store is not implied by where you are standing.
 *
 * The store card's version knows its own store; this one has to ask. That question is the whole
 * reason it exists — a till belongs to exactly one store, and the choice decides which address
 * prints on its receipts, which VAT bands and theme it inherits, and which store's takings its
 * sales land in. Getting it wrong is a move operation later, and moves drag historic figures with
 * them.
 *
 * ⚠ With more than one store, NO store is preselected. A silent default here is a till quietly
 * created against whichever store happened to sort first.
 */
function NewTillAnywhere({ stores, busy, onCreate }: {
  stores: StoreRow[];
  busy: boolean;
  onCreate: (storeId: number, name: string) => Promise<void>;
}) {
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  // One store means there is nothing to ask; more than one means the operator must choose.
  const [storeId, setStoreId] = useState<number | "">(stores.length === 1 ? stores[0].id : "");
  useEffect(() => { if (stores.length === 1) setStoreId(stores[0].id); }, [stores]);

  if (stores.length === 0) {
    return <span className="muted small">Add a store before creating a till — a till has to live somewhere.</span>;
  }

  if (!open) return <button className="ghost small" onClick={() => setOpen(true)}>+ New till</button>;

  return (
    <span className="new-location">
      <label className="muted small">
        Location
        <select value={storeId} onChange={(e) => setStoreId(e.target.value === "" ? "" : Number(e.target.value))}>
          {stores.length > 1 && <option value="">Which location?</option>}
          {stores.map((s) => <option key={s.id} value={s.id}>{s.name ?? `Store ${s.id}`}</option>)}
        </select>
      </label>
      <input
        value={name}
        maxLength={80}
        placeholder="Till name, e.g. Front Desk"
        onChange={(e) => setName(e.target.value)}
      />
      <button
        className="primary small"
        disabled={busy || !name.trim() || storeId === ""}
        title={storeId === "" ? "Choose which location this till belongs to" : "Create the till and issue its enrolment code"}
        onClick={async () => {
          if (storeId === "") return;
          await onCreate(storeId, name);
          setName("");
          setOpen(false);
        }}
      >
        Create till + code
      </button>
      <button className="ghost small" onClick={() => { setOpen(false); setName(""); }}>Cancel</button>
    </span>
  );
}

/** New till + enrolment code, with a required unique name. */
function NewTillRow({ storeId, busy, onCreate }: { storeId: number; busy: boolean; onCreate: (storeId: number, name: string) => Promise<void> }) {
  const [name, setName] = useState("");
  return (
    <div className="toolbar">
      <label>New till (store {storeId})
        <input value={name} maxLength={80} placeholder="e.g. Front Desk" onChange={(e) => setName(e.target.value)} />
      </label>
      <button
        className="primary small"
        disabled={busy || !name.trim()}
        onClick={async () => { await onCreate(storeId, name); setName(""); }}
      >
        Create till + code
      </button>
    </div>
  );
}

function StoreCard({ store, defaultOpen, onSaved, tills, busy, buckets, onRename, onRemoveTill, onNewTill, onRevoke, onDecide, onReissue, issued, onDismissIssued }: {
  store: StoreRow; defaultOpen: boolean; onSaved: () => Promise<void> | void;
  tills: TillRow[]; busy: boolean; buckets: StockLocationRow[];
  onRename: (id: string, name: string) => Promise<void>;
  onRemoveTill: (t: TillRow) => void; onNewTill: (storeId: number, name: string) => Promise<void>;
  onRevoke: (id: string) => void; onDecide: (deviceId: string, approve: boolean) => void;
  onReissue: (t: TillRow) => Promise<void>;
  issued: { tillId: string; code: string; expires: string } | null;
  onDismissIssued: () => void;
}) {
  const [edit, setEdit] = useState<StoreRow>(store);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState("");
  const dirty = JSON.stringify(edit) !== JSON.stringify(store);
  const addr = [store.adLine1, store.city, store.postCode].filter((x) => x && x !== "N/A").join(", ");

  // ⚠ 2026-08-17: the hours must be READABLE before they can be saved. See `hoursProblem` — the
  // portal is the strict end of this agreement and the tills are the tolerant end.
  const hoursFault = hoursProblem(edit.openingHoursJson);

  const save = async () => {
    if (hoursFault) { setSaveError(`Opening hours: ${hoursFault}`); return; }
    setSaving(true);
    setSaveError("");
    try {
      await updateStore(store.id, {
        name: edit.name, adLine1: edit.adLine1, city: edit.city, postCode: edit.postCode,
        contactNumber: edit.contactNumber, openingHoursJson: edit.openingHoursJson,
      });
      await onSaved();
    } catch (e) {
      // 409 = store name taken. Inline, next to the form — an alert() popup for a validation
      // message is both ugly and easy to dismiss without reading.
      setSaveError(String(e instanceof Error ? e.message : e));
    } finally { setSaving(false); }
  };

  return (
    <details className="card store-card" open={defaultOpen}>
      <summary className="store-summary">
        <strong>{store.name || `Store ${store.id}`}</strong>
        {addr && <span className="muted small"> — {addr}</span>}
      </summary>

      {/* address + phone (top part) */}
      <div className="toolbar">
        <label>Name <input placeholder="e.g. High Street" value={edit.name ?? ""} onChange={(e) => setEdit({ ...edit, name: e.target.value || null })} /></label>
        <label>Address <input value={edit.adLine1} onChange={(e) => setEdit({ ...edit, adLine1: e.target.value })} /></label>
        <label>City <input value={edit.city} onChange={(e) => setEdit({ ...edit, city: e.target.value })} /></label>
        <label>Postcode <input value={edit.postCode} onChange={(e) => setEdit({ ...edit, postCode: e.target.value })} /></label>
        <label>Phone <input value={edit.contactNumber} onChange={(e) => setEdit({ ...edit, contactNumber: e.target.value })} /></label>
        {dirty && <button className="primary small" disabled={saving} onClick={() => void save()}>Save</button>}
        {saveError && <span className="error small">{saveError}</span>}
      </div>

      <details className="sub" open={hoursFault !== null}>
        {/* ⚠ Forced open when the stored hours are unreadable — a fault hidden behind a collapsed
            summary is a fault nobody knows they have. */}
        <summary className="muted small">
          Opening hours{hoursFault !== null && <span className="error small"> — the tills can't read these</span>}
        </summary>
        <OpeningHoursEditor value={edit.openingHoursJson} onChange={(v) => setEdit({ ...edit, openingHoursJson: v })} />
        {/* ⚠⚠ ITS OWN SAVE BUTTON, 2026-08-17. This section used to say "press Save in the address
            row" — a button in a different part of the card, above a collapsed section, which is a fine
            way to lose an operator's work without telling them. Hours set and never saved look
            identical to hours never set. */}
        {dirty && (
          <div className="toolbar">
            <button className="primary small" disabled={saving || hoursFault !== null} onClick={() => void save()}>
              {saving ? "Saving…" : "Save opening hours"}
            </button>
            {hoursFault === null && <span className="muted small">Saves the whole store card, address included.</span>}
          </div>
        )}
      </details>

      <details className="sub">
        <summary className="muted small">Tills ({tills.length})</summary>
        <TillsTable tills={tills} busy={busy} storeId={store.id}
          onRename={onRename} onRemove={onRemoveTill} onRevoke={onRevoke} onNewTill={onNewTill} onDecide={onDecide} onReissue={onReissue}
          issued={issued} onDismissIssued={onDismissIssued} />
      </details>

      {/* WP11.6: the store's own inventory bucket lives HERE, not in a flat "locations" list. */}
      {buckets.length > 0 && (
        <p className="muted small">
          Inventory bucket{buckets.length === 1 ? "" : "s"}: {buckets.map((b) => b.name).join(", ")} (auto-created; stock counts and transfers use this)
        </p>
      )}

      <ReceiptTemplateEditor store={store} onSaved={onSaved} />
    </details>
  );
}

/** WP11.3: add a store from the portal (the API existed; the button did not). */
function AddStore({ companies, onSaved }: { companies: Company[]; onSaved: () => Promise<void> | void }) {
  const [open, setOpen] = useState(false);
  const [f, setF] = useState({ name: "", adLine1: "", city: "", postCode: "", contactNumber: "" });
  const [companyId, setCompanyId] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  if (!open) return <button className="ghost small" onClick={() => setOpen(true)}>+ New store</button>;
  return (
    <div className="card">
      <div className="toolbar">
        {companies.length > 1 && (
          <label>Company
            <select value={companyId} onChange={(e) => setCompanyId(e.target.value)}>
              <option value="">(first)</option>
              {companies.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
          </label>
        )}
        <label>Name <input placeholder="unique, e.g. High Street" value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} /></label>
        <label>Address <input value={f.adLine1} onChange={(e) => setF({ ...f, adLine1: e.target.value })} /></label>
        <label>City <input value={f.city} onChange={(e) => setF({ ...f, city: e.target.value })} /></label>
        <label>Postcode <input value={f.postCode} onChange={(e) => setF({ ...f, postCode: e.target.value })} /></label>
        <label>Phone <input value={f.contactNumber} onChange={(e) => setF({ ...f, contactNumber: e.target.value })} /></label>
      </div>
      {error && <p className="error small">{error}</p>}
      <button className="primary small" disabled={busy} onClick={async () => {
        setBusy(true); setError("");
        try {
          await createStore({ ...(companyId ? { companyId } : {}), ...f });
          setOpen(false); setF({ name: "", adLine1: "", city: "", postCode: "", contactNumber: "" });
          await onSaved();
        } catch (e) { setError(String(e instanceof Error ? e.message : e)); } finally { setBusy(false); }
      }}>Create store</button>
      <button className="ghost small" onClick={() => setOpen(false)}>Cancel</button>
    </div>
  );
}

/**
 * What is wrong with this opening-hours blob — or null when nothing is.
 *
 * ⚠⚠ 2026-08-17. The advanced textarea below sent whatever was typed straight to the server with **no
 * check of any kind**, and the simple editor's `catch { return {} }` then showed a malformed value back
 * as *"no hours set"* — every day unticked. So the portal could hold hours that no till could read
 * while looking, to the person who typed them, exactly like a store with hours set. Reported by Matt:
 * *"webtill does not show the opening hours set in the portal."*
 *
 * ⚠ THE PORTAL IS THE STRICT END, THE TILLS ARE THE TOLERANT END, and that asymmetry is deliberate.
 * The tills stretch to read what already exists in the field (`openingHours.ts` /
 * `Client.Core/OpeningHours.cs`); this refuses to CREATE anything they would have to stretch for. A
 * writer as lax as its readers guarantees that the next odd shape reaches production.
 */
function hoursProblem(value: string | null): string | null {
  if (!value || !value.trim()) return null;   // nothing stored is a legitimate answer

  let raw: unknown;
  try { raw = JSON.parse(value); } catch (e) { return e instanceof Error ? e.message : String(e); }
  if (raw === null) return null;

  if (typeof raw !== "object" || Array.isArray(raw))
    return `expected an object of days, got ${Array.isArray(raw) ? "a list" : typeof raw}`;

  const days = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"];
  const bad = Object.keys(raw as Record<string, unknown>).filter((k) => !days.includes(k));
  if (bad.length) return `not a day: ${bad.join(", ")} — use ${days.join(", ")}`;

  for (const [day, spans] of Object.entries(raw as Record<string, unknown>)) {
    if (!Array.isArray(spans)) return `${day} must be a list of {"open","close"} times`;
    for (const s of spans) {
      const span = s as { open?: unknown; close?: unknown } | null;
      const ok = (t: unknown) => typeof t === "string" && /^\d{2}:\d{2}$/.test(t);
      if (!span || !ok(span.open) || !ok(span.close))
        return `${day} needs 24-hour times, e.g. {"open":"09:00","close":"17:30"}`;
    }
  }
  return null;
}

/** WP11.3: opening hours as tick-box days + 24-hour times, writing the same JSON the API stores.
 *  Multi-interval days (e.g. lunch closing) drop to an advanced raw-JSON view. */
function OpeningHoursEditor({ value, onChange }: { value: string | null; onChange: (v: string | null) => void }) {
  const parsed: Record<string, { open: string; close: string }[]> = (() => {
    try { return value ? JSON.parse(value) : {}; } catch { return {}; }
  })();
  const problem = hoursProblem(value);
  const multiInterval = Object.values(parsed).some((a) => Array.isArray(a) && a.length > 1);
  // ⚠ A blob that cannot be read opens in the ADVANCED view, showing the operator the actual text.
  // Dropping them into the simple editor would show every day unticked — the portal's own version of
  // the lie the tills were telling, and it hides the very characters that need fixing.
  const [advanced, setAdvanced] = useState(multiInterval || problem !== null);

  function setDay(day: string, next: { open: string; close: string } | null) {
    const obj = { ...parsed };
    if (next) obj[day] = [next];
    else delete obj[day];
    onChange(Object.keys(obj).length ? JSON.stringify(obj) : null);
  }

  if (advanced) {
    return (
      <label className="block">
        Opening hours (advanced JSON)
        <textarea rows={3} value={value ?? ""} onChange={(e) => onChange(e.target.value || null)} />
        {/* ⚠ LIVE, and it gates Save (see the store card). A textarea that accepts anything and a
            till that then says "not set" is two screens disagreeing with nobody to referee. */}
        {problem
          ? <span className="error small">The tills won't be able to read this: {problem}</span>
          : value?.trim() && <span className="muted small">✓ Readable by the tills.</span>}
        <button className="ghost small" type="button" onClick={() => setAdvanced(false)}>
          {problem ? "Start again with the simple editor" : "Back to simple editor"}
        </button>
      </label>
    );
  }

  return (
    <div className="hours-editor">
      <div className="muted small">Opening hours</div>
      {DAYS.map((d) => {
        const iv = parsed[d.key]?.[0];
        return (
          <div className="hours-row" key={d.key}>
            <label className="chk">
              <input type="checkbox" checked={!!iv}
                onChange={(e) => setDay(d.key, e.target.checked ? { open: "09:00", close: "17:30" } : null)} />
              {d.label}
            </label>
            {iv && (
              <>
                <input type="time" value={iv.open} onChange={(e) => setDay(d.key, { ...iv, open: e.target.value })} />
                <span className="muted">to</span>
                <input type="time" value={iv.close} onChange={(e) => setDay(d.key, { ...iv, close: e.target.value })} />
              </>
            )}
            {!iv && <span className="muted small">closed</span>}
          </div>
        );
      })}
      <button className="ghost small" type="button" onClick={() => setAdvanced(true)}>Advanced (JSON)</button>
    </div>
  );
}

/** WP11.2: per-store receipt template — header/footer lines (one per row) + toggles. */
function ReceiptTemplateEditor({ store, onSaved }: { store: StoreRow; onSaved: () => Promise<void> | void }) {
  const initial: ReceiptTemplate = (() => {
    try { return store.receiptTemplateJson ? JSON.parse(store.receiptTemplateJson) : {}; } catch { return {}; }
  })();
  const [tpl, setTpl] = useState<ReceiptTemplate>(initial);
  const [busy, setBusy] = useState(false);
  const dirty = JSON.stringify(tpl) !== JSON.stringify(initial);
  const linesToText = (a?: string[]) => (a ?? []).join("\n");
  const textToLines = (s: string) => s.split("\n").map((l) => l.trimEnd()).filter((l, i, arr) => l !== "" || i < arr.length);

  return (
    <details className="receipt-tpl">
      <summary className="muted small">Receipt template</summary>
      <p className="muted small">Ported from the NatApp receipt — everything below prints on the receipt and is editable. The preview on the right updates as you type.</p>
      <div className="receipt-tpl-body">
        <div className="receipt-tpl-fields">
          <div className="toolbar">
            <label>Shop name <input placeholder={store.name ?? "business name"} value={tpl.storeName ?? ""}
              onChange={(e) => setTpl({ ...tpl, storeName: e.target.value })} /></label>
            <label>Phone <input placeholder={store.contactNumber} value={tpl.phone ?? ""}
              onChange={(e) => setTpl({ ...tpl, phone: e.target.value })} /></label>
            <label>VAT number <input value={tpl.vatNumber ?? ""} onChange={(e) => setTpl({ ...tpl, vatNumber: e.target.value })} /></label>
          </div>
          <label className="block">Address (one line per row)
            <textarea rows={2} placeholder={[store.adLine1, store.city, store.postCode].filter(Boolean).join("\n")}
              value={linesToText(tpl.addressLines)} onChange={(e) => setTpl({ ...tpl, addressLines: textToLines(e.target.value) })} />
          </label>
          <label className="block">Header lines (e.g. "Thank you for shopping with us")
            <textarea rows={2} value={linesToText(tpl.headerLines)}
              onChange={(e) => setTpl({ ...tpl, headerLines: textToLines(e.target.value) })} />
          </label>
          <label className="block">Footer lines (e.g. returns policy)
            <textarea rows={2} value={linesToText(tpl.footerLines)}
              onChange={(e) => setTpl({ ...tpl, footerLines: textToLines(e.target.value) })} />
          </label>
          <label className="chk"><input type="checkbox" checked={tpl.showBarcode !== false}
            onChange={(e) => setTpl({ ...tpl, showBarcode: e.target.checked })} /> Show sale barcode</label>
          <label className="chk"><input type="checkbox" checked={!!tpl.showOperator}
            onChange={(e) => setTpl({ ...tpl, showOperator: e.target.checked })} /> Show operator name</label>
          <label className="chk"><input type="checkbox" checked={!!tpl.showVatNumber}
            onChange={(e) => setTpl({ ...tpl, showVatNumber: e.target.checked })} /> Show VAT number</label>
          {dirty && (
            <button className="primary small" disabled={busy} onClick={async () => {
              setBusy(true);
              await putReceiptTemplate(store.id, tpl).finally(() => setBusy(false));
              await onSaved();
            }}>Save receipt template</button>
          )}
        </div>
        <div className="receipt-tpl-preview">
          <div className="muted small centre">Preview</div>
          <ReceiptPreview tpl={tpl} store={store} />
        </div>
      </div>
    </details>
  );
}

/** Live preview of the receipt as the till would print it, from the current template + store,
 *  with sample sale data. Mirrors the web POS Receipt component's layout (NatApp order). */
function ReceiptPreview({ tpl, store }: { tpl: ReceiptTemplate; store: StoreRow }) {
  const headerLines = tpl.headerLines?.length ? tpl.headerLines : ["Thank you for shopping with us"];
  const shopName = tpl.storeName || store.name || "Your business";
  const phone = tpl.phone || store.contactNumber;
  const address = tpl.addressLines?.length ? tpl.addressLines : [store.adLine1, store.city, store.postCode].filter(Boolean);
  const showBarcode = tpl.showBarcode !== false;
  // Sample basket for the preview.
  const lines = [
    { name: "Comic Book", qty: 1, pricePence: 349 },
    { name: "Sticker Pack", qty: 2, pricePence: 100 },
  ];
  const gross = lines.reduce((s, l) => s + l.pricePence * l.qty, 0);
  const vat = Math.round(gross - gross / 1.2);

  return (
    <div className="receipt-view">
      {headerLines.map((l, i) => <p className="centre small" key={`h${i}`}>{l}</p>)}
      <p className="centre" style={{ fontWeight: 700 }}>{shopName}</p>
      {phone && <p className="centre small">{phone}</p>}
      {address.map((l, i) => <p className="centre small" key={`a${i}`}>{l}</p>)}
      {tpl.showVatNumber && tpl.vatNumber && <p className="centre small">VAT No: {tpl.vatNumber}</p>}
      <p className="centre small">{new Date().toLocaleString("en-GB")}</p>
      {tpl.showOperator && <p className="centre small">Served by Sample Staff</p>}
      <hr />
      <table className="receipt-lines">
        <tbody>
          {lines.map((l, i) => (
            <tr key={i}><td>{l.qty} × {l.name}</td><td className="num">{gbp(l.pricePence * l.qty)}</td></tr>
          ))}
        </tbody>
      </table>
      <hr />
      <p className="r-total"><span>Total</span><span>{gbp(gross)}</span></p>
      <p className="small r-total"><span>VAT</span><span>{gbp(vat)}</span></p>
      <hr />
      <p className="centre small">Cash {gbp(600)} (change {gbp(600 - gross)})</p>
      {(tpl.footerLines ?? []).map((l, i) => <p className="centre small" key={`f${i}`}>{l}</p>)}
      {showBarcode && <div className="centre"><Barcode39 value="019f9a8d-sample-sale" height={38} /></div>}
      <p className="centre mono tiny">019f9a8d-…-sample</p>
    </div>
  );
}
