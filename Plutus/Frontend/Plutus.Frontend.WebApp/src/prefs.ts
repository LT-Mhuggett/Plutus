// Per-device till preferences (localStorage — device-scoped by design, like the
// native apps' Preferences store).

export interface Prefs {
  /** print automatically after each sale (silent under kiosk-printing) */
  autoPrintReceipt: boolean;
  /** ask "print receipt?" after each sale (NatApp AskForReceipt) */
  askReceipt: boolean;
  /** show newest basket lines at the top (NatApp TillListOrderReversed) */
  newestFirst: boolean;
  // ⚠ `bagBarcode` was HERE and is gone (ruling 2026-08-19): one bag barcode, per device, free text,
  // validated by nothing — so a five-till shop set it five times and this till held "001", a barcode
  // no item has. Carrier bags now come from the portal; see `till/carrierBags.ts`. A stale key left in
  // a browser's localStorage is harmless — `getPrefs` spreads over DEFAULTS and ignores extras.
  /** item search matches each word separately — "batman one" finds "Batman Year One";
   *  off = the whole phrase must appear (the original behaviour) */
  matchAllWords: boolean;
}

const KEY = "plutus.prefs";
const DEFAULTS: Prefs = { autoPrintReceipt: false, askReceipt: false, newestFirst: false, matchAllWords: true };

export function getPrefs(): Prefs {
  try {
    return { ...DEFAULTS, ...JSON.parse(localStorage.getItem(KEY) ?? "{}") };
  } catch {
    return { ...DEFAULTS };
  }
}

export function setPrefs(p: Partial<Prefs>): Prefs {
  const next = { ...getPrefs(), ...p };
  localStorage.setItem(KEY, JSON.stringify(next));
  return next;
}
