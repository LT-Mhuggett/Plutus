// Scanning an unknown barcode at the till offers "Add this item" — this carries the barcode
// across to Inventory Management, which opens its Add-item dialog with it prefilled.
//
// A module-level handoff plus a window event, rather than threading a payload through the
// app's tab state: PAGES renders each tab as a bare component with no props, and one scanned
// barcode is not worth restructuring that.

const EVENT = "plutus:add-item-barcode";
let pending: string | null = null;

/** Ask the app to open Inventory Management with this barcode ready to add. */
export function requestNewItem(barcode: string): void {
  pending = barcode.trim();
  window.dispatchEvent(new CustomEvent(EVENT));
}

/** Consume the pending barcode (single use — a later visit to Inventory must not reopen it). */
export function takeNewItemBarcode(): string | null {
  const b = pending;
  pending = null;
  return b;
}

/** Fires when a barcode is handed off; the shell switches tabs, the page reads it. */
export function onNewItemRequested(handler: () => void): () => void {
  window.addEventListener(EVENT, handler);
  return () => window.removeEventListener(EVENT, handler);
}
