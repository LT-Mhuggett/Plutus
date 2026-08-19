/**
 * Member numbers — what a card holds, and what a human typing one is allowed to leave out. WP-T2,
 * 2026-08-19.
 *
 * ⚠⚠ C2 TWIN of `SharedKernel.MemberNumbers`. Before this file the web till had **no member-number
 * logic at all** — only the `MEMBER_CARD` shape test in its scan handler — so a typed bare number was
 * answered with *"Nothing found"*. Matt, testing 1.101.0: *"How do I get the credit though? I have
 * people with credit. But there is no way to select them?"* A missing `C` prefix was being reported as
 * nothing found.
 *
 * ⚠⚠ **THE CHECK CHARACTER IS THE WHOLE POINT, so this is a real port and not a regex.** `000482P`
 * verifies; `000482Q` does not, and must be refused rather than looked up — otherwise a mistyped digit
 * silently attaches a DIFFERENT member, with their discount and their store credit, to somebody else's
 * sale. `memberNumbers.test.ts` runs the same vectors as `MemberNumberTests` in .NET.
 *
 * ⚠ **DELIBERATELY LOOSER THAN THE SCAN TEST, AND ONLY REACHABLE AFTER A FAILED ITEM LOOKUP.** The
 * strict `MEMBER_CARD` regex in `TillPage.tsx` demands the `C` prefix so a six-digit PRODUCT barcode
 * can never be hijacked into a customer lookup. This one accepts a bare `482`, because somebody is
 * reading a card down the phone — and it runs only once the catalogue has already failed to match, so
 * the collision the strict test guards against is impossible by construction.
 */

/** ⚠ MUST MATCH `Crockford32.Alphabet`. The check character is an index into it. */
const ALPHABET = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

/** ⚠ MUST MATCH `MemberNumbers.Prefix` / `.SequenceDigits`. */
const PREFIX = "C";
const SEQUENCE_DIGITS = 6;

/** ⚠ MUST MATCH `MemberNumbers.Weights`. Reordering these renumbers every card ever printed. */
const WEIGHTS = [7, 3, 1];

/**
 * Crockford folding, so a hand-typed card number still resolves.
 *
 * ⚠ O→0 and I/L→1 are FOLDED, NOT REFUSED, on purpose: a human reading `000482P` off a card down a
 * phone says "oh oh oh four eight two", and refusing the letter O would fail the exact case this
 * exists to serve. ⚠ C2 twin of `Crockford32.Normalise`.
 */
function normalise(code: string): string {
  return (code ?? "").trim().toUpperCase()
    .replace(/I/g, "1").replace(/L/g, "1").replace(/O/g, "0").replace(/U/g, "V");
}

/**
 * The check character for a digit string.
 *
 * ⚠ Throws on anything that is not digits, matching the .NET side — a caller that has not checked
 * should fail loudly here rather than get a character computed from rubbish.
 */
export function checkChar(digits: string): string {
  if (!digits || !/^[0-9]+$/.test(digits)) throw new Error("digits required");

  let sum = 0;
  for (let i = 0; i < digits.length; i++) {
    sum += Number(digits[i]) * WEIGHTS[i % WEIGHTS.length];
  }
  return ALPHABET[sum % ALPHABET.length];
}

/**
 * Turn anything a human or scanner might supply into the canonical member number, or null when it is
 * not one.
 *
 * Accepts the bare number (`000482P`), the barcode payload (`C000482P`), the sequence without its
 * check character (`000482`, `482`), and any of those in lower case or with spaces/hyphens.
 *
 * ⚠⚠ **LENGTH IS TESTED BEFORE "all digits", and that order is load-bearing.** A check character is
 * often itself a digit — sequence 1 canonicalises to `0000011` — so a full member number can be
 * entirely numeric and must not be mistaken for a bare sequence. Swap these two branches and every
 * such card resolves to the wrong member.
 *
 * ⚠ A 7-character candidate whose check character does not verify is REJECTED. That is what a check
 * character is for.
 */
export function tryCanonicalise(input: string | null | undefined): string | null {
  if (input === null || input === undefined || input.trim() === "") return null;

  const stripped = input.replace(/[\s-]/g, "");
  let s = normalise(stripped);

  if (s.length > 1 && s.startsWith(PREFIX)) s = s.slice(PREFIX.length);
  if (s.length === 0) return null;

  if (s.length === SEQUENCE_DIGITS + 1) {
    const body = s.slice(0, -1);
    if (!/^[0-9]+$/.test(body)) return null;
    return checkChar(body) === s[s.length - 1] ? s : null;
  }

  // shorter, all digits → a sequence without its check character ("482" → "000482" + check)
  if (s.length <= SEQUENCE_DIGITS && /^[0-9]+$/.test(s)) {
    const digits = s.padStart(SEQUENCE_DIGITS, "0");
    return digits + checkChar(digits);
  }

  return null;
}

/** Sequence number → member number, e.g. 482 → "000482P". ⚠ C2 twin of `MemberNumbers.Format`. */
export function formatMemberNo(sequence: number): string {
  if (sequence < 0) throw new Error("sequence must not be negative");

  const digits = String(Math.trunc(sequence)).padStart(SEQUENCE_DIGITS, "0");
  return digits + checkChar(digits);
}
