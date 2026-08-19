import { describe, expect, it } from "vitest";
import { mayReadReport, specificCodeFor } from "./reportPermissions.ts";

/**
 * ⚠⚠ THESE ARE THE C2 VECTORS. The same cases run in `tests/Plutus.Tests.Unit/ReportPermissionsTests.cs`
 * against `SharedKernel.ReportPermissions`, because both tills filter their own report menu and two
 * tills that disagree about who may read the VAT report disagree about who may read the VAT report.
 * **Add a case here and add it there, in the same commit.**
 */

const ALL_KEYS = [
  "summary", "vat", "items-sold", "category-sales",
  "best-sellers", "stock", "negative-stock", "sales",
];

describe("report permissions — the C2 vectors", () => {
  // ⚠⚠ THE COMPATIBILITY GUARANTEE, and the most important case here. If this goes red, the deploy
  // logs every supervisor out of every report in a live shop — their tokens cache for 12 hours with
  // the permission set baked in.
  it.each(ALL_KEYS)("the master key still opens %s", (key) => {
    expect(mayReadReport(key, ["pos.reports.view"])).toBe(true);
  });

  // ⚠ A portal reader passes too — the server already serves them these endpoints, so hiding a report
  // from somebody the SERVER would serve would be lying to them.
  it.each(["portal.reports.view", "portal.financials.view"])(
    "a portal reader (%s) sees the reports the server would serve them",
    (code) => {
      expect(mayReadReport("vat", [code])).toBe(true);
    },
  );

  // ⚠⚠ THE POINT OF THE RULING: one specific code opens exactly one report, or "separate permissions"
  // means nothing.
  it("one specific code opens exactly one report", () => {
    const vatOnly = ["pos.reports.vat"];
    expect(mayReadReport("vat", vatOnly)).toBe(true);
    expect(mayReadReport("summary", vatOnly)).toBe(false);
    expect(mayReadReport("items-sold", vatOnly)).toBe(false);
    expect(mayReadReport("stock", vatOnly)).toBe(false);
    expect(mayReadReport("sales", vatOnly)).toBe(false);
  });

  // ⚠⚠ Negative stock is the SAME rows filtered below zero — one dataset, one permission.
  it("stock and negative stock travel together", () => {
    const stockOnly = ["pos.reports.stock"];
    expect(mayReadReport("stock", stockOnly)).toBe(true);
    expect(mayReadReport("negative-stock", stockOnly)).toBe(true);
    expect(specificCodeFor("stock")).toBe(specificCodeFor("negative-stock"));
  });

  // ⚠ Every other report is an aggregate; the drill-down shows individual transactions and how each
  // was paid. A shop may want the takings readable without a customer's basket being readable.
  it("takings does not open the sale drill-down", () => {
    expect(mayReadReport("sales", ["pos.reports.takings"])).toBe(false);
    expect(mayReadReport("sales", ["pos.reports.sales"])).toBe(true);
  });

  it("nobody with no codes reads anything", () => {
    expect(mayReadReport("vat", [])).toBe(false);
  });

  // ⚠⚠ An unknown key is master-key-only, never a throw — and it fails CLOSED for a narrow role.
  it("an unmapped report falls back to the master key", () => {
    expect(specificCodeFor("some-report-from-the-future")).toBeNull();
    expect(mayReadReport("some-report-from-the-future", ["pos.reports.view"])).toBe(true);
    expect(mayReadReport("some-report-from-the-future", ["pos.reports.vat"])).toBe(false);
  });
});
