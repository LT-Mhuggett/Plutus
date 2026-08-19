/**
 * Which permission lets somebody read which report — ruling 5b, 2026-08-18.
 *
 * ⚠⚠ Matt: *"Portal shows which reports a till can show. Separate permissions need to be created for
 * viewing them."* This is the web till's half of the second part. The portal choosing which reports a
 * till OFFERS is a separate work package, and the ruling settles how the two meet: **the publish
 * decides the menu, the permission decides the door.**
 *
 * ⚠⚠ C2 TWIN of `SharedKernel/ReportPermissions.cs`. Both tills filter their own menu, and two tills
 * that disagree about who may read the VAT report disagree about who may read the VAT report. The same
 * vectors run on both sides — `ReportPermissionsTests` (C#) and `reportPermissions.test.ts` (here).
 * **Add a case to one and add it to the other, in the same commit.**
 *
 * ⚠ `pos.reports.view` IS A MASTER KEY, which is the whole compatibility story: every existing role
 * keeps every report the moment this ships, and a NARROW role is built by granting specific codes and
 * not the master. Requiring the specific codes instead would have logged every supervisor out of every
 * report in a live shop.
 */

/** The master keys — any one of these opens every report. */
const MASTERS = ["pos.reports.view", "portal.reports.view", "portal.financials.view"] as const;

/**
 * Report key → the permission that specifically grants it.
 *
 * ⚠ The keys match `ReportCatalogue.Key` on MAUI, so one map describes both tills. ⚠⚠ **`stock` and
 * `negative-stock` share one code** — negative stock is the same rows filtered below zero, so
 * splitting them would invent a role that sees the worst of the data and not its context.
 */
const SPECIFIC: Record<string, string> = {
  summary: "pos.reports.takings",
  takings: "pos.reports.takings",
  vat: "pos.reports.vat",
  "items-sold": "pos.reports.items-sold",
  "category-sales": "pos.reports.category-sales",
  "best-sellers": "pos.reports.best-sellers",
  stock: "pos.reports.stock",
  "negative-stock": "pos.reports.stock",
  sales: "pos.reports.sales",
};

/**
 * The specific code for a report key, or null when the key is unknown.
 *
 * ⚠ NULL rather than a throw: a build may name a report this map has not heard of, and a reports page
 * that dies is worse than one missing a tab. `mayReadReport` then falls back to the master key — the
 * pre-5b behaviour.
 */
export function specificCodeFor(reportKey: string): string | null {
  return SPECIFIC[reportKey] ?? null;
}

/**
 * May somebody holding `heldCodes` read this report?
 *
 * ⚠ A PORTAL READER PASSES TOO — the server already serves them every one of these endpoints, so a
 * page that hid a report from somebody the SERVER would serve would be lying to them. The list and the
 * door must agree.
 */
export function mayReadReport(reportKey: string, heldCodes: readonly string[]): boolean {
  if (!heldCodes || heldCodes.length === 0) return false;
  if (MASTERS.some((m) => heldCodes.includes(m))) return true;

  const specific = specificCodeFor(reportKey);
  // ⚠ An unmapped report is master-key-only. It fails CLOSED for a narrow role, which is the right
  // direction — but it fails SILENTLY, so a report added to a catalogue and not to this map simply
  // stops appearing for anyone without a master key.
  return specific !== null && heldCodes.includes(specific);
}
