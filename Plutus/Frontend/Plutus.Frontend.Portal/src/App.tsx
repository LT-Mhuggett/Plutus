import { useEffect, useState } from "react";
import Dashboard from "./Dashboard.tsx";
import { PlutusMark } from "./PlutusMark.tsx";
import ReportingPage from "./ReportingPage.tsx";
import BankingPage from "./BankingPage.tsx";
import CustomersPage from "./CustomersPage.tsx";
import LoyaltyPage from "./LoyaltyPage.tsx";
import GiftCardsPage from "./GiftCardsPage.tsx";
import InventoryPage from "./InventoryPage.tsx";
import PricesPage from "./PricesPage.tsx";
import UsersPage from "./UsersPage.tsx";
import StoresPage from "./StoresPage.tsx";
import WebstorePage from "./WebstorePage.tsx";
import CompanyPage from "./CompanyPage.tsx";
import VatPage from "./VatPage.tsx";
import PlatformPage from "./PlatformPage.tsx";
import HelpPage from "./HelpPage.tsx";
import LoginPage from "./LoginPage.tsx";
import { clearSession, getSession, type Session } from "./session.ts";
import { canUseTill, impersonatingAs, isOidcSession, isOperatorOnly, isPlatformAdmin, signOut, stopImpersonation, tillUrl } from "./auth.ts";
import { NavContext, tabSlug } from "./nav.tsx";

// Keycloak self-service (password + MFA). Only meaningful for an OIDC session.
const ACCOUNT_CONSOLE = "https://login.plutus.huggett.dscloud.me/realms/plutus/account";
import { fetchActiveAnnouncements, type ActiveAnnouncement } from "./api.ts";
import { completeLoginIfCallback } from "./oidc.ts";
import AskHost from "./Ask.tsx";

declare const __BUILD_TIME__: string;
declare const __APP_VERSION__: string;

// WP11.5 (Matt): "Dashboard" always takes you home; "Company" holds company details + the
// absorbed Financial periods; "Locations & Tills" is the WP11.6 grouped stores/warehouses/webstores page.
// WP2c: VAT is its own tab, next to Banking — the portal is the source of VAT truth (the bands the
// tills apply are edited there), so it is no longer just one report under Reporting.
const TABS = ["Dashboard", "Reporting", "Banking", "VAT", "Inventory", "Prices", "Customers", "Loyalty", "Gift cards", "Webstore", "Users & Roles", "Locations & Tills", "Company", "Help"] as const;
// WP13.4: the operator-only Platform section, shown only when the token carries platform-admin.
const PLATFORM_TAB = "Platform" as const;
type Tab = (typeof TABS)[number] | typeof PLATFORM_TAB;

const PAGES: Record<Tab, () => React.JSX.Element> = {
  Dashboard: DashboardTab,
  Reporting: ReportingPage,
  Banking: BankingPage,
  VAT: VatPage,
  Inventory: InventoryPage,
  Prices: PricesPage,
  Customers: CustomersPage,
  Loyalty: LoyaltyPage,
  "Gift cards": GiftCardsPage,
  Webstore: WebstorePage,
  "Users & Roles": UsersPage,
  "Locations & Tills": StoresPage,
  Company: CompanyPage,
  Help: HelpPage,
  Platform: PlatformPage,
};

// The same analytics view Reporting → Summary shows — one component, two doors (decided at
// build time per the WP11.5 note; no data difference).
function DashboardTab() {
  return <Dashboard variant="dashboard" />;
}

function Page({ tab }: { tab: Tab }) {
  const Component = PAGES[tab];
  return <Component />;
}

// WP15.1: dismissible announcement banners (dismissal is a localStorage flag keyed by id — not
// the API token — so it survives reloads). Polls on mount + every 2 min (the sync cadence).
function Announcements() {
  const [items, setItems] = useState<ActiveAnnouncement[]>([]);
  const [, force] = useState(0);
  useEffect(() => {
    const load = () => fetchActiveAnnouncements().then(setItems).catch(() => undefined);
    void load();
    const t = setInterval(load, 120_000);
    return () => clearInterval(t);
  }, []);
  const colour = (s: string) => (s === "Incident" ? "#dc2626" : s === "Maintenance" ? "#d97706" : "#2563eb");
  const shown = items.filter((a) => localStorage.getItem(`plutus.portal.dismissed.${a.id}`) == null);
  if (shown.length === 0) return null;
  return (
    <>
      {shown.map((a) => (
        <div key={a.id} style={{ background: colour(a.severity), color: "white", padding: "6px 12px", display: "flex", gap: 12, alignItems: "center" }}>
          <strong>{a.severity}:</strong> <span className="grow">{a.title}{a.body ? ` — ${a.body}` : ""}</span>
          <button className="ghost small" style={{ background: "white", color: colour(a.severity) }}
            onClick={() => { localStorage.setItem(`plutus.portal.dismissed.${a.id}`, "1"); force((n) => n + 1); }}>Dismiss</button>
        </div>
      ))}
    </>
  );
}

const ALL_TABS: Tab[] = [...TABS, PLATFORM_TAB];
const tabFromHash = (): Tab => {
  const h = window.location.hash.replace(/^#/, "");
  return (ALL_TABS.find((t) => tabSlug(t) === h) as Tab | undefined) ?? "Dashboard";
};

export default function App() {
  const [tab, setTabState] = useState<Tab>(tabFromHash);
  const [focus, setFocus] = useState<string | undefined>(undefined);

  // ⚠⚠ WP-TABTOP (2026-08-21). Matt: *"Clicking on the tabs at the top needs to take you back to the
  // top level of that tab."*
  //
  // A page component stays MOUNTED across a tab change, so it keeps whatever sub-screen, filter,
  // expanded card and dialog it was left on. Clicking the tab you are already on therefore did
  // nothing visible at all, and coming back to a tab dropped you three screens deep in it — right
  // for a browser, wrong for a till, where the tab bar is the way out of somewhere.
  //
  // ⚠ AN EPOCH ON THE `key`, NOT A RESET METHOD PER PAGE. Fifteen pages each remembering to expose
  // "go back to your top level" is fifteen chances to forget; changing the key makes React do it,
  // for every page, including ones not written yet.
  //
  // ⚠ IT BUMPS ON **EVERY** `go`, not only on a click of the current tab. A drill that lands on
  // Reporting should also arrive at its top level — landing on a page still showing the last
  // operator's filter is the same fault wearing a different hat.
  const [navEpoch, setNavEpoch] = useState(0);
  // Navigate to a tab (from a nav button or a pill/link elsewhere). Mirrors into the URL hash so
  // reload + deep-links work; `focus` is an optional hint the target page may consume.
  const go = (t: string, f?: string) => {
    const match = ALL_TABS.find((x) => x === t || tabSlug(x) === t);
    if (!match) return;
    setTabState(match);
    setFocus(f);
    setNavEpoch((n) => n + 1);

    // ⚠ AND BACK TO THE TOP OF THE PAGE. A remounted page renders from its first row while the
    // window is still scrolled to where the last one ended — so the operator lands halfway down a
    // fresh screen, which reads as a page that failed to load its header.
    window.scrollTo({ top: 0 });

    if (window.location.hash !== `#${tabSlug(match)}`) window.location.hash = tabSlug(match);
  };
  useEffect(() => {
    const onHash = () => setTabState(tabFromHash());
    window.addEventListener("hashchange", onHash);
    return () => window.removeEventListener("hashchange", onHash);
  }, []);

  // Email-first: the boot effect resolves the name — first any Keycloak callback, then a stored
  // password session. No auto-redirect to the IdP; the landing decides per-email.
  const [name, setName] = useState<string | null>(null);
  const [booting, setBooting] = useState<boolean>(true);
  const [error, setError] = useState("");

  useEffect(() => {
    void (async () => {
      try {
        // Returning from Keycloak (?code=&state=) completes the OIDC login; otherwise this is null
        // and we fall back to any stored password session.
        const user = await completeLoginIfCallback();
        if (user) { clearSession(); setName(user.name); } // drop any stale password session
        else setName(getSession()?.name ?? null);
      } catch (e) {
        setError(String(e instanceof Error ? e.message : e));
      } finally {
        setBooting(false);
      }
    })();
  }, []);

  if (booting || error) {
    return (
      <main className="login-shell">
        <div className="login-card">
          <h1><PlutusMark size={30} />Plutus Portal</h1>
          <p className="muted small">{error ? "Sign-in problem" : "Loading…"}</p>
          {error && <p className="error small">{error}</p>}
          {error && <button className="primary" onClick={() => window.location.assign("/")}>Back to sign in</button>}
        </div>
      </main>
    );
  }

  // No session → the email-first landing. Password users sign in here; MFA/SSO users are redirected
  // to Keycloak from within it.
  if (!name) return <LoginPage onLogin={(s: Session) => setName(s.name)} />;

  // OP1: a pure operator (platform-admin, no tenant identity) gets the OPERATOR portal only —
  // no client tabs at all. The Platform screens ARE the app. Client data is reachable only by
  // impersonating a tenant (which swaps in a tid-bearing session → the branch below).
  if (isOperatorOnly()) {
    return (
      <main className="shell">
        <header className="appbar">
          <h1><PlutusMark size={24} />Plutus Operator</h1>
          <span className="grow" />
          <span className="muted small">{name}</span>
          {isOidcSession() && <a className="ghost small" href={ACCOUNT_CONSOLE} target="_blank" rel="noreferrer">Account &amp; MFA</a>}
          <button className="ghost small" onClick={() => signOut()}>Sign out</button>
        </header>
        <PlatformPage />
        <footer className="muted small">Plutus operator console · portal v{__APP_VERSION__} · built {__BUILD_TIME__}</footer>
      </main>
    );
  }

  const tabs: Tab[] = isPlatformAdmin() ? [...TABS, PLATFORM_TAB] : [...TABS];
  const impersonating = impersonatingAs();
  const tillHref = tillUrl();

  return (
    <NavContext.Provider value={{ tab, focus, go }}>
    <main className="shell">
      {impersonating && (
        <div style={{ background: "#dc2626", color: "white", padding: "6px 12px", display: "flex", alignItems: "center", gap: 12, fontWeight: 600 }}>
          <span>⚠ Viewing as {impersonating} — actions are audited</span>
          <button className="ghost small" style={{ background: "white", color: "#dc2626" }} onClick={() => stopImpersonation()}>Stop</button>
        </div>
      )}
      <header className="appbar">
        <h1><PlutusMark size={24} />Plutus Portal</h1>
        <nav className="tabs">
          {tabs.map((t) => (
            <button key={t} className={t === tab ? "tab active" : "tab"} onClick={() => go(t)}>
              {t}
            </button>
          ))}
        </nav>
        <span className="muted small">{name}</span>
        {/* Counterpart of the till's "Switch to Portal" — shown only to operators who can sell.
            No basket to lose on this side, so it just goes. */}
        {tillHref && canUseTill() && (
          <a className="ghost small" href={tillHref} title="Open the till">Switch to Till</a>
        )}
        <button className="ghost small" onClick={() => signOut()}>
          Sign out
        </button>
      </header>

      <AskHost />
      <Announcements />

      <div className="page">
        {/* ⚠ THE `key` IS THE RESET (WP-TABTOP). A changed key remounts the page, and a remount is
            what takes an operator back to a tab's top level instead of wherever they left it. */}
        <Page tab={tab} key={`${tab}:${navEpoch}`} />
      </div>

      <footer className="muted small">Plutus management portal · portal v{__APP_VERSION__} · built {__BUILD_TIME__}</footer>
    </main>
    </NavContext.Provider>
  );
}
