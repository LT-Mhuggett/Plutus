import { createContext, useContext } from "react";

// Portal tab navigation. App has no router; this lets any component jump to a tab
// (e.g. the Dashboard pills → Reporting / Locations / Webstore) and optionally pass a
// `focus` hint the target page can act on (e.g. open the Warehouses group on Locations).
// The active tab is mirrored into location.hash so tabs survive reload + are deep-linkable.

export interface Nav {
  tab: string;
  focus?: string;
  go: (tab: string, focus?: string) => void;
}

export const NavContext = createContext<Nav>({ tab: "Dashboard", go: () => {} });
export const useNav = () => useContext(NavContext);

/** Tab name → URL-hash slug ("Users & Roles" → "users-roles"). */
export const tabSlug = (tab: string) =>
  tab.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/(^-|-$)/g, "");
