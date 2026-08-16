import { useEffect, useState } from "react";
import TillPage from "./till/TillPage.tsx";
import CashPage from "./CashPage.tsx";
import InventoryPage from "./InventoryPage.tsx";
import ReportingPage from "./reporting/ReportingPage.tsx";
import LoyaltyPage from "./LoyaltyPage.tsx";
import StoreInformationPage from "./StoreInformationPage.tsx";
import SettingsPage from "./SettingsPage.tsx";
import EmployeesPage from "./EmployeesPage.tsx";
import HelpPanel from "./HelpPanel.tsx";
import LoginPage from "./LoginPage.tsx";
import { PlutusMark } from "./PlutusMark.tsx";
import { ackPickNotification, drainOutbox, fetchActiveAnnouncements, showsOnATill, fetchPickNotifications, fetchTillName, loadReceiptTemplate, loadVatBands, onOutboxChanged, sendHeartbeat, syncCatalogue, type ActiveAnnouncement, type PickNotification } from "./api.ts";
import { getDeviceCredential, sessionScopes } from "./pipeline.ts";
import { startAgentReporter } from "./hardware.ts";
import { startUpdateWatcher } from "./appUpdate.ts";
import { hasPortalAccess, portalUrl } from "./sibling.ts";
import { onNewItemRequested } from "./newItemHandoff.ts";
import { basketLineCount } from "./till/basket.ts";
import { refreshTheme } from "./theme.ts";
import { queuedCount } from "./offline.ts";
import { getSession, type Session } from "./session.ts";
import { oidcMode, signOut } from "./auth.ts";
import { beginLogin, completeLoginIfCallback } from "./oidc.ts";
import AskHost, { ask } from "./Ask.tsx";

declare const __BUILD_TIME__: string;
declare const __APP_VERSION__: string;

// Menu mirrors the original NatApp till — all sections live. "Users" is reached
// via the people button, like the original intended.
const TABS = ["Till", "Cash", "Inventory Management", "Reporting", "Loyalty", "Store Information", "Settings"] as const;
type Tab = (typeof TABS)[number] | "Users";

const PAGES: Record<Tab, () => React.JSX.Element> = {
  Till: TillPage,
  Cash: CashPage,
  "Inventory Management": InventoryPage,
  Reporting: ReportingPage,
  Loyalty: LoyaltyPage,
  "Store Information": StoreInformationPage,
  Settings: SettingsPage,
  Users: EmployeesPage,
};

// Render pages as COMPONENTS (<Component/>), never as plain function calls —
// calling PAGES[tab]() inline hoists the page's hooks into App and violates
// the rules of hooks the moment App's own render shape changes (React #310).
function Page({ tab }: { tab: Tab }) {
  const Component = PAGES[tab];
  return <Component />;
}

export default function App() {
  const [tab, setTab] = useState<Tab>("Till");
  const [session, setSession] = useState<Session | null>(() => (oidcMode ? null : getSession()));
  const [booting, setBooting] = useState<boolean>(oidcMode);
  const [authError, setAuthError] = useState("");
  const [userMenu, setUserMenu] = useState(false);
  const [helpOpen, setHelpOpen] = useState(false);
  const [online, setOnline] = useState(navigator.onLine);
  const [queued, setQueued] = useState(0);
  const [tillName, setTillName] = useState<string | null>(null);
  const [updateReady, setUpdateReady] = useState(false);
  const portalHref = portalUrl();
  const [pickNotes, setPickNotes] = useState<PickNotification[]>([]);
  const [announcements, setAnnouncements] = useState<ActiveAnnouncement[]>([]);

  // OIDC mode: complete the redirect callback, or bounce to the IdP. Password mode: no-op.
  useEffect(() => {
    if (!oidcMode) return;
    void (async () => {
      try {
        const user = await completeLoginIfCallback();
        if (user) {
          setSession({
            token: "(oidc)", employeeId: "", name: user.name,
            expiresAt: new Date(Date.now() + 12 * 3600_000).toISOString(),
          });
          setBooting(false);
        } else {
          await beginLogin(); // redirect away (seamless if the IdP SSO cookie is live)
        }
      } catch (e) {
        setAuthError(String(e instanceof Error ? e.message : e));
        setBooting(false);
      }
    })();
  }, []);

  // FE3.0: poll the local hardware agent (if any) and report its version + printer health so the
  // portal's Locations page can see the fleet. Enrolled tills only; never affects till behaviour.
  useEffect(() => {
    if (!session || !getDeviceCredential()) return;
    return startAgentReporter();
  }, [session]);

  // Watch for a newer deployed build (see appUpdate.ts — banner only, never auto-reloads).
  useEffect(() => {
    if (!session) return;
    return startUpdateWatcher(() => setUpdateReady(true));
  }, [session]);

  // "Add this item" on an unknown scan — jump to Inventory, which picks up the barcode.
  useEffect(() => onNewItemRequested(() => setTab("Inventory Management")), []);

  // Offline plumbing: connectivity indicator, outbox badge, replay on reconnect,
  // and a background pull of the item catalogue for offline scanning.
  useEffect(() => {
    if (!session) return;
    const refreshQueued = () => void queuedCount().then(setQueued);
    refreshQueued();
    const offOutbox = onOutboxChanged(refreshQueued);
    const goOnline = () => {
      setOnline(true);
      void drainOutbox().then(refreshQueued);
    };
    const goOffline = () => setOnline(false);
    window.addEventListener("online", goOnline);
    window.addEventListener("offline", goOffline);
    void drainOutbox().then(refreshQueued); // catch anything queued before a reload
    void syncCatalogue().catch(() => undefined); // offline scanning working set
    void loadReceiptTemplate(); // WP11.2: cache the per-store receipt template for printing
    void loadVatBands(); // WP2c: the portal's VAT bands — no till holds a VAT rate of its own
    // WP11.1: show this till's name in the header (from its enrolled device identity).
    const cred = getDeviceCredential();
    if (cred?.tillId) void fetchTillName(cred.tillId).then(setTillName).catch(() => undefined);
    // Phase 6 pick-from-floor: poll for "sold online — pull it off the shelf" notifications on
    // the same cadence as the rest of the till's background sync (60 s; unacked persist).
    const pollNotes = () => void fetchPickNotifications().then(setPickNotes).catch(() => undefined);
    pollNotes();
    const notesTimer = window.setInterval(pollNotes, 60_000);
    // WP15.1: show announcements that belong on a till (Info is portal-only) on the same cadence.
    // ⚠ The filter is `showsOnATill`, the twin of MAUI's `NoticesClient.ShowsOnATill` — it used to be
    // an inline allow-list here, which silently dropped any severity this build had not heard of.
    const pollAnn = () => void fetchActiveAnnouncements()
      .then((a) => setAnnouncements(a.filter((x) => showsOnATill(x.severity))))
      .catch(() => undefined);
    pollAnn();
    const annTimer = window.setInterval(pollAnn, 60_000);
    // FE10: the portal-assigned theme, same cadence — assigning a scheme in the portal reaches
    // every till within a minute, no reload needed (refreshTheme applies it live).
    void refreshTheme();
    const themeTimer = window.setInterval(() => void refreshTheme(), 60_000);
    // WP2c: VAT bands on the same cadence. A rate change published in the portal — including one
    // dated for a future day — reaches every till within a minute, and the cached timeline means
    // a till that then goes offline still switches over on the day itself.
    const vatTimer = window.setInterval(() => void loadVatBands(), 60_000);
    // The web till has never told the platform it exists. /api/v1/heartbeat has been there since
    // WP5 and only the MAUI till called it, so the portal's fleet list showed "version unknown"
    // against every browser till — and "is that one on the new build?" could only be answered by
    // walking to it. Same 60s cadence as everything else here.
    void sendHeartbeat();
    const beatTimer = window.setInterval(() => void sendHeartbeat(), 60_000);
    return () => {
      offOutbox();
      window.clearInterval(notesTimer);
      window.clearInterval(annTimer);
      window.clearInterval(themeTimer);
      window.clearInterval(vatTimer);
      window.clearInterval(beatTimer);
      window.removeEventListener("online", goOnline);
      window.removeEventListener("offline", goOffline);
    };
  }, [session]);

  if (oidcMode && booting) {
    return (
      <div className="login-screen">
        <div className="login-card">
          <h1><PlutusMark size={34} />Plutus</h1>
          <p className="muted small">Signing in…</p>
          {authError && <p className="error small">{authError}</p>}
          {authError && <button className="primary" onClick={() => void beginLogin()}>Try again</button>}
        </div>
      </div>
    );
  }

  if (!session) {
    // Password mode only — OIDC redirects or errors above rather than reaching here.
    return <LoginPage onLogin={setSession} />;
  }

  function logout() {
    setUserMenu(false);
    signOut(); // clears + reloads (password) or redirects to the IdP end-session (oidc)
  }

  /** Leaving the till abandons an unsaved basket — the persisted basket survives a reload, but
   *  not a walk away to another app and back later, so say so before going. */
  async function switchToPortal() {
    if (!portalHref) return;
    const lines = basketLineCount();
    if (lines > 0) {
      const go = await ask.confirm({
        title: "Leave the till?",
        body: (
          <p className="small">
            Till items will be lost if the basket is not saved — there {lines === 1 ? "is" : "are"}{" "}
            <strong>{lines}</strong> {lines === 1 ? "line" : "lines"} in the basket. Use{" "}
            <strong>Save Transaction</strong> on the till first if you want it back later.
          </p>
        ),
        confirmLabel: "Leave anyway",
        cancelLabel: "Stay on the till",
        danger: true,
      });
      if (!go) return;
    }
    window.location.assign(portalHref);
  }

  return (
    <main className="shell">
      <header className="appbar">
        <h1><PlutusMark />Plutus</h1>
        <nav className="tabs">
          {TABS.map((t) => (
            <button key={t} className={t === tab ? "tab active" : "tab"} onClick={() => setTab(t)}>
              {t}
            </button>
          ))}
        </nav>
        {!online && <span className="offline-badge">OFFLINE</span>}
        {queued > 0 && (
          <span className="queued-badge" title="Sales made offline, waiting to sync">
            {queued} queued
          </span>
        )}
        {tillName && <span className="till-name-badge" title="This till">{tillName}</span>}
        {/* Switch to the management portal. Only for operators who can actually use it, and it
            warns first when a basket would be abandoned by leaving. */}
        {portalHref && hasPortalAccess(sessionScopes()) && (
          <button className="switch-app" title="Open the management portal" onClick={() => void switchToPortal()}>
            Switch to Portal
          </button>
        )}
        {/* A till tab stays open for days, so a deploy never reaches it on its own. Never
            auto-reloads — that would drop a basket mid-sale; the operator picks the moment. */}
        {updateReady && (
          <button
            className="update-badge"
            title="A newer version of the till has been deployed. Reload when you're between sales — anything in the basket is lost."
            onClick={() => window.location.reload()}
          >
            ⬆ Update — reload
          </button>
        )}
        <span className="env-badge">test</span>
        {/* WP6.3: Help, top-right next to the users button — raise/track support tickets. */}
        <button className="user-btn" title="Help &amp; support" onClick={() => setHelpOpen(true)}>❓</button>
        {/* the users button — the original till's people icon, now functional */}
        <div className="user-wrap">
          <button className="user-btn" title="Users" onClick={() => setUserMenu((v) => !v)}>
            👥
          </button>
          {userMenu && (
            <div className="user-menu" onMouseLeave={() => setUserMenu(false)}>
              <div className="user-name">{session.name}</div>
              <div className="muted small">signed in until {new Date(session.expiresAt).toLocaleTimeString("en-GB")}</div>
              <button
                className="ghost"
                onClick={() => {
                  setTab("Users");
                  setUserMenu(false);
                }}
              >
                Manage users
              </button>
              <button className="ghost" onClick={logout}>
                Sign out
              </button>
            </div>
          )}
        </div>
      </header>

      {/* WP15.1 platform announcements (Maintenance/Incident) — same banner style as pick-notes.
          Severity colours live in index.css (.incident/.maintenance), not inline — FE10 audit. */}
      {announcements.map((a) => (
        <div key={a.id} className={a.severity === "Incident" ? "pick-note incident" : "pick-note maintenance"}>
          <span className="grow">{a.severity === "Incident" ? "⛔" : "🛠"} {a.title}{a.body ? ` — ${a.body}` : ""}</span>
        </div>
      ))}

      {/* Phase 6 pick-from-floor: a web sale sold stock that's physically on the shelf. */}
      {pickNotes.map((n) => (
        <div key={n.id} className="pick-note">
          <span className="grow">🛒 {n.message}</span>
          <button
            className="ghost small"
            onClick={() =>
              void ackPickNotification(n.id)
                .then(() => setPickNotes((xs) => xs.filter((x) => x.id !== n.id)))
                .catch(() => undefined)
            }
          >
            Done — acknowledged
          </button>
        </div>
      ))}

      <AskHost />
      <div className="page">
        <Page tab={tab} />
      </div>

      {helpOpen && <HelpPanel onClose={() => setHelpOpen(false)} />}

      <footer className="muted">
        {session.name} · Kapow Comics ltd — seeded test data · web till v{__APP_VERSION__} · built {__BUILD_TIME__}
      </footer>
    </main>
  );
}
