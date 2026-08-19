import { describe, expect, it } from "vitest";
import { checkChar, formatMemberNo, tryCanonicalise } from "./memberNumbers.ts";

/**
 * The TypeScript half of the member-number twin — WP-T2, 2026-08-19.
 *
 * ⚠⚠ **THESE ARE THE SAME VECTORS AS `MemberNumberTests` IN .NET, ON PURPOSE.** The check character is
 * the only thing standing between a mistyped digit and attaching a DIFFERENT member — with their
 * discount and their store credit — to somebody else's sale. A twin executed on one side only is not
 * pinned, and this side had no implementation at all until now.
 *
 * **Add a case here, add it there.**
 */
describe("formatting", () => {
  it("is six digits plus a check character", () => {
    const n = formatMemberNo(482);

    expect(n).toHaveLength(7);
    expect(n.slice(0, 6)).toBe("000482");
    expect(n).toBe("000482P");
  });

  /**
   * ⚠ The alphabet has no I, L, O or U — the four characters a human confuses with 1, 1, 0 and V. A
   * check character drawn from outside it would be unreadable off a printed card.
   */
  it("never produces an ambiguous character", () => {
    for (let seq = 0; seq < 500; seq++) {
      const c = formatMemberNo(seq).slice(-1);

      expect("ILOU").not.toContain(c);
      expect(c).toMatch(/^[0-9A-Z]$/);
    }
  });
});

describe("canonicalising", () => {
  /**
   * ⚠⚠ THE ORDER-OF-TESTS CASE. Sequence 1 canonicalises to `0000011` — a full member number that is
   * ENTIRELY DIGITS. If length were tested after "all digits", it would be read as a bare sequence and
   * resolve to a different member. This is the vector that catches that.
   */
  it("resolves numbers whose check character is itself a digit", () => {
    const full = formatMemberNo(1);

    expect(full).toBe("0000011");
    expect(tryCanonicalise(full)).toBe(full);
  });

  it("accepts every form a person or scanner supplies", () => {
    const expected = "000482P";

    expect(tryCanonicalise("000482P")).toBe(expected);   // as printed
    expect(tryCanonicalise("C000482P")).toBe(expected);  // the barcode payload
    expect(tryCanonicalise("c000482p")).toBe(expected);  // hand-typed, lower case
    expect(tryCanonicalise("  000482P ")).toBe(expected);
    expect(tryCanonicalise("000-482-P")).toBe(expected);
    expect(tryCanonicalise("000482")).toBe(expected);    // sequence, no check character
    expect(tryCanonicalise("482")).toBe(expected);       // nobody types the leading zeros
  });

  /** ⚠ Read down a phone, "oh oh oh four eight two" — refusing the letter O would fail the very case. */
  it("folds the ambiguous characters to the digits they were meant to be", () => {
    expect(tryCanonicalise("OOO482P")).toBe("000482P");
    expect(tryCanonicalise("O00482P")).toBe("000482P");
  });

  /** ⚠⚠ THE ONE THAT PROTECTS SOMEBODY ELSE'S CREDIT. */
  it("rejects a single mis-keyed digit", () => {
    expect(tryCanonicalise("000483P")).toBeNull();
    expect(tryCanonicalise("000482Z")).toBeNull();
  });

  it("rejects transposed adjacent digits", () => {
    // ⚠ The 7/3/1 weights are what make a transposition change the sum — an unweighted checksum
    // would accept it, and "482" vs "824" is the classic counter mistype.
    const real = formatMemberNo(1234);
    const swapped = `00${"2134"}${real.slice(-1)}`;

    expect(tryCanonicalise(real)).toBe(real);
    expect(tryCanonicalise(swapped)).toBeNull();
  });

  it("is not fooled by things that are not member numbers", () => {
    expect(tryCanonicalise("")).toBeNull();
    expect(tryCanonicalise(null)).toBeNull();
    expect(tryCanonicalise(undefined)).toBeNull();
    expect(tryCanonicalise("Ada Lovelace")).toBeNull();
    expect(tryCanonicalise("ada@example.com")).toBeNull();
    expect(tryCanonicalise("C")).toBeNull();

    // ⚠⚠ A 13-DIGIT PRODUCT EAN MUST NEVER CANONICALISE. This is the collision the whole design
    // guards against — a scanned book barcode attaching a stranger to the sale.
    expect(tryCanonicalise("9780306406157")).toBeNull();
    expect(tryCanonicalise("5012345678900")).toBeNull();
  });

  /**
   * ⚠ THE CEILING IS SIX DIGITS — a million members per tenant — and past it a number formats but
   * cannot be read back. Pinned here as it is in .NET, because the fix is to widen the constant on
   * BOTH sides, never to loosen the parser: accepting arbitrary lengths would make a bare EAN-8
   * canonicalise as a member number about 3% of the time.
   */
  it("cannot read back a sequence past the six-digit ceiling", () => {
    expect(tryCanonicalise(formatMemberNo(1_000_000))).toBeNull();
  });
});

describe("the check character itself", () => {
  it("refuses anything that is not digits", () => {
    expect(() => checkChar("")).toThrow();
    expect(() => checkChar("00048A")).toThrow();
  });
});
