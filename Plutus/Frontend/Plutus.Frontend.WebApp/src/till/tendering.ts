import { parsePence } from "../money.ts";

/**
 * The web till's tendering arithmetic — what is paid, what remains, what change is due, and whether
 * the sale may complete.
 *
 * ⚠⚠ THIS IS A C2 TWIN, AND UNTIL NOW IT WAS THE UNTESTED HALF. The same rules exist in .NET as
 * `Plutus.Client.Core.TenderLoop`, which has 19 mutation-checked tests. This side had **none** —
 * `Plutus.Frontend.WebApp/package.json` defined no test runner at all. Two tills that disagree by a
 * penny on one basket disagree on every VAT return afterwards, and nothing flags it.
 *
 * ⚠ EXTRACTED, NOT REWRITTEN. Every expression below is lifted verbatim out of
 * `CheckoutDialog.tsx` so the behaviour is identical and the component keeps one source of truth.
 * The point is to make the arithmetic REACHABLE by a test, not to improve it — anything that looks
 * like it wants tidying should be changed only with a failing test in front of it.
 *
 * ⚠ THE WEB TILL AND MAUI USE DIFFERENT INTERACTION MODELS AND THAT IS FINE. MAUI asks for one
 * tender at a time and names the remainder ("There is £6.00 left to pay"); this puts every method on
 * screen at once with a live "Remaining" figure. What must NOT differ is the arithmetic: the same
 * basket and the same tenders have to produce the same totals, the same change and the same
 * accept/refuse decision on both.
 */

/** One pay method, as the arithmetic needs it. `isChangeable` is true for cash. */
export interface TenderMethod {
  id: number;
  isChangeable: boolean;
}

export type ParsedAmounts =
  | { valid: false }
  | { valid: true; perMethod: Map<number, number>; paid: number };

/**
 * Read the per-method amount boxes.
 *
 * ⚠ ONE BAD BOX INVALIDATES THE WHOLE SET, deliberately. A basket that is 90% parseable is not 90%
 * payable — completing on a partial read would take a different sum from the one on screen.
 * ⚠ Blank is SKIPPED, not zero: an untouched row is not a £0.00 tender.
 * ⚠ Zero and negative are dropped rather than refused, matching the component: the gate that stops
 * the sale is `remaining === 0`, so a £0 row simply contributes nothing.
 */
export function parseAmounts(amounts: Record<number, string>): ParsedAmounts {
  const perMethod = new Map<number, number>();
  for (const [id, raw] of Object.entries(amounts)) {
    if (!raw.trim()) continue;
    const pence = parsePence(raw);
    if (pence === null) return { valid: false as const };
    if (pence > 0) perMethod.set(Number(id), pence);
  }
  const paid = [...perMethod.values()].reduce((a, b) => a + b, 0);
  return { valid: true as const, perMethod, paid };
}

export interface Settlement {
  /** Absolute value of what the basket owes — a refund is a positive `owed` with `refunding` true. */
  owed: number;
  refunding: boolean;
  paid: number;
  /** Still to take. Never negative — an excess is `overpay`, not a negative remainder. */
  remaining: number;
  /** Taken above what is owed. Always 0 on a refund. */
  overpay: number;
  /** ⚠ A refund handed back MORE than it owes. Not change — an error. */
  overRefund: boolean;
  /** Sum of the tenders that can physically give change back (cash). */
  changeablePaid: number;
  /** Whether the overpay can actually be handed back from the drawer. */
  changeOk: boolean;
  /** Change per pay method, summing EXACTLY to `overpay`. */
  changeByPayId: Map<number, number>;
}

/**
 * Assess a set of amounts against a basket.
 *
 * ⚠ `totalPence` NEGATIVE MEANS REFUND, matching the component and the wire.
 * ⚠ NO CHANGE ON A REFUND. You hand back exactly what is owed, so an excess is a mistake rather
 * than change — the same rule as `TenderLoop`, where a refund gives neither change nor cashback.
 */
export function assess(
  totalPence: number,
  parsed: ParsedAmounts,
  methods: readonly TenderMethod[],
): Settlement {
  const refunding = totalPence < 0;
  const owed = Math.abs(totalPence);

  const paid = parsed.valid ? parsed.paid : 0;
  const remaining = Math.max(0, owed - paid);
  const overpay = refunding ? 0 : Math.max(0, paid - owed);
  const overRefund = refunding && paid > owed;

  const changeablePaid = parsed.valid
    ? [...parsed.perMethod.entries()]
        .filter(([id]) => methods.find((m) => m.id === id)?.isChangeable)
        .reduce((a, [, v]) => a + v, 0)
    : 0;

  const changeOk = overpay === 0 || overpay <= changeablePaid;

  return {
    owed, refunding, paid, remaining, overpay, overRefund, changeablePaid, changeOk,
    changeByPayId: apportionChange(parsed, methods, overpay, changeablePaid),
  };
}

/**
 * Split the change across the methods that can give it.
 *
 * ⚠⚠ THE LAST CHANGEABLE METHOD ABSORBS THE ROUNDING REMAINDER, and that is load-bearing: Σchange
 * must equal the overpay TO THE PENNY, because the v1 pipeline enforces `net tender == gross` and
 * rejects the sale otherwise. Rounding each share independently loses or invents a penny on three-way
 * splits, and the sale is refused at ingest with nothing on screen explaining why.
 */
export function apportionChange(
  parsed: ParsedAmounts,
  methods: readonly TenderMethod[],
  overpay: number,
  changeablePaid: number,
): Map<number, number> {
  const changeByPayId = new Map<number, number>();
  if (!parsed.valid || overpay <= 0 || changeablePaid <= 0) return changeByPayId;

  const changeable = [...parsed.perMethod.entries()]
    .filter(([id]) => methods.find((m) => m.id === id)?.isChangeable);

  let allocated = 0;
  changeable.forEach(([payId, pence], i) => {
    const change =
      i === changeable.length - 1 ? overpay - allocated : Math.round((overpay * pence) / changeablePaid);
    allocated += change;
    changeByPayId.set(payId, change);
  });
  return changeByPayId;
}

/**
 * What the "rest" button should put in one row.
 *
 * ⚠ IT IGNORES WHAT THIS ROW ALREADY HOLDS. Using the bare remainder made "rest" toggle between
 * 0.00 and the full amount whenever the row was already filled (reported 2026-08-07), and left an
 * overpaid row untouched. `own` is subtracted out so the row is recomputed from the others.
 */
export function restFor(owed: number, paid: number, own: number, capTo?: number): number {
  const needed = Math.max(0, owed - (paid - own));
  return capTo === undefined ? needed : Math.min(needed, capTo);
}

/**
 * Why the sale cannot complete yet — or null when it can.
 *
 * ⚠ IT RETURNS A SENTENCE, and that is a real gap this closes. The web till only ever DISABLED the
 * Complete button, saying nothing; MAUI names its refusal (`TenderRefusal` — zero, wrong direction,
 * overpaid without change) and the operator is told. A greyed-out button with no reason is the same
 * failure as "Something went wrong": the screen has decided something and not said what.
 */
export function refusalReason(
  s: Settlement,
  parsed: ParsedAmounts,
  gbp: (pence: number) => string,
): string | null {
  if (!parsed.valid) return "One of those amounts isn't a number.";
  if (s.remaining > 0) return `${gbp(s.remaining)} still to pay.`;
  if (s.overRefund) return `That's more than the refund owes — hand back exactly ${gbp(s.owed)}.`;
  if (!s.changeOk) return `${gbp(s.overpay)} over, and only cash can give change back.`;
  return null;
}
