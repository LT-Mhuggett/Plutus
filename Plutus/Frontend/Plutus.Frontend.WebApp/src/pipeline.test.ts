import { describe, expect, it } from "vitest";
import { mayAddCustomer } from "./pipeline.ts";

/**
 * WP12 / binding default 20 (Matt, 2026-08-13): *"Supervisor to change tiers. Till operator to add
 * new loyalty members."*
 *
 * ⚠ These test the ADD gate only. There is deliberately no `mayEditCustomer` — editing stays on
 * `canManageCustomers()`, and the point of this pair is that they are DIFFERENT bars. A test that
 * asserted one helper for both would pass while the distinction was lost.
 *
 * ⚠ The server is the real gate (`perm:customers.manage,pos.customers.add`). This decides whether a
 * BUTTON is shown, so getting it wrong is visible either way — a missing button, or a 403. It is
 * tested because the two codes are easy to transpose, not because it guards money.
 */
describe("mayAddCustomer — who can sign a new member up", () => {
  it("lets a cashier holding pos.customers.add add a member", () => {
    expect(mayAddCustomer(["pos.sell", "pos.customers.add"])).toBe(true);
  });

  it("still lets a supervisor/manager add, via customers.manage", () => {
    // ⚠ Nobody who could already add a member may lose the ability — mirrors the server's OR.
    expect(mayAddCustomer(["pos.sell", "customers.manage"])).toBe(true);
  });

  it("refuses a plain cashier holding neither", () => {
    expect(mayAddCustomer(["pos.sell"])).toBe(false);
    expect(mayAddCustomer([])).toBe(false);
  });

  it("is not satisfied by a similar-looking scope", () => {
    // The near-misses that a transposition or a copy/paste would produce.
    expect(mayAddCustomer(["pos.customers"])).toBe(false);
    expect(mayAddCustomer(["customers.add"])).toBe(false);
    expect(mayAddCustomer(["pos.customers.edit"])).toBe(false);
    expect(mayAddCustomer(["pos.stock.adjust"])).toBe(false);
  });
});
