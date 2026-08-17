import { describe, expect, it } from "vitest";
import {
  SURCHARGE_ITEM_ID, SURCHARGE_TAX_ID, cardIsTendered, feePence, hasSurcharge, pairFor, surchargeLine,
} from "./surcharge.ts";
import { isCardSurcharge, type BasketLine } from "./basket.ts";

/**
 * W-P7 — the card surcharge.
 *
 * ⚠⚠ THE SAME VECTORS AS `tests/Plutus.Tests.Unit/CardSurchargeVatTests.cs`, on purpose. This is the
 * TypeScript half of a C2 twin: the numbers below are the .NET test's numbers, so the pair cannot
 * drift without one of them going red. Two tills that disagree by a penny on one basket disagree on
 * every VAT return afterwards, and nothing else flags it.
 *
 * ⚠ THE FAILURE MODE IS A PLAUSIBLE-LOOKING CONSTANT. Hardcoding 20% on the fee is what most systems
 * do, it reconciles perfectly — every invariant passes — and it is wrong on every basket that is not
 * purely standard-rated. Only these tests stand between that shortcut and a VAT return.
 */

const item = (idOne: string, pricePence: number, exPricePence: number) => ({
  idOne, name: idOne, brand: "-", desc: "", cost: 0,
  exPrice: exPricePence / 100, price: pricePence / 100, taxId: 1, catId: "",
});

let nextKey = 1;
const line = (pricePence: number, exPricePence: number, over: Partial<BasketLine> = {}): BasketLine => ({
  key: nextKey++,
  item: item(`ITEM-${nextKey}`, pricePence, exPricePence),
  quantity: 1,
  pricePence,
  exPricePence,
  adjusted: false,
  ...over,
});

describe("pairFor — the fee's VAT follows the basket", () => {
  it("carries the basket's 20% on a standard-rated basket", () => {
    // £120 gross / £100 ex — pure 20%. A 50p fee → ex 42p (41.67 rounded), VAT 8p.
    expect(pairFor(50, 12000, 10000)).toEqual({ incPence: 50, exPence: 42 });
  });

  it("carries NO VAT on a zero-rated basket", () => {
    // ⚠ THE ONE A HARDCODED RATE GETS WRONG. A fee on children's books or most food is itself
    // zero-rated; charging VAT on it puts output tax on a return that HMRC says is not due.
    expect(pairFor(50, 10000, 10000)).toEqual({ incPence: 50, exPence: 50 });
  });

  it("is apportioned by value on a mixed basket", () => {
    // £120 standard (ex £100) + £120 zero-rated (ex £120) = gross 240, ex 220.
    // 120 × 220/240 = 110 → VAT 10p, not the 20p a hardcoded rate would take.
    expect(pairFor(120, 24000, 22000)).toEqual({ incPence: 120, exPence: 110 });
  });

  it("rounds away from zero, once", () => {
    // ⚠ 3 × 10000 ÷ 12000 is exactly 2.5, and away-from-zero says 3. Banker's rounding says 2.
    expect(pairFor(3, 12000, 10000).exPence).toBe(3);
    expect(pairFor(50, 12000, 10000).exPence).toBe(42);   // 41.67 — truncation would say 41
  });

  it("multiplies before dividing", () => {
    // ⚠⚠ THE .NET VECTOR DOES NOT PIN THIS SIDE, and that is worth knowing about every twin verified
    // by copying the other's tests. `CardSurchargeVatTests` uses 3 × 10000 ÷ 12000 = 2.5 to force
    // products-first, because `decimal` computes 10000m/12000m as 0.8333…3 and 3 × that is 2.4999…,
    // which rounds DOWN. A double rounds 10000/12000 UP, to 0.8333333333333334, so 3 × that is
    // 2.5000000000000004 and the ratio-first mutant PASSES that vector here. Mutation-tested
    // 2026-08-17: it survived, and this test is why the pair is pinned at all.
    //
    // 45 × 70 ÷ 100 is exactly 31.5 → 32. Ratio-first: 70/100 is not representable, 45 × 0.7 is
    // 31.499999999999996, and the fee's ex comes out a penny light.
    expect(pairFor(45, 100, 70).exPence).toBe(32);

    // ⚠ NO REALISTIC BASKET REACHES IT — searched exhaustively over gross 50p–£50, flat fee 15–60p
    // and every ex/gross ratio between pure-20% and pure-zero-rated: zero divergences. So this is not
    // protecting today's tenants from a penny, it is protecting the RULE, which is the thing the two
    // tills have to agree on. Products-first IS the .NET decimal answer for every input in the money
    // range (the quotient is exact until one rounding); ratio-first is only accidentally the same one.
  });

  it("makes a zero pair from a zero fee", () => {
    expect(pairFor(0, 12000, 10000)).toEqual({ incPence: 0, exPence: 0 });
  });

  it("refuses a negative fee", () => {
    // ⚠ A negative fee is a discount wearing the wrong hat, and the two must not blur — a return
    // drops a discount and keeps a fee, so mixing them corrupts refunds.
    expect(() => pairFor(-50, 12000, 10000)).toThrow();
  });

  it.each([0, -500])("refuses a basket with no positive sale value (%i)", (gross) => {
    expect(() => pairFor(50, gross, 0)).toThrow();
  });

  it.each([-1, 12001])("refuses an impossible ex total (%i)", (ex) => {
    expect(() => pairFor(50, 12000, ex)).toThrow();
  });
});

describe("feePence — percent plus flat", () => {
  it("is percent plus flat", () => {
    // 1.69% + 20p on £10 = 17p + 20p = 37p.
    expect(feePence(169, 20, 1000)).toBe(37);
  });

  it("charges nothing on a zero setting", () => {
    expect(feePence(0, 0, 10000)).toBe(0);
  });

  it("rounds the percent half away from zero", () => {
    // ⚠ 25bp of £1.00 is exactly 2.5p and must round to 3 — every shared money rule's convention.
    expect(feePence(25, 0, 1000)).toBe(3);
  });

  it.each([0, -500])("charges nothing when there is no positive sale value (%i)", (gross) => {
    // ⚠ Zero rather than a throw: "don't surcharge this" is an answer, not an error.
    expect(feePence(169, 20, gross)).toBe(0);
  });

  it("refuses a negative setting", () => {
    expect(() => feePence(-1, 0, 1000)).toThrow();
    expect(() => feePence(0, -1, 1000)).toThrow();
  });
});

describe("surchargeLine — which lines the fee rides on", () => {
  it("prices the line from the sale lines, after discounts", () => {
    // £120 gross / £100 ex standard-rated, 1.69% + 20p → 203 + 20 = 223p, ex 186 (185.83).
    const fee = surchargeLine([line(12000, 10000)], 169, 20, 99);

    expect(fee).not.toBeNull();
    expect(fee!.pricePence).toBe(223);
    expect(fee!.exPricePence).toBe(186);
    expect(fee!.quantity).toBe(1);
    expect(fee!.item.idOne).toBe(SURCHARGE_ITEM_ID);
  });

  it("rides on the DISCOUNTED value, not the list price", () => {
    // ⚠ The fee is a percentage of what is actually being paid. Charging it on the pre-discount
    // figure would quietly claw back part of the discount the operator just gave.
    const discounted = line(10000, 10000, {
      discount: { discountId: 1, name: "Half price", type: 1, amount: 0.5 },
    });

    // 50% off £100 = £50 → 1000bp of 5000 = 500p. On the undiscounted basket it would be 1000p.
    expect(surchargeLine([discounted], 1000, 0, 1)!.pricePence).toBe(500);
  });

  it("carries no band a reader could mistake for the fee's VAT treatment", () => {
    // ⚠⚠ The provisioned item sits on the ZERO band server-side and that band must never tax this
    // line. A taxId no published band claims makes `vatBandForTaxId` return null, so nothing
    // travels and the server snaps the band from the declared rate — what MAUI does with a null
    // VatBandKey. A stated "zero" beside a 1905bp rate reads as zero-rated output tax.
    expect(surchargeLine([line(12000, 10000)], 169, 20, 1)!.item.taxId).toBe(SURCHARGE_TAX_ID);
  });

  it("charges nothing when the tenant has no surcharge set", () => {
    expect(surchargeLine([line(12000, 10000)], 0, 0, 1)).toBeNull();
  });

  it("charges nothing on a refund-only basket", () => {
    // ⚠ No sale line, nothing for the fee to follow — and money is going OUT.
    const ret = line(12000, 10000, { isReturn: true, originSaleId: "abc" });
    expect(surchargeLine([ret], 169, 20, 1)).toBeNull();
  });

  it("ignores returns when pricing the fee", () => {
    // ⚠ A refund attracts no fee, so the returned line must not be in the base — netting it off
    // would under-charge, and on a big return could flip the base negative and throw.
    const sale = line(12000, 10000);
    const ret = line(6000, 5000, { isReturn: true, originSaleId: "abc" });

    expect(surchargeLine([sale, ret], 1000, 0, 1)!.pricePence)
      .toBe(surchargeLine([sale], 1000, 0, 1)!.pricePence);
  });

  it("still charges on a mixed basket that nets negative", () => {
    // ⚠ ONE SALE LINE IS ENOUGH — the twin of MAUI's `refundOnly = !Basket.Any(item && !IsReturn)`.
    // A shared quirk, deliberately kept shared: inventing a third behaviour here is the divergence.
    const sale = line(1000, 833);
    const ret = line(50000, 41667, { isReturn: true, originSaleId: "abc" });

    expect(surchargeLine([sale, ret], 1000, 0, 1)!.pricePence).toBe(100);
  });

  it("is applied ONCE — a second call over the same basket adds nothing", () => {
    // ⚠ A split across two cards must not charge the flat half twice.
    const basket = [line(12000, 10000)];
    const fee = surchargeLine(basket, 169, 20, 99)!;

    expect(hasSurcharge(basket)).toBe(false);
    expect(hasSurcharge([...basket, fee])).toBe(true);
    expect(surchargeLine([...basket, fee], 169, 20, 100)).toBeNull();
  });

  it("never rides on itself", () => {
    // Belt and braces on the same rule: were the guard removed, the fee would compound.
    const basket = [line(12000, 10000)];
    const fee = surchargeLine(basket, 169, 20, 99)!;
    expect(surchargeLine([...basket, fee], 169, 20, 100)).toBeNull();
  });
});

describe("the reducer's own exclusion", () => {
  it("recognises the fee line the pricing rule builds", () => {
    // ⚠⚠ THIS TEST IS THE PIN. `basket.ts` spells the id as a literal (importing `surcharge.ts` back
    // would be a module cycle), so nothing but this stops the two spellings drifting — and if they
    // did, a member's loyalty tier would quietly discount the card fee: the shop would charge less
    // than the acquirer charges it, on every card sale, and every figure would still add up.
    const fee = surchargeLine([line(12000, 10000)], 169, 20, 1)!;
    expect(isCardSurcharge(fee)).toBe(true);
    expect(isCardSurcharge(line(500, 417))).toBe(false);
  });
});

describe("cardIsTendered", () => {
  it("is true when a card row holds money", () => {
    expect(cardIsTendered([{ tenderType: 0, pence: 500 }, { tenderType: 1, pence: 100 }])).toBe(true);
  });

  it("is false on a cash-only sale", () => {
    expect(cardIsTendered([{ tenderType: 0, pence: 500 }])).toBe(false);
  });

  it("is false when the card row is empty", () => {
    // ⚠ An untouched row is not a card payment — the fee must not appear before anybody chooses.
    expect(cardIsTendered([{ tenderType: 1, pence: 0 }])).toBe(false);
  });

  it("does not treat a GIFT card as a card", () => {
    // ⚠ Gift card is tender 4. `tenderTypeFor` checks "gift" before "credit" for the same reason,
    // and a redemption against a balance is not an acquirer transaction — there is no cost to pass on.
    expect(cardIsTendered([{ tenderType: 4, pence: 500 }])).toBe(false);
  });

  it("does not treat store credit as a card", () => {
    expect(cardIsTendered([{ tenderType: 3, pence: 500 }])).toBe(false);
  });
});
