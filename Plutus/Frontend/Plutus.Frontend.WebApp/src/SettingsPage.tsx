import { useEffect, useState, type ReactNode } from "react";
import { effectiveStoreId, fetchDeviceStatus, fetchPayMethods, fetchTillName, loadReceiptTemplate, onOutboxChanged, renameTill, requestUnenrol, BUSINESS_ID, STORE_ID } from "./api.ts";
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
import { currentThemeLabel } from "./theme.ts";
import {
  agentAccessState, agentAvailable, fetchAgentStatus, getAgentToken, openDrawer, printDocument,
  setAgentToken, testPrint,
  type AgentAccessState, type AgentStatus,
} from "./hardware.ts";
import { receiptToDocument } from "./till/receiptDoc.ts";
import { ask } from "./Ask.tsx";
import { getSession } from "./session.ts";
import Receipt, { ReceiptBody, type ReceiptData } from "./till/Receipt.tsx";

declare const __BUILD_TIME__: string;
declare const __APP_VERSION__: string;

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

/**
 * Official vendor driver-download pages for the common receipt printers — stable landing pages,
 * not deep file links (those rot). All verified live 2026-08-07, vendor domains only.
 * ⚠ Epson's old download.epson-biz.com portal closed June 2024 — don't link it.
 */
const DRIVER_LINKS: { label: string; url: string; covers: string }[] = [
  {
    label: "Star TSP100 futurePRNT",
    url: "https://starmicronics.com/support/download/tsp100-futureprnt-software-lite/",
    covers: "TSP100 / TSP113 / TSP143 up to the TSP100III — NOT the TSP100IV",
  },
  {
    label: "Star TSP100IV",
    url: "https://starmicronics.com/support/products/tsp100iv-support-page/",
    covers: "TSP100IV (TSP143IV) — Star Windows Software, not futurePRNT",
  },
  {
    label: "Epson TM series",
    url: "https://epson.com/Support/Point-of-Sale/Thermal-Printers/sh/s530",
    covers: "TM-T20, TM-T88, TM-m — pick your model for the Advanced Printer Driver",
  },
  {
    label: "Citizen CT-S series",
    url: "https://www.citizen-systems.com/us/support/drivers-and-tools",
    covers: "CT-S310 / CT-S601 and the rest of the CT-S range",
  },
  {
    label: "Bixolon SRP series",
    url: "https://www.bixolon.com/support.php?kind=download",
    covers: "SRP-350 family and other Bixolon POS printers",
  },
];

/**
 * FE3.3 the Hardware card: pair this browser with the Plutus Till Agent running on this PC, so
 * receipts print silently on the receipt printer and the cash drawer kicks on a cash sale.
 *
 * Everything here is optional. With no agent the till behaves exactly as it does today (browser/PDF
 * receipt, drawer opened by hand) — the card says so rather than looking broken.
 */
/** The hosted agent build, from /agent/latest.json (written by tools/Plutus.TillAgent/
 *  publish-agent.ps1). The filename carries the version so a downloaded exe is identifiable,
 *  and the manifest lets this page compare it against the installed agent. */
interface AgentDownload { version: string; file: string }

function HardwareSection({ canSettings }: { canSettings: boolean }) {
  const [status, setStatus] = useState<AgentStatus | null>(null);
  const [probed, setProbed] = useState(false);
  const [latest, setLatest] = useState<AgentDownload | "error" | null>(null);
  useEffect(() => {
    fetch("/agent/latest.json")
      .then((r) => (r.ok ? r.json() : Promise.reject(new Error(String(r.status)))))
      .then(setLatest)
      .catch(() => setLatest("error"));
  }, []);
  // when the probe finds nothing, WHY matters: agent not running vs the browser's
  // "Apps on device" permission having been blocked (the popup answered "Block").
  const [access, setAccess] = useState<AgentAccessState>("unknown");
  const [token, setToken] = useState(getAgentToken());
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState("");

  // A probe with the permission at "prompt" re-raises the browser's popup — so this
  // button doubles as the "re-request access" button. A "denied" decision is sticky:
  // no page can re-prompt; only the site-settings toggle clears it.
  const probe = () => {
    setProbed(false);
    void Promise.all([fetchAgentStatus(), agentAccessState()])
      .then(([s, a]) => { setStatus(s); setAccess(a); setProbed(true); });
  };
  useEffect(probe, []);

  return (
    <>
      {!probed ? (
        <p className="muted small">Looking for the Plutus Till Agent on this PC…</p>
      ) : !status ? (
        <>
          <p className="muted small">
            No hardware agent reachable on this PC. Receipts print through the browser (PDF) and the cash
            drawer is opened by hand — everything works, just not automatically. Install and run the{" "}
            <strong>Plutus Till Agent</strong> (download button below) to print silently and kick the drawer.
          </p>
          {access === "denied" && (
            <p className="error small">
              This browser has <strong>blocked</strong> the till's access to apps on this device — the
              permission popup was answered "Block", and a web page cannot re-ask. To fix it: click the
              icon left of the address bar → allow <strong>"Apps on device"</strong> (older Chrome/Edge
              call it <strong>"Local network access"</strong>) → reload this page.
            </p>
          )}
          {access === "prompt" && (
            <p className="small discount-note">
              The browser may show a permission popup when you check — choose <strong>Allow</strong> so
              this page can reach the agent on this PC.
            </p>
          )}
          <div className="setting-row">
            <span className="grow muted small">
              Look for the agent again{access !== "denied" ? " — this re-requests access if the browser asks" : ""}.
            </span>
            <button className="ghost" onClick={probe}>
              {access === "prompt" ? "Re-request access" : "Check again"}
            </button>
          </div>
        </>
      ) : (
        <>
          <dl className="env-info">
            <dt>Agent</dt>
            <dd>v{status.agentVersion}</dd>
            <dt>Printer</dt>
            <dd>
              {status.printer?.name
                ? <>{status.printer.name} {status.printer.online
                    ? <span className="chip ok">ready</span>
                    : <span className="chip">not responding</span>}</>
                : <span className="error">
                    none selected — on this PC: agent tray icon → Settings → pick the printer (it lists
                    every printer Windows has installed; plug in + install the driver first)
                  </span>}
            </dd>
            <dt>Paper</dt>
            <dd>{status.columns === 32 ? "58mm" : "80mm"}</dd>
            {status.emulation && (
              <>
                <dt>Language</dt>
                <dd>
                  {status.emulation === "pointofservice"
                    ? "Windows POS (direct)"
                    : status.emulation === "gdi"
                      ? "Windows driver (TSP100 family — recommended)"
                      : status.emulation === "star-raster"
                        ? "Star raster (raw — advanced)"
                        : "ESC/POS"}
                  <span className="muted small"> — chosen automatically for the selected printer</span>
                </dd>
              </>
            )}
          </dl>
          <div className="setting-row">
            <span className="grow">
              Pairing token
              <span className="muted small block">
                From the agent's tray icon → Settings. Without it the agent refuses to print — which is what
                stops any other web page on this PC driving your printer or drawer.
              </span>
            </span>
            <input
              className="pref-input"
              placeholder="e.g. K7QP2M9WXT4RH3NB8DZ2"
              value={token}
              disabled={!canSettings}
              onChange={(e) => setToken(e.target.value)}
            />
            <button className="ghost" disabled={!canSettings} onClick={() => { setAgentToken(token); setResult("Token saved."); }}>
              Save token
            </button>
          </div>
          <div className="setting-row">
            <span className="grow muted small">Send a test receipt and a drawer kick to the hardware.</span>
            <button className="ghost" disabled={!canSettings || busy} onClick={async () => {
              setBusy(true); setResult("");
              const r = await testPrint();
              setResult(r.ok ? "Test receipt sent — check the printer." : `⚠ ${r.detail}`);
              setBusy(false);
            }}>Test print</button>{" "}
            <button className="ghost" disabled={!canSettings || busy} onClick={async () => {
              setBusy(true); setResult("");
              const ok = await openDrawer();
              setResult(ok ? "Drawer kick sent." : "⚠ The drawer kick failed — check the token and the printer.");
              setBusy(false);
            }}>Open drawer</button>
          </div>
          {result && <p className="small">{result}</p>}
        </>
      )}

      {/* Downloads — shown in every state: with no agent this is how you get one; with an
          agent, the driver links are still the fix for "my printer isn't in the list". */}
      {status && latest && latest !== "error" && status.agentVersion !== latest.version && (
        <p className="small discount-note">
          ⬆ Agent v{latest.version} is available — this PC runs v{status.agentVersion}. Download it
          below, exit the tray agent (right-click its icon → Exit), replace the old file, run the
          new one. Settings and the pairing token carry over.
        </p>
      )}
      <div className="setting-row">
        <span className="grow">
          Plutus Till Agent
          <span className="muted small block">
            The small tray app that connects this browser to the receipt printer and cash drawer.
            Download it on this PC, run it (it appears by the clock), tick "start with Windows",
            pick your printer in its Settings, then copy its pairing token into the box above.
          </span>
        </span>
        {latest && latest !== "error" ? (
          <a className="ghost" href={`/agent/${latest.file}`} download>
            Download the agent (v{latest.version})
          </a>
        ) : (
          <span className="muted small">{latest === "error" ? "download unavailable — reload this page" : "checking version…"}</span>
        )}
      </div>
      <p className="muted small">
        To make the webtill be able to connect to printers, you usually need the drivers installed.
        Here are some of the most common, but you need to find the ones that match your printer
        make and model:
      </p>
      <div className="driver-links">
        {DRIVER_LINKS.map((d) => (
          <a key={d.label} className="ghost" href={d.url} target="_blank" rel="noreferrer" title={d.covers}>
            {d.label}
          </a>
        ))}
      </div>
    </>
  );
}

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
    if (!await ask.confirm({
      title: "Un-enrol this device?",
      body: (
        <p className="small">
          This till keeps working until a manager approves the removal in the management portal.
          Once approved, this browser stops trading and forgets its credential.
        </p>
      ),
      confirmLabel: "Request un-enrolment",
      danger: true,
    })) return;
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
              <span className="muted small block">
                Single-use code, valid 48h. For an EXISTING till, get it from the management portal
                (Locations → Tills → New code) so this browser takes over that till and its history.
              </span>
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
          {/* FE6.1: this button creates a BRAND-NEW till, which is how duplicate throwaway tills
              got made — someone whose browser lost its credential clicked it expecting to re-enrol.
              The wording now says so, and points at the correct operation. */}
          {canEnrolTills() && (
            <div className="setting-row">
              <span className="grow">
                Create a NEW till
                <span className="muted small block">
                  Adds another till (another counter) and gives you its enrolment code. Use this only
                  for a genuinely new position.
                </span>
                <span className="muted small block">
                  <strong>Re-enrolling an existing till instead?</strong> Don't use this — in the
                  management portal open Locations → Tills → <em>New code</em> for that till. That
                  keeps the till's identity and all of its sales history.
                </span>
              </span>
              <button className="ghost" disabled={busy} onClick={generateCode}>
                Create new till + code
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

/** Collapsible settings group — closed by default, so the page reads as a table of contents:
 *  title + one-line description on the summary row, controls revealed on click. */
function SettingsSection({ title, desc, children }: { title: string; desc: string; children: ReactNode }) {
  return (
    <details className="settings-section">
      <summary>
        <h3 className="settings-h">{title}</h3>
        <span className="muted small">{desc}</span>
      </summary>
      <div className="settings-section-body">{children}</div>
    </details>
  );
}

export default function SettingsPage() {
  const [prefs, setPrefsState] = useState<Prefs>(() => getPrefs());
  const [apiStatus, setApiStatus] = useState<"checking" | "ok" | "down">("checking");
  // renamed from `testPrint`, which shadowed the imported hardware.ts function of that name
  const [browserTest, setBrowserTest] = useState(false);
  // bumping the tick remounts the receipt preview so it re-reads the refreshed template cache
  const [tplTick, setTplTick] = useState(0);
  const [tplBusy, setTplBusy] = useState(false);
  const [paperBusy, setPaperBusy] = useState(false);
  const [paperResult, setPaperResult] = useState("");
  const session = getSession();

  /** Test print on the REAL receipt printer, via the agent — and deliberately the same document
   *  the preview shows, so it proves this store's template on paper, not the agent's own canned
   *  strip (which is what the Hardware section's "Test print" sends). */
  async function testOnPaper() {
    setPaperBusy(true);
    setPaperResult("");
    const agent = await agentAvailable();
    if (!agent) {
      setPaperResult("⚠ No hardware agent reachable on this PC — see Hardware below. Receipts still print through Windows.");
      setPaperBusy(false);
      return;
    }
    const ok = await printDocument(
      receiptToDocument({ ...TEST_RECEIPT, date: new Date().toISOString() }, agent.columns ?? 42, false),
    );
    setPaperResult(ok
      ? "Sent to the receipt printer — check the paper."
      : "⚠ The receipt printer didn't accept it. Check Hardware below: pairing token, printer selected, printer switched on.");
    setPaperBusy(false);
  }

  async function refreshTemplate() {
    setTplBusy(true);
    await loadReceiptTemplate();
    setTplTick((t) => t + 1);
    setTplBusy(false);
  }

  useEffect(() => {
    fetchPayMethods()
      .then(() => setApiStatus("ok"))
      .catch(() => setApiStatus("down"));
  }, []);

  const canSettings = canManageSettings();
  const toggle = (key: "autoPrintReceipt" | "askReceipt" | "newestFirst" | "matchAllWords") =>
    setPrefsState(setPrefs({ [key]: !prefs[key] }));

  return (
    <section className="panel">
      <h2>Settings</h2>
      <p className="muted small">These options apply to this device only (like the native till's preferences).</p>
      {!canSettings && (
        <p className="error small">You don't have permission to change device settings — ask a manager (you can still view them and get help).</p>
      )}

      <SettingsSection title="Till" desc="How the till screen behaves — basket order, search, the bag button">
      <label className="setting-row">
        <span className="grow">
          Newest items at the top of the basket
          <span className="muted small block">Off = new items appended at the bottom (recommended).</span>
        </span>
        <input type="checkbox" checked={prefs.newestFirst} disabled={!canSettings} onChange={() => toggle("newestFirst")} />
      </label>
      <label className="setting-row">
        <span className="grow">
          Item search matches each word
          <span className="muted small block">On: “batman one” finds “Batman Year One”; wrap words in quotes ("batman one") for an exact phrase. Off: the whole phrase must always appear in the name/barcode/brand.</span>
        </span>
        <input type="checkbox" checked={prefs.matchAllWords} disabled={!canSettings} onChange={() => toggle("matchAllWords")} />
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
      {/* FE10: read-only on purpose — colours are pushed from the portal (store info pattern),
          so two tills in one store can't drift apart because someone fiddled locally. */}
      <div className="setting-row">
        <span className="grow">
          Appearance
          <span className="muted small block">
            Colour scheme: <strong>{currentThemeLabel().name}</strong>
            {currentThemeLabel().source !== "default" && ` — set ${
              currentThemeLabel().source === "tenant" ? "company-wide" : `for this ${currentThemeLabel().source}`
            } in the portal`}. Change it under Locations → Till themes in the management portal;
            tills pick it up within a minute.
          </span>
        </span>
      </div>
      </SettingsSection>

      <SettingsSection title="Checkout" desc="What happens after each sale — receipt prompt or auto-print">
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
      </SettingsSection>

      <SettingsSection title="Printer" desc="The receipt this till prints — live preview, and a test print on paper or through Windows">
      <div className="setting-row">
        <span className="grow">
          Receipt layout
          <span className="muted small block">
            What this till will print, using store {effectiveStoreId()}'s template with sample items.
            The layout is controlled from the portal — Stores &amp; Tills → your store → Receipt
            template — and each store prints its own details. The till picks up changes at sign-in,
            or right now with Refresh.
          </span>
        </span>
        <button className="ghost" onClick={() => void refreshTemplate()} disabled={tplBusy}>
          {tplBusy ? "Refreshing…" : "Refresh"}
        </button>
      </div>
      {/* no-print: if a test print fires while this is on screen, only the dialog's copy prints */}
      <div className="receipt-preview no-print" key={tplTick}>
        <ReceiptBody data={{ ...TEST_RECEIPT, date: new Date().toISOString() }} />
      </div>

      <div className="setting-row">
        <span className="grow">
          Test print on the receipt printer
          <span className="muted small block">
            Prints the receipt above on the real printer via the Plutus Till Agent — silent, no
            dialog, cut at the end. This is the one that proves the hardware.
          </span>
        </span>
        <button className="ghost" disabled={!canSettings || paperBusy} onClick={() => void testOnPaper()}>
          {paperBusy ? "Sending…" : "Print on receipt printer"}
        </button>
      </div>
      {paperResult && <p className="small">{paperResult}</p>}

      <div className="setting-row">
        <span className="grow">
          Test print through Windows (browser)
          <span className="muted small block">
            The browser/PDF route with the usual print dialog — always available, and what the till
            falls back to when there is no hardware agent.
          </span>
        </span>
        <button className="ghost" disabled={!canSettings} onClick={() => setBrowserTest(true)}>
          Print via Windows
        </button>
      </div>
      </SettingsSection>

      <SettingsSection title="Hardware" desc="Receipt printer & cash drawer — pair the Plutus Till Agent on this PC">
        <HardwareSection canSettings={canSettings} />
      </SettingsSection>

      <SettingsSection title="Database" desc="Where this till's data lives">
      <p className="muted small">
        Unlike the native till, the webapp has no device database — the server's MySQL is the single source of truth and
        is backed up on the server. Backup/restore buttons are therefore not needed here.
      </p>
      </SettingsSection>

      <SettingsSection title="Till device" desc="This browser's enrolment as a till — identity, sync queue, un-enrol">
        <TillDeviceSection />
      </SettingsSection>

      <SettingsSection title="Environment" desc="Who's signed in, API status and app build">
      <dl className="env-info">
        <dt>Signed in as</dt>
        <dd>{session?.name ?? "—"}</dd>
        <dt>API</dt>
        <dd>{apiStatus === "checking" ? "checking…" : apiStatus === "ok" ? "✅ reachable" : "❌ unreachable"}</dd>
        <dt>Business id</dt>
        <dd className="mono small">{BUSINESS_ID}</dd>
        <dt>Till version</dt>
        <dd className="mono">{__APP_VERSION__}</dd>
        <dt>App build</dt>
        <dd>{__BUILD_TIME__}</dd>
      </dl>
      </SettingsSection>

      {browserTest && <Receipt data={{ ...TEST_RECEIPT, date: new Date().toISOString() }} onClose={() => setBrowserTest(false)} />}
    </section>
  );
}
