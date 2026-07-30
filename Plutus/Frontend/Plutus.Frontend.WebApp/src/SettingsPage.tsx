import { useEffect, useState } from "react";
import { fetchDeviceStatus, fetchPayMethods, fetchTillName, onOutboxChanged, renameTill, requestUnenrol, BUSINESS_ID, STORE_ID } from "./api.ts";
import { parkedCount, queuedCount, resetDeviceSeq } from "./offline.ts";
import {
  canEnrolTills,
  canManageSettings,
  clearDeviceCredential,
  createTillEnrolCode,
  enrolDevice,
  getDeviceCredential,
  type DeviceCredential,
} from "./pipeline.ts";
import { getPrefs, setPrefs, type Prefs } from "./prefs.ts";
import { getSession } from "./session.ts";
import Receipt, { type ReceiptData } from "./till/Receipt.tsx";

declare const __BUILD_TIME__: string;

/** Sample receipt for the printer test (NatApp "Print Test Page" equivalent). */
const TEST_RECEIPT: ReceiptData = {
  saleId: "test-print — not a sale",
  date: new Date().toISOString(),
  lines: [
    {
      key: 1,
      item: { idOne: "TEST-1", name: "Test item one", brand: "-", desc: "", cost: 0, exPrice: 1, price: 1.2, taxId: 1, catId: "" },
      quantity: 2,
      pricePence: 120,
      exPricePence: 100,
      adjusted: false,
    },
    {
      key: 2,
      item: { idOne: "TEST-2", name: "Test item two (exempt)", brand: "-", desc: "", cost: 0, exPrice: 5, price: 5, taxId: 3, catId: "" },
      quantity: 1,
      pricePence: 500,
      exPricePence: 500,
      adjusted: false,
    },
  ],
  totalPence: 740,
  totalExTaxPence: 700,
  payments: [{ name: "Cash", amountPence: 1000, changePence: 260 }],
};

/** WP2.2 device enrolment (2026-07-24): this browser becomes an enrolled till device —
 *  the credential lives in localStorage (per browser, like the native till's identity)
 *  and every sale it submits carries its deviceId + monotonic deviceSeq. */
function TillDeviceSection() {
  const [cred, setCred] = useState<DeviceCredential | null>(() => getDeviceCredential());
  const [code, setCode] = useState("");
  const [issued, setIssued] = useState<{ code: string; expires: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [counts, setCounts] = useState({ queued: 0, parked: 0 });
  // WP6.2: this device's server-side enrolment status (Active / PendingRemoval / Revoked).
  const [deviceStatus, setDeviceStatus] = useState<string | null>(null);

  useEffect(() => {
    const refresh = () => void Promise.all([queuedCount(), parkedCount()]).then(([q, p]) => setCounts({ queued: q, parked: p }));
    refresh();
    return onOutboxChanged(refresh) as () => void;
  }, []);

  // Poll our own device status: once approved (Revoked) we forget the local credential; while
  // PendingRemoval we show the "awaiting approval" note. This till keeps trading throughout.
  useEffect(() => {
    if (!cred) return;
    const check = () => void fetchDeviceStatus(cred.deviceId)
      .then((r) => { setDeviceStatus(r.status); if (r.status === "Revoked") { clearDeviceCredential(); setCred(null); } })
      .catch(() => undefined);
    check();
    const timer = window.setInterval(check, 20_000);
    return () => window.clearInterval(timer);
  }, [cred]);

  async function requestRemoval() {
    if (!cred) return;
    if (!window.confirm("Un-enrol this device? It keeps working until a manager approves the removal in the management portal.")) return;
    setBusy(true); setError("");
    try { const r = await requestUnenrol(cred.deviceId); setDeviceStatus(r.status); }
    catch (e) { setError(String(e instanceof Error ? e.message : e)); }
    finally { setBusy(false); }
  }

  async function enrol(withCode: string) {
    setBusy(true);
    setError("");
    try {
      const c = await enrolDevice(withCode);
      await resetDeviceSeq(); // fresh deviceId → its sale sequence restarts at 1
      setCred(c);
      setCode("");
      setIssued(null);
    } catch (e) {
      setError(String(e instanceof Error ? e.message : e));
    } finally {
      setBusy(false);
    }
  }

  async function generateCode() {
    setBusy(true);
    setError("");
    try {
      const r = await createTillEnrolCode(STORE_ID, `Web POS ${new Date().toISOString().slice(0, 10)}`);
      setIssued({ code: r.enrolmentCode, expires: r.expiresAtUtc });
    } catch (e) {
      setError(String(e instanceof Error ? e.message : e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <h3 className="settings-h">Till device</h3>
      {cred ? (
        <>
          <p className="muted small">
            This browser is enrolled as a till device — its sales go through the platform pipeline with this identity.
            "Forget" only clears the local credential; revoking the device is done from the till admin.
          </p>
          <dl className="env-info">
            <dt>Device id</dt>
            <dd className="mono small">{cred.deviceId}</dd>
            <dt>Till id</dt>
            <dd className="mono small">{cred.tillId}</dd>
            <dt>Enrolled</dt>
            <dd>{new Date(cred.enrolledAt).toLocaleString("en-GB")}</dd>
            <dt>Sync queue</dt>
            <dd>
              {counts.queued} waiting{counts.parked > 0 && <span className="error"> · {counts.parked} parked (rejected — needs attention)</span>}
            </dd>
          </dl>
          {canEnrolTills() && <TillNameSetting tillId={cred.tillId} />}
          {deviceStatus === "PendingRemoval" ? (
            <p className="small discount-note">⏳ Removal requested — awaiting approval in the management portal (Locations → this till). This device keeps working until then.</p>
          ) : canEnrolTills() ? (
            <div className="setting-row">
              <span className="grow muted small">Un-enrol this browser — a manager approves it in the portal, then this device stops trading and forgets its credential.</span>
              <button className="ghost" disabled={busy} onClick={requestRemoval}>Un-enrol this device</button>
            </div>
          ) : (
            <p className="muted small">Un-enrolling this till needs a manager (the <span className="mono">portal.tills.enrol</span> permission).</p>
          )}
        </>
      ) : (
        <>
          <p className="error small">Not enrolled — checkout is blocked until this browser is enrolled as a till device.</p>
          <div className="setting-row">
            <span className="grow">
              Enrolment code
              <span className="muted small block">Single-use code from the till admin (valid 48h).</span>
            </span>
            <input
              className="pref-input"
              placeholder="e.g. 4F7K2M9P"
              value={code}
              onChange={(e) => setCode(e.target.value)}
            />
            <button className="primary" disabled={busy || !code.trim()} onClick={() => enrol(code)}>
              Enrol this device
            </button>
          </div>
          {canEnrolTills() && (
            <div className="setting-row">
              <span className="grow">
                Till admin
                <span className="muted small block">
                  Your login can create tills: generate a code for this browser or type it into another one.
                </span>
              </span>
              <button className="ghost" disabled={busy} onClick={generateCode}>
                Generate a code
              </button>
            </div>
          )}
          {issued && (
            <p className="small">
              Code <strong className="mono">{issued.code}</strong> (single-use, expires{" "}
              {new Date(issued.expires).toLocaleString("en-GB")}){" "}
              <button className="ghost small" disabled={busy} onClick={() => enrol(issued.code)}>
                Enrol this browser with it
              </button>
            </p>
          )}
        </>
      )}
      {error && <p className="error small">{error}</p>}
    </>
  );
}

/** WP11.1: rename this till from the till itself (admins only — uniqueness checked server-side). */
function TillNameSetting({ tillId }: { tillId: string }) {
  const [name, setName] = useState("");
  const [saved, setSaved] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    void fetchTillName(tillId).then((n) => { if (n) { setName(n); setSaved(n); } });
  }, [tillId]);

  async function save() {
    setBusy(true);
    setError("");
    try {
      await renameTill(tillId, name.trim());
      setSaved(name.trim());
    } catch (e) {
      setError(String(e instanceof Error ? e.message : e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="setting-row">
      <span className="grow">
        Till name
        <span className="muted small block">Unique across your tills — a duplicate is rejected.</span>
      </span>
      <input className="pref-input" maxLength={80} value={name} placeholder="e.g. Front Desk" onChange={(e) => setName(e.target.value)} />
      <button className="primary" disabled={busy || !name.trim() || name.trim() === saved} onClick={save}>
        Save name
      </button>
      {error && <span className="error small">{error}</span>}
    </div>
  );
}

export default function SettingsPage() {
  const [prefs, setPrefsState] = useState<Prefs>(() => getPrefs());
  const [apiStatus, setApiStatus] = useState<"checking" | "ok" | "down">("checking");
  const [testPrint, setTestPrint] = useState(false);
  const session = getSession();

  useEffect(() => {
    fetchPayMethods()
      .then(() => setApiStatus("ok"))
      .catch(() => setApiStatus("down"));
  }, []);

  const canSettings = canManageSettings();
  const toggle = (key: "autoPrintReceipt" | "askReceipt" | "newestFirst") =>
    setPrefsState(setPrefs({ [key]: !prefs[key] }));

  return (
    <section className="panel">
      <h2>Settings</h2>
      <p className="muted small">These options apply to this device only (like the native till's preferences).</p>
      {!canSettings && (
        <p className="error small">You don't have permission to change device settings — ask a manager (you can still view them and get help).</p>
      )}

      <h3 className="settings-h">Till</h3>
      <label className="setting-row">
        <span className="grow">
          Newest items at the top of the basket
          <span className="muted small block">Off = new items appended at the bottom (recommended).</span>
        </span>
        <input type="checkbox" checked={prefs.newestFirst} disabled={!canSettings} onChange={() => toggle("newestFirst")} />
      </label>
      <label className="setting-row">
        <span className="grow">
          Carrier bag barcode
          <span className="muted small block">When set, the till shows a one-tap "Bag" button that adds this item.</span>
        </span>
        <input
          className="pref-input"
          placeholder="scan or type barcode"
          value={prefs.bagBarcode}
          disabled={!canSettings}
          onChange={(e) => setPrefsState(setPrefs({ bagBarcode: e.target.value.trim() }))}
        />
      </label>

      <h3 className="settings-h">Checkout</h3>
      <label className="setting-row">
        <span className="grow">
          Ask "print receipt?" after each sale
          <span className="muted small block">The NatApp behaviour — a yes/no prompt when the sale completes.</span>
        </span>
        <input type="checkbox" checked={prefs.askReceipt} disabled={!canSettings} onChange={() => toggle("askReceipt")} />
      </label>
      <label className="setting-row">
        <span className="grow">
          Print receipt automatically after each sale
          <span className="muted small block">
            Silent when the browser runs with kiosk-printing; otherwise shows the print dialog. Ignored when "ask" is on.
          </span>
        </span>
        <input type="checkbox" checked={prefs.autoPrintReceipt} disabled={!canSettings} onChange={() => toggle("autoPrintReceipt")} />
      </label>

      <h3 className="settings-h">Printer</h3>
      <div className="setting-row">
        <span className="grow">
          Print a test receipt
          <span className="muted small block">
            Printer choice and cash-drawer kick are configured at OS/driver level (see the hosting notes: kiosk-printing +
            "open drawer on print").
          </span>
        </span>
        <button className="ghost" disabled={!canSettings} onClick={() => setTestPrint(true)}>
          Print test receipt
        </button>
      </div>

      <h3 className="settings-h">Database</h3>
      <p className="muted small">
        Unlike the native till, the webapp has no device database — the server's MySQL is the single source of truth and
        is backed up on the server. Backup/restore buttons are therefore not needed here.
      </p>

      <TillDeviceSection />

      <h3 className="settings-h">Environment</h3>
      <dl className="env-info">
        <dt>Signed in as</dt>
        <dd>{session?.name ?? "—"}</dd>
        <dt>API</dt>
        <dd>{apiStatus === "checking" ? "checking…" : apiStatus === "ok" ? "✅ reachable" : "❌ unreachable"}</dd>
        <dt>Business id</dt>
        <dd className="mono small">{BUSINESS_ID}</dd>
        <dt>App build</dt>
        <dd>{__BUILD_TIME__}</dd>
      </dl>

      {testPrint && <Receipt data={{ ...TEST_RECEIPT, date: new Date().toISOString() }} onClose={() => setTestPrint(false)} />}
    </section>
  );
}
