import { describe, expect, it } from "vitest";
import { apportionChange, assess, parseAmounts, refusalReason, restFor } from "./tendering.ts";
import { gbp } from "../money.ts";

/**
 * The web till's half of the tendering C2 twin.
 *
 * ⚠⚠ THIS FILE EXISTS BECAUSE THE TWIN WAS VERIFIED ON ONE SIDE ONLY. `Plutus.Client.Core.TenderLoop`
 * has 19 mutation-checked tests; this side had none, because the WebApp had no test runner at all.
 * The cases below are deliberately the SAME cases as `TenderLoopTests` — a £10 basket settled £4
 * then £6, a refund that must not give change, an overpay that only cash can cover — so that if the
 * two tills ever diverge on a penny, one of the two suites goes red instead of a VAT return going
 * quietly wrong for a quarter.
 *
 * ⚠ CASH is changeable, CARD is not. That pairing is easy to invert and the MAUI side
 * mutation-checks it, so it is pinned here too.
 */

const CASH = { id: 1, isChangeable: true };
const CARD = { id: 2, isChangeable: false };
const METHODS = [CASH, CARD];

describe("parseAmounts", () => {
  it("skips untouched rows rather than reading them as zero", () => {
    const p = parseAmounts({ 1: "", 2: "   " });
    expect(p.valid).toBe(true);
    if (p.valid) {
      expect(p.paid).toBe(0);
      expect(p.perMethod.size).toBe(0);
    }
  });

  // ⚠ A basket that is 90% parseable is not 90% payable. Completing on a partial read would take a
  // different sum from the one on screen.
  it("invalidates the WHOLE set when one box is not a number", () => {
    expect(parseAmounts({ 1: "4.00", 2: "abc" }).valid).toBe(false);
  });

  it("reads two methods into two tenders", () => {
    const p = parseAmounts({ 1: "4.00", 2: "6.00" });
    expect(p.valid).toBe(true);
    if (p.valid) {
      expect(p.paid).toBe(1000);
      expect(p.perMethod.get(1)).toBe(400);
      expect(p.perMethod.get(2)).toBe(600);
    }
  });
});

describe("a £10 basket settled £4 then £6 — the split-payment case", () => {
  it("is short by £6 after the first £4, and settles on the second", () => {
    const first = assess(1000, parseAmounts({ 1: "4.00" }), METHODS);
    expect(first.remaining).toBe(600);
    expect(first.overpay).toBe(0);

    const both = assess(1000, parseAmounts({ 1: "4.00", 2: "6.00" }), METHODS);
    expect(both.remaining).toBe(0);
    expect(both.overpay).toBe(0);
    expect(refusalReason(both, parseAmounts({ 1: "4.00", 2: "6.00" }), gbp)).toBeNull();
  });

  // ⚠ The same words the operator is owed. MAUI says "There is £6.00 left to pay"; this side had
  // only a greyed-out button until `refusalReason` existed.
  it("says how much is left rather than only refusing", () => {
    const p = parseAmounts({ 1: "4.00" });
    expect(refusalReason(assess(1000, p, METHODS), p, gbp)).toBe("£6.00 still to pay.");
  });
});

describe("change", () => {
  it("is due on a cash overpay and can be given", () => {
    const s = assess(330, parseAmounts({ 1: "20.00" }), METHODS);
    expect(s.overpay).toBe(1670);
    expect(s.changeOk).toBe(true);
    expect(s.changeByPayId.get(1)).toBe(1670);
  });

  // ⚠ CARD CANNOT GIVE CHANGE. Overpaying £20 on a £3.30 basket by card is not a sale with change,
  // it is a refusal — the same rule TenderLoop mutation-checks in the opposite direction.
  it("cannot be given from a card, so the sale is refused with a reason", () => {
    const p = parseAmounts({ 2: "20.00" });
    const s = assess(330, p, METHODS);
    expect(s.overpay).toBe(1670);
    expect(s.changeOk).toBe(false);
    expect(refusalReason(s, p, gbp)).toBe("£16.70 over, and only cash can give change back.");
  });

  it("comes only out of the cash half of a mixed overpay", () => {
    // £5 basket, £2 card + £5 cash = £7 paid, £2 over — all of it from the cash.
    const s = assess(500, parseAmounts({ 1: "5.00", 2: "2.00" }), METHODS);
    expect(s.overpay).toBe(200);
    expect(s.changeablePaid).toBe(500);
    expect(s.changeOk).toBe(true);
    expect(s.changeByPayId.get(1)).toBe(200);
    expect(s.changeByPayId.has(2)).toBe(false);
  });

  // ⚠⚠ THE PENNY. Σchange must equal the overpay exactly or the v1 pipeline rejects the sale for
  // breaking `net tender == gross`. Rounding each share independently is how a three-way split
  // loses one.
  it("sums EXACTLY to the overpay across two cash methods, remainder and all", () => {
    const CASH2 = { id: 3, isChangeable: true };
    const methods = [CASH, CARD, CASH2];

    // ⚠⚠ THESE NUMBERS ARE CHOSEN, NOT ARBITRARY, and the first attempt at this test was USELESS.
    // £9.99 owed, £5.00 + £5.00 cash → 1p over across two EQUAL tenders. Each proportional share is
    // exactly 0.5p, and `Math.round(0.5)` rounds UP in JavaScript — so rounding the shares
    // independently produces 1p + 1p = 2p of change for a 1p overpay, and the sale is rejected at
    // ingest for breaking `net tender == gross`.
    //
    // ⚠ My first version used 3.33 + 3.34 against £6.66, where the shares are 0.499 and 0.5007 —
    // they round to 0 and 1 either way, so the test passed with the remainder rule DELETED. It was
    // pinning nothing. Unequal tenders cannot catch this bug; equal ones always can.
    const parsed = parseAmounts({ 1: "5.00", 3: "5.00" });
    const s = assess(999, parsed, methods);
    expect(s.overpay).toBe(1);

    const total = [...s.changeByPayId.values()].reduce((a, b) => a + b, 0);
    expect(total).toBe(s.overpay);

    // And it is ONE of them that absorbs it, not both.
    expect([...s.changeByPayId.values()].sort()).toEqual([0, 1]);
  });

  it("is empty when nothing is over", () => {
    const s = assess(1000, parseAmounts({ 1: "10.00" }), METHODS);
    expect(s.overpay).toBe(0);
    expect(s.changeByPayId.size).toBe(0);
  });

  it("apportions nothing when the overpay is on an unchangeable method only", () => {
    const parsed = parseAmounts({ 2: "20.00" });
    expect(apportionChange(parsed, METHODS, 1670, 0).size).toBe(0);
  });
});

describe("refunds", () => {
  // ⚠ A refund gives money BACK, so nothing here gives change on top of it — both would hand over
  // the same money twice. Identical rule to `TillTenders.Offered(refundOnly: true)`.
  it("never produce change, however much is handed back", () => {
    const s = assess(-1000, parseAmounts({ 1: "10.00" }), METHODS);
    expect(s.refunding).toBe(true);
    expect(s.owed).toBe(1000);
    expect(s.overpay).toBe(0);
    expect(s.changeByPayId.size).toBe(0);
  });

  it("refuse handing back MORE than is owed", () => {
    const p = parseAmounts({ 1: "12.00" });
    const s = assess(-1000, p, METHODS);
    expect(s.overRefund).toBe(true);
    expect(refusalReason(s, p, gbp)).toBe("That's more than the refund owes — hand back exactly £10.00.");
  });

  it("settle when the amount matches exactly", () => {
    const p = parseAmounts({ 1: "10.00" });
    expect(refusalReason(assess(-1000, p, METHODS), p, gbp)).toBeNull();
  });
});

describe("the rest button", () => {
  // ⚠ Reported 2026-08-07: using the bare remainder made "rest" toggle 0.00 ↔ full on an
  // already-filled row, and left an overpaid row untouched.
  it("recomputes a filled row from the others rather than toggling it", () => {
    // £10 owed, this row holds £4, another holds £2 → paid £6, own £4 → rest is £8.
    expect(restFor(1000, 600, 400)).toBe(800);
  });

  it("covers the whole basket when nothing else is entered", () => {
    expect(restFor(1000, 0, 0)).toBe(1000);
  });

  it("never goes negative when the others already overpay", () => {
    expect(restFor(1000, 1200, 0)).toBe(0);
  });

  // ⚠ A gift card's "rest" is capped at what the card holds — filling the full remainder would just
  // be refused.
  it("is capped when the method holds a balance", () => {
    expect(restFor(1000, 0, 0, 250)).toBe(250);
  });
});

describe("an unparseable set", () => {
  it("refuses with the reason, and reports nothing paid", () => {
    const p = parseAmounts({ 1: "££" });
    const s = assess(1000, p, METHODS);
    expect(s.paid).toBe(0);
    expect(s.remaining).toBe(1000);
    expect(refusalReason(s, p, gbp)).toBe("One of those amounts isn't a number.");
  });
});
