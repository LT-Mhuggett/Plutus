import { useEffect, useState } from "react";
import TillPage from "./till/TillPage.tsx";
import InventoryPage from "./InventoryPage.tsx";
import ReportingPage from "./reporting/ReportingPage.tsx";
import StoreInformationPage from "./StoreInformationPage.tsx";
import SettingsPage from "./SettingsPage.tsx";
import EmployeesPage from "./EmployeesPage.tsx";
import LoginPage from "./LoginPage.tsx";
import { drainOutbox, onOutboxChanged, syncCatalogue } from "./api.ts";
import { queuedCount } from "./offline.ts";
import { clearSession, getSession, type Session } from "./session.ts";

declare const __BUILD_TIME__: string;

// Menu mirrors the original NatApp till — all sections live. "Users" is reached
// via the people button, like the original intended.
const TABS = ["Till", "Inventory Management", "Reporting", "Store Information", "Settings"] as const;
type Tab = (typeof TABS)[number] | "Users";

const PAGES: Record<Tab, () => React.JSX.Element> = {
  Till: TillPage,
  "Inventory Management": InventoryPage,
  Reporting: ReportingPage,
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
  const [session, setSession] = useState<Session | null>(() => getSession());
  const [userMenu, setUserMenu] = useState(false);
  const [online, setOnline] = useState(navigator.onLine);
  const [queued, setQueued] = useState(0);

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
    return () => {
      offOutbox();
      window.removeEventListener("online", goOnline);
      window.removeEventListener("offline", goOffline);
    };
  }, [session]);

  if (!session) {
    return <LoginPage onLogin={setSession} />;
  }

  function logout() {
    clearSession();
    setSession(null);
    setUserMenu(false);
  }

  return (
    <main className="shell">
      <header className="appbar">
        <h1>Plutus</h1>
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
        <span className="env-badge">test</span>
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

      <div className="page">
        <Page tab={tab} />
      </div>

      <footer className="muted">
        {session.name} · Kapow Comics ltd — seeded test data · built {__BUILD_TIME__}
      </footer>
    </main>
  );
}
