import { useState } from "react";
import Dashboard from "./Dashboard.tsx";
import VatPage from "./VatPage.tsx";
import StockPage from "./StockPage.tsx";
import PricesPage from "./PricesPage.tsx";
import UsersPage from "./UsersPage.tsx";
import StoresPage from "./StoresPage.tsx";
import PeriodsPage from "./PeriodsPage.tsx";
import LoginPage from "./LoginPage.tsx";
import { clearSession, getSession, type Session } from "./session.ts";

declare const __BUILD_TIME__: string;

const TABS = ["Dashboard", "VAT", "Stock", "Prices", "Users & Roles", "Stores & Tills", "Periods"] as const;
type Tab = (typeof TABS)[number];

const PAGES: Record<Tab, () => React.JSX.Element> = {
  Dashboard: Dashboard,
  VAT: VatPage,
  Stock: StockPage,
  Prices: PricesPage,
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
  const [session, setSession] = useState<Session | null>(() => getSession());

  if (!session) return <LoginPage onLogin={setSession} />;

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
        <span className="muted small">{session.name}</span>
        <button
          className="ghost small"
          onClick={() => {
            clearSession();
            setSession(null);
          }}
        >
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
