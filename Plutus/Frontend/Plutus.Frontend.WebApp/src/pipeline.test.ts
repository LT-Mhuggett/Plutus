import { describe, expect, it } from "vitest";
import { mayAddCustomer, mayAdjustStock } from "./pipeline.ts";

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

/**
 * Who may change a stock count from the till (2026-08-25).
 *
 * ⚠⚠ THE PAIR IS THE POINT. `POST /api/v1/stock/movements` is gated
 * `perm:portal.stock.adjust,pos.stock.adjust`, and accepting only the till code is a bug this
 * platform has already shipped once: MAUI asked for `pos.stock.adjust` alone until 2026-08-11 and
 * refused an **Owner** — who holds the portal code — for something the server would have allowed.
 *
 * ⚠ A test that only checked the till code would pass while that exact fault was reintroduced.
 */
describe("mayAdjustStock — who can change a stock count from the till", () => {
  it("lets a supervisor holding the till code adjust", () => {
    expect(mayAdjustStock(["pos.sell", "pos.stock.adjust"])).toBe(true);
  });

  it("lets an owner/manager holding the PORTAL code adjust", () => {
    // ⚠ The regression guard: a portal-only holder must not be refused by the till.
    expect(mayAdjustStock(["portal.stock.adjust"])).toBe(true);
  });

  it("refuses a plain cashier", () => {
    // ⚠ Load-bearing: the person minding the shelf and the person who can alter its count must
    // differ, or shrinkage stops being visible (Matt, 2026-08-11).
    expect(mayAdjustStock(["pos.sell"])).toBe(false);
    expect(mayAdjustStock([])).toBe(false);
  });

  it("is not satisfied by a similar-looking scope", () => {
    expect(mayAdjustStock(["stock.adjust"])).toBe(false);
    expect(mayAdjustStock(["pos.stock"])).toBe(false);
    expect(mayAdjustStock(["pos.stock.adjustment"])).toBe(false);
    expect(mayAdjustStock(["pos.items.manage"])).toBe(false);
  });
});
