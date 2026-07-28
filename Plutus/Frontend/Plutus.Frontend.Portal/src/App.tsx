import { useEffect, useState } from "react";
import Dashboard from "./Dashboard.tsx";
import ReportingPage from "./ReportingPage.tsx";
import BankingPage from "./BankingPage.tsx";
import CustomersPage from "./CustomersPage.tsx";
import LoyaltyPage from "./LoyaltyPage.tsx";
import StockPage from "./StockPage.tsx";
import PricesPage from "./PricesPage.tsx";
import UsersPage from "./UsersPage.tsx";
import StoresPage from "./StoresPage.tsx";
import WebstorePage from "./WebstorePage.tsx";
import CompanyPage from "./CompanyPage.tsx";
import PlatformPage from "./PlatformPage.tsx";
import LoginPage from "./LoginPage.tsx";
import { getSession, type Session } from "./session.ts";
import { impersonatingAs, isPlatformAdmin, oidcMode, signOut, stopImpersonation } from "./auth.ts";
import { beginLogin, completeLoginIfCallback } from "./oidc.ts";

declare const __BUILD_TIME__: string;

// WP11.5 (Matt): "Dashboard" always takes you home; "Company" holds company details + the
// absorbed Financial periods; "Locations" is the WP11.6 grouped stores/warehouses/webstores page.
const TABS = ["Dashboard", "Reporting", "Banking", "Stock", "Prices", "Customers", "Loyalty", "Webstore", "Users & Roles", "Locations", "Company"] as const;
// WP13.4: the operator-only Platform section, shown only when the token carries platform-admin.
const PLATFORM_TAB = "Platform" as const;
type Tab = (typeof TABS)[number] | typeof PLATFORM_TAB;

const PAGES: Record<Tab, () => React.JSX.Element> = {
  Dashboard: DashboardTab,
  Reporting: ReportingPage,
  Banking: BankingPage,
  Stock: StockPage,
  Prices: PricesPage,
  Customers: CustomersPage,
  Loyalty: LoyaltyPage,
  Webstore: WebstorePage,
  "Users & Roles": UsersPage,
  Locations: StoresPage,
  Company: CompanyPage,
  Platform: PlatformPage,
};

// The same analytics view Reporting → Summary shows — one component, two doors (decided at
// build time per the WP11.5 note; no data difference).
function DashboardTab() {
  return <Dashboard />;
}

function Page({ tab }: { tab: Tab }) {
  const Component = PAGES[tab];
  return <Component />;
}

export default function App() {
  const [tab, setTab] = useState<Tab>("Dashboard");
  // password mode: seed from the stored session. oidc mode: resolved by the effect below.
  const [name, setName] = useState<string | null>(() => (oidcMode ? null : getSession()?.name ?? null));
  const [booting, setBooting] = useState<boolean>(oidcMode);
  const [error, setError] = useState("");

  useEffect(() => {
    if (!oidcMode) return;
    void (async () => {
      try {
        const user = await completeLoginIfCallback();
        if (user) {
          setName(user.name);
          setBooting(false);
        } else {
          // Not a callback and no token — bounce to the IdP (seamless if SSO cookie is live).
          await beginLogin();
        }
      } catch (e) {
        setError(String(e instanceof Error ? e.message : e));
        setBooting(false);
      }
    })();
  }, []);

  if (booting) {
    return (
      <main className="login-shell">
        <div className="login-card">
          <h1>Plutus Portal</h1>
          <p className="muted small">Signing in…</p>
          {error && <p className="error small">{error}</p>}
          {error && <button className="primary" onClick={() => void beginLogin()}>Try again</button>}
        </div>
      </main>
    );
  }

  // Password mode only: no session → show the login form. (OIDC never reaches here unauthenticated
  // — it either redirects or errors above.)
  if (!name) return <LoginPage onLogin={(s: Session) => setName(s.name)} />;

  const tabs: Tab[] = isPlatformAdmin() ? [...TABS, PLATFORM_TAB] : [...TABS];
  const impersonating = impersonatingAs();

  return (
    <main className="shell">
      {impersonating && (
        <div style={{ background: "#dc2626", color: "white", padding: "6px 12px", display: "flex", alignItems: "center", gap: 12, fontWeight: 600 }}>
          <span>⚠ Viewing as {impersonating} — actions are audited</span>
          <button className="ghost small" style={{ background: "white", color: "#dc2626" }} onClick={() => stopImpersonation()}>Stop</button>
        </div>
      )}
      <header className="appbar">
        <h1>Plutus Portal</h1>
        <nav className="tabs">
          {tabs.map((t) => (
            <button key={t} className={t === tab ? "tab active" : "tab"} onClick={() => setTab(t)}>
              {t}
            </button>
          ))}
        </nav>
        <span className="muted small">{name}</span>
        <button className="ghost small" onClick={() => signOut()}>
          Sign out
        </button>
      </header>

      <div className="page">
        <Page tab={tab} />
      </div>

      <footer className="muted small">Plutus management portal · built {__BUILD_TIME__}</footer>
    </main>
  );
}
