import { useEffect, useState } from "react";
import { fetchPayMethods, BUSINESS_ID, TILL_ID } from "./api.ts";
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

  const toggle = (key: "autoPrintReceipt" | "askReceipt" | "newestFirst") =>
    setPrefsState(setPrefs({ [key]: !prefs[key] }));

  return (
    <section className="panel">
      <h2>Settings</h2>
      <p className="muted small">These options apply to this device only (like the native till's preferences).</p>

      <h3 className="settings-h">Till</h3>
      <label className="setting-row">
        <span className="grow">
          Newest items at the top of the basket
          <span className="muted small block">Off = new items appended at the bottom (recommended).</span>
        </span>
        <input type="checkbox" checked={prefs.newestFirst} onChange={() => toggle("newestFirst")} />
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
          onChange={(e) => setPrefsState(setPrefs({ bagBarcode: e.target.value.trim() }))}
        />
      </label>

      <h3 className="settings-h">Checkout</h3>
      <label className="setting-row">
        <span className="grow">
          Ask "print receipt?" after each sale
          <span className="muted small block">The NatApp behaviour — a yes/no prompt when the sale completes.</span>
        </span>
        <input type="checkbox" checked={prefs.askReceipt} onChange={() => toggle("askReceipt")} />
      </label>
      <label className="setting-row">
        <span className="grow">
          Print receipt automatically after each sale
          <span className="muted small block">
            Silent when the browser runs with kiosk-printing; otherwise shows the print dialog. Ignored when "ask" is on.
          </span>
        </span>
        <input type="checkbox" checked={prefs.autoPrintReceipt} onChange={() => toggle("autoPrintReceipt")} />
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
        <button className="ghost" onClick={() => setTestPrint(true)}>
          Print test receipt
        </button>
      </div>

      <h3 className="settings-h">Database</h3>
      <p className="muted small">
        Unlike the native till, the webapp has no device database — the server's MySQL is the single source of truth and
        is backed up on the server. Backup/restore buttons are therefore not needed here.
      </p>

      <h3 className="settings-h">Environment</h3>
      <dl className="env-info">
        <dt>Signed in as</dt>
        <dd>{session?.name ?? "—"}</dd>
        <dt>API</dt>
        <dd>{apiStatus === "checking" ? "checking…" : apiStatus === "ok" ? "✅ reachable" : "❌ unreachable"}</dd>
        <dt>Business id</dt>
        <dd className="mono small">{BUSINESS_ID}</dd>
        <dt>Till id</dt>
        <dd className="mono small">{TILL_ID}</dd>
        <dt>App build</dt>
        <dd>{__BUILD_TIME__}</dd>
      </dl>

      {testPrint && <Receipt data={{ ...TEST_RECEIPT, date: new Date().toISOString() }} onClose={() => setTestPrint(false)} />}
    </section>
  );
}
