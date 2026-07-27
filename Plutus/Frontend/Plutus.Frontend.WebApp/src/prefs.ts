// Per-device till preferences (localStorage — device-scoped by design, like the
// native apps' Preferences store).

export interface Prefs {
  /** print automatically after each sale (silent under kiosk-printing) */
  autoPrintReceipt: boolean;
  /** ask "print receipt?" after each sale (NatApp AskForReceipt) */
  askReceipt: boolean;
  /** show newest basket lines at the top (NatApp TillListOrderReversed) */
  newestFirst: boolean;
  /** barcode of the carrier-bag item for the till's quick "Bag" button */
  bagBarcode: string;
}

const KEY = "plutus.prefs";
const DEFAULTS: Prefs = { autoPrintReceipt: false, askReceipt: false, newestFirst: false, bagBarcode: "" };

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
