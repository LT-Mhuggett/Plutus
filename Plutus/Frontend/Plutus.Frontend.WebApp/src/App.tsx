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
import { isRevoked, mustStop, pollDeviceStanding, REVOKED_MESSAGE } from "./deviceStanding.ts";
import { DISABLED_MESSAGE, isStillPermitted, refreshRoster } from "./roster.ts";
import { hasPortalAccess, portalUrl } from "./sibling.ts";
import { onNewItemRequested } from "./newItemHandoff.ts";
import { basketLineCount } from "./till/basket.ts";
import { refreshTheme } from "./theme.ts";
import { pendingSalesForDay, queuedCount } from "./offline.ts";
import { drainCashOutbox } from "./cashOutbox.ts";
import { getSession, type Session } from "./session.ts";
import { oidcMode, signOut } from "./auth.ts";
import { beginLogin, completeLoginIfCallback } from "./oidc.ts";
import AskHost, { ask } from "./Ask.tsx";
import TillClock from "./TillClock.tsx";
import { getUnreadSupport, onUnreadSupport } from "./api.ts";

/**
 * ⚠ THE ENVIRONMENT BADGE. `VITE_ENV_BADGE=test` on the test deploy; unset in production, which shows
 * nothing. ⚠ Trimmed, so a stray space in a `.env` file does not render an empty pill.
 */
const envBadge = (import.meta.env.VITE_ENV_BADGE ?? "").trim();

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

  /**
   * ⚠⚠ WP-TABTOP (2026-08-21). Matt: *"Clicking on the tabs at the top needs to take you back to the
   * top level of that tab."*
   *
   * A page component stays MOUNTED across a tab change, so it keeps whatever sub-screen, filter and
   * expanded row it was left on. Clicking the tab you are already on did nothing visible, and coming
   * back to a tab dropped you wherever the last person left it — right for a browser, wrong for a
   * till, where the tab bar is the way OUT of somewhere.
   *
   * ⚠⚠ **THE TILL TAB IS EXEMPT, AND THAT IS NOT AN INCONSISTENCY — IT IS THE BASKET.** Remounting
   * `TillPage` mid-sale would take a part-rung basket off the screen because somebody brushed the
   * tab bar. Nothing else on this till holds unsaved work an operator cannot get back; the basket is
   * the one thing that does, and it is the whole point of the machine.
   *
   * ⚠ Same mechanism as the portal's (`App.tsx` `navEpoch`), and it must stay the same: two tills
   * whose tab bars behave differently is the 2026-08-19 look-and-feel ruling being broken by the
   * control an operator touches most.
   */
  const [navEpoch, setNavEpoch] = useState(0);
  /**
   * ⚠ THE SUPPORT BADGE COUNT, fed by the heartbeat (WP-TICKETS, 2026-08-21) — see `api.ts`
   * `onUnreadSupport`. Seeded from the last value so a re-render does not blank it, and unsubscribed
   * on unmount because a listener left behind on a till open for days is the leak that reads as
   * "it got slow".
   */
  const [unread, setUnread] = useState(getUnreadSupport);
  useEffect(() => onUnreadSupport(setUnread), []);


  const goTab = (t: Tab) => {
    setTab(t);
    if (t !== "Till") setNavEpoch((n) => n + 1);
    window.scrollTo({ top: 0 });
  };
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
  /** W-P1: latched when the platform has explicitly revoked this device. ⚠ Read from IndexedDB on
   *  boot as well as polled, so a reload does not un-revoke a till. */
  const [revoked, setRevoked] = useState(false);
  const [recheckBusy, setRecheckBusy] = useState(false);

  /** The "Check again" button on the blocked screen — lets a re-enrolled till back in without
   *  somebody clearing browser storage by hand. */
  async function recheckStanding() {
    setRecheckBusy(true);
    try {
      const standing = await pollDeviceStanding();
      if (standing !== null && !mustStop(standing)) setRevoked(false);
    } finally {
      setRecheckBusy(false);
    }
  }

  // ⚠⚠ W-P1 — THE REVOCATION POLL, and it runs WITHOUT A SESSION on purpose: a stolen till is
  // revoked while it sits at a login screen, which is precisely when nobody is signed in. That is
  // why this effect has no `session` guard and its own cadence, unlike the block below.
  //
  // ⚠ Device tokens have no server-side denylist, so this poll is the ONLY thing that stops a
  // revoked till inside its 12h token life.
  useEffect(() => {
    // The latch first, so a reload of an already-revoked till blocks before any network call.
    void isRevoked().then((r) => { if (r) setRevoked(true); });

    const check = () =>
      void pollDeviceStanding().then((standing) => {
        if (standing !== null) setRevoked(mustStop(standing));
      });

    check();
    const timer = window.setInterval(check, 60_000);
    return () => window.clearInterval(timer);
  }, []);

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
  useEffect(() => onNewItemRequested(() => goTab("Inventory Management")), []);

  // Offline plumbing: connectivity indicator, outbox badge, replay on reconnect,
  // and a background pull of the item catalogue for offline scanning.
  useEffect(() => {
    if (!session) return;
    const refreshQueued = () => void queuedCount().then(setQueued);
    refreshQueued();
    const offOutbox = onOutboxChanged(refreshQueued);
    // ⚠ W-P5: the CASH queue drains beside the sale outbox. ⚠ Sales FIRST, deliberately — a Z close
    // waits for its own day's sales, so draining cash first would just stall on a Z that the sales
    // drain is about to unblock.
    const drainBoth = async () => {
      await drainOutbox();
      await drainCashOutbox(pendingSalesForDay);
      await refreshQueued();
    };

    const goOnline = () => {
      setOnline(true);
      void drainBoth();
    };
    const goOffline = () => setOnline(false);
    window.addEventListener("online", goOnline);
    window.addEventListener("offline", goOffline);
    void drainBoth(); // catch anything queued before a reload — sales and cash
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
    // ⚠⚠ W-P2 — RE-READ THE ROSTER, AND PUT A DISABLED OPERATOR OUT. Matt, 2026-08-11: *"If a user
    // is disabled, the user needs immediately logging out with an information message."* MAUI has
    // done this since till 1.41.0; the web till signed out only REACTIVELY (`handle401`), and login
    // tokens are cached 12 HOURS carrying their permission set — so a disabled operator kept a
    // working session until something happened to 401.
    //
    // ⚠⚠ A ROSTER THAT COULD NOT BE FETCHED IS NOT AN EMPTY ROSTER. `refreshRoster` returns null on
    // any failure and `isStillPermitted` treats null as "carry on" — otherwise every broadband
    // hiccup would sign the whole shop out mid-sale, accusing the operator of being disabled, on the
    // flakiest sites first. An EMPTY roster the server actually sent does revoke, correctly.
    const pollRoster = () =>
      void refreshRoster().then((roster) => {
        if (isStillPermitted(session?.employeeId, roster)) return;
        // ⚠ The message first, then the sign-out: `signOut()` reloads the page in password mode, so
        // anything after it never runs.
        window.alert(DISABLED_MESSAGE);
        signOut();
      });
    pollRoster();
    const rosterTimer = window.setInterval(pollRoster, 60_000);

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
      window.clearInterval(rosterTimer);
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

  // ⚠⚠ W-P1: A REVOKED TILL IS BLOCKED BEFORE THE LOGIN SCREEN. Gating after login would let
  // somebody sign in to a machine the platform has finished with — and the point of a revocation is
  // that this hardware stops being a till, not that one operator stops being able to use it.
  //
  // ⚠ The flag is latched in IndexedDB, so this survives a reload. A revoked till must not come
  // back by pressing F5.
  if (revoked) {
    return (
      <div className="login-screen">
        <div className="login-card">
          <h1><PlutusMark size={34} />Plutus</h1>
          <p className="error">{REVOKED_MESSAGE}</p>
          <button className="ghost" onClick={() => void recheckStanding()} disabled={recheckBusy}>
            {recheckBusy ? "Checking…" : "Check again"}
          </button>
          <p className="muted small">
            If it has just been re-enrolled in the portal, Check again will let this till back in.
          </p>
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
    // ⚠ till-locked ONLY on the Till tab: it bounds the app to the viewport so the basket
    // scrolls inside .basket-grid and the totals + action buttons never leave the screen
    // (Matt, 2026-08-20: many items pushed Checkout off the bottom). Every other tab keeps
    // the normal page scroll — reports and inventory are meant to scroll as a page.
    <main className={tab === "Till" ? "shell till-locked" : "shell"}>
      <header className="appbar">
        <h1><PlutusMark />Plutus</h1>
        <nav className="tabs">
          {TABS.map((t) => (
            <button key={t} className={t === tab ? "tab active" : "tab"} onClick={() => goTab(t)}>
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
        {/* ⚠ THE CLOCK, LEFT OF THE ENV BADGE (2026-08-21). Matt asked for it in the same breath as
            the hour-out sale times: a live time on the screen is what makes a wrong one obvious. Its
            hover caption names the timezone, which is the half that says whether the fault is the PC
            or the data. See `TillClock`. */}
        <TillClock />
        {/* ⚠⚠ THE ENVIRONMENT BADGE IS CONFIGURED, NOT HARD-CODED (2026-08-21). Matt: *"How do I turn
            off the 'Test' on the webtill?"* — you could not. It was the literal string `test` in this
            file, so the only way to remove it from a production till was a code change and a deploy,
            and the badge that exists to say "this is not the real till" would have shipped **onto the
            real till**.

            ⚠ SET `VITE_ENV_BADGE` PER ENVIRONMENT: any text shows that text; **empty or unset shows
            nothing**. Unset is the safe default precisely because production is the environment
            nobody remembers to configure — a missing setting must fail towards "no badge", never
            towards a live till labelled `test`. */}
        {envBadge && <span className="env-badge">{envBadge}</span>}
        {/* WP6.3: Help, top-right next to the users button — raise/track support tickets.
            ⚠⚠ THE BADGE (WP-TICKETS, 2026-08-21). Matt: *"When I reply to a live ticket, how is the
            user informed?"* They were not — the reply sat in a thread nobody had a reason to open.
            The count rides the heartbeat, which is the only thing on this till that runs whether or
            not anybody is looking at the screen.
            ⚠ It clears when the THREAD is opened, not when this button is clicked: the badge is a
            consequence of the state, and clearing it here would leave the other till in the shop
            still lit. */}
        <button className="user-btn" title={unread > 0 ? `${unread} unread repl${unread === 1 ? "y" : "ies"} from Plutus support` : "Help & support"} onClick={() => setHelpOpen(true)}>
          ❓{unread > 0 && <span className="badge-dot" aria-label={`${unread} unread`}>{unread}</span>}
        </button>
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
                  goTab("Users");
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
        {/* ⚠ THE `key` IS THE RESET (WP-TABTOP) — a changed key remounts the page, which is what
            takes an operator back to a tab's top level. ⚠ `navEpoch` does NOT move for the Till tab,
            deliberately: remounting it would drop a part-rung basket. See `goTab`. */}
        <Page tab={tab} key={`${tab}:${navEpoch}`} />
      </div>

      {helpOpen && <HelpPanel onClose={() => setHelpOpen(false)} />}

      <footer className="muted">
        {session.name} · Kapow Comics ltd — seeded test data · web till v{__APP_VERSION__} · built {__BUILD_TIME__}
      </footer>
    </main>
  );
}
