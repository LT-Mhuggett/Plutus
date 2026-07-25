import { useEffect, useState } from "react";
import Dashboard from "./Dashboard.tsx";
import VatPage from "./VatPage.tsx";
import BankingPage from "./BankingPage.tsx";
import CustomersPage from "./CustomersPage.tsx";
import StockPage from "./StockPage.tsx";
import PricesPage from "./PricesPage.tsx";
import UsersPage from "./UsersPage.tsx";
import StoresPage from "./StoresPage.tsx";
import PeriodsPage from "./PeriodsPage.tsx";
import ItemsSoldPage from "./ItemsSoldPage.tsx";
import LoginPage from "./LoginPage.tsx";
import { getSession, type Session } from "./session.ts";
import { oidcMode, signOut } from "./auth.ts";
import { beginLogin, completeLoginIfCallback } from "./oidc.ts";

declare const __BUILD_TIME__: string;

const TABS = ["Dashboard", "VAT", "Banking", "Stock", "Prices", "Items Sold", "Customers", "Users & Roles", "Stores & Tills", "Periods"] as const;
type Tab = (typeof TABS)[number];

const PAGES: Record<Tab, () => React.JSX.Element> = {
  Dashboard: Dashboard,
  VAT: VatPage,
  Banking: BankingPage,
  Stock: StockPage,
  Prices: PricesPage,
  "Items Sold": ItemsSoldPage,
  Customers: CustomersPage,
  "Users & Roles": UsersPage,
  "Stores & Tills": StoresPage,
  Periods: PeriodsPage,
};

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

  return (
    <main className="shell">
      <header className="appbar">
        <h1>Plutus Portal</h1>
        <nav className="tabs">
          {TABS.map((t) => (
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
