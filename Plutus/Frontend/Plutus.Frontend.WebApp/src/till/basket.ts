import { useEffect, useReducer } from "react";
import type { Discount, Item } from "../api.ts";
import { toPence } from "../money.ts";
import { exFromInc } from "./priceAdjust.ts";
import { discountPence, type ScheduledDiscount } from "./scheduledDiscounts.ts";
import { autoDiscountForLine, type MemberStanding } from "./autoDiscounts.ts";

/**
 * The id an operator's own typed discount carries (D8) — it has no catalogue row.
 *
 * ⚠⚠ NEGATIVE ON PURPOSE. Checkout only puts a discount on `LineMeta.discounts[]` when its id is
 * POSITIVE, because that array projects into legacy `Transaction_Discount` rows keyed on a real
 * `DiscountId`. One rule ("a real catalogue discount has a positive id") now covers both sentinels:
 * the member's 0 and this. An invented positive id would FK-fail the projection of every sale that
 * carried one.
 */
export const AD_HOC_DISCOUNT_ID = -1;

export interface LineDiscount {
  discountId: number;
  name: string;
  /** 0 = fixed pence off per unit; else fraction (0.10 = 10%) */
  type: number;
  amount: number;
  /** WHY this discount was given — binding default 22(c), Matt 2026-08-13: "All discounts need to be
   *  tracked — till, logged-in employee and reason."
   *
   *  ⚠ NOT THE SAME AS `name`. The name is the CATEGORY the operator picked off the list ("Staff
   *  discount"); the reason is why this basket got one. A report grouped by name answers "how much
   *  staff discount did we give", never "why was this £40 taken off".
   *
   *  ⚠ Mandatory for an operator-applied discount — the dialog will not apply one without it. Set
   *  automatically for the members' auto-discount, where the tier IS the reason and no operator
   *  decided anything.
   *
   *  ⚠ Optional in the TYPE only so a basket parked before 2026-08-14 still deserialises. A recalled
   *  basket without one sends no authority rather than a blank one — see `checkout` in api.ts. */
  reason?: string;

  /** W-P3: the SUPERVISOR who authorised a discount over the operator's own ceiling.
   *
   *  ⚠⚠ ABSENT means "no step-up was required" — it must NEVER be filled with the requester. That
   *  would record a self-approval that never happened, and the shared rule (`DiscountAudit`) refuses
   *  exactly that. Before 2026-08-17 the web till had no ceiling at all, so absent was true of every
   *  discount it had ever taken. */
  authorisedBy?: string;

  /** ⚠⚠ THE TILL PUT THIS HERE, NOT THE OPERATOR — and telling the two apart is load-bearing.
   *
   *  The auto-discount resolver re-runs on every basket change. It must skip a line the OPERATOR has
   *  discounted (no stacking) while being free to REPLACE its own earlier answer — otherwise scanning
   *  a second item would find every line "already discounted" and the automatic discount would
   *  silently vanish from the basket.
   *
   *  ⚠ The member discount could be recognised by its sentinel id alone; a scheduled rule cannot,
   *  because its id is a REAL catalogue id and an operator can pick that very same discount by hand
   *  off the till's list. So the distinction has to be recorded rather than inferred.
   *
   *  ⚠ Optional in the TYPE so a basket parked before 2026-08-20 still deserialises — an old parked
   *  basket's discounts read as the operator's, which is the safe direction: they are preserved
   *  rather than recomputed. */
  auto?: boolean;
}

export interface BasketLine {
  key: number;
  item: Item;
  quantity: number;
  /** unit prices in pence — start from the item, mutate on adjust */
  pricePence: number;
  exPricePence: number;
  adjusted: boolean;
  discount?: LineDiscount;
  isReturn?: boolean;
  originSaleId?: string;
  /** FE7: this line SELLS a gift card, and carries the code being loaded. Its price is the amount
   *  loaded, its VAT is zero (the activation item sits on a zero-rate band), and it is excluded from
   *  every discount — knocking 10% off a £20 card would hand out £20 of goods for £18. */
  giftCardCode?: string;
  /** The barcode actually SCANNED, when the item has more than one and it was not the item's own
   *  (multi-barcode, 2026-08-20).
   *
   *  ⚠⚠ A SNAPSHOT, NEVER AN IDENTITY. `item.idOne` is canonical and is what pricing, line merging,
   *  the deterministic item GUID and the sale line all key on. This exists so that when a supplier's
   *  barcode migration goes wrong somebody can ask which code the tills actually read — the question
   *  nothing else could answer.
   *
   *  ⚠ Absent on almost every line, and omitted from the checkout payload when it equals `idOne`, so
   *  an ordinary sale's metadata is byte-identical to what it was before this existed. */
  scannedBarcode?: string;
  /** ⚠⚠ THE OPERATOR TOOK AN AUTOMATIC DISCOUNT OFF THIS LINE, AND IT MUST STAY OFF.
   *
   *  Without this the promise "you can always charge full price" lasts until the next scan: clearing
   *  the discount leaves the line bare, the resolver re-runs on the very next basket change, finds an
   *  eligible line and puts it straight back. The operator would watch it reappear and have no way to
   *  stop it.
   *
   *  ⚠ Per LINE, not per basket, and it is cleared by removing the line — which is the operator's own
   *  "actually, ring it again" gesture. */
  autoWaived?: boolean;
}

export interface BasketState {
  lines: BasketLine[];
  nextKey: number;
}

type Action =
  | { type: "add"; item: Item; quantity?: number; scannedBarcode?: string }
  | { type: "addReturn"; item: Item; quantity: number; unitPricePence: number; unitExPricePence: number; originSaleId: string }
  | { type: "addGiftCard"; item: Item; code: string; amountPence: number; exAmountPence: number }
  | { type: "quantity"; key: number; delta: number }
  /**
   * ⚠ An ABSOLUTE quantity, from the operator typing into the box (2026-08-25). Kept separate from
   * `quantity` rather than folded into it: a delta and a total are different intentions, and
   * computing `delta = typed - current` at the call site would race a basket that changed underneath
   * the edit. ⚠ 0 removes the line, exactly as a delta down to 0 already does — see `commitQuantity`.
   */
  | { type: "setQuantity"; key: number; quantity: number }
  | { type: "adjust"; key: number; pricePence: number }
  | { type: "applyDiscount"; discount: Discount; keys: number[]; reason: string; authorisedBy?: string | null }
  | { type: "clearDiscount"; key: number }
  /** D8: a figure the operator typed, with no catalogue row behind it. */
  | { type: "applyAdHocDiscount"; kind: number; amount: number; label: string; keys: number[]; reason: string; authorisedBy?: string | null }
  /** Recompute every AUTOMATIC discount — the member's tier and any live scheduled rule. */
  | { type: "autoDiscounts"; member: MemberStanding; rules: ScheduledDiscount[]; nowMs: number }
  | { type: "remove"; key: number }
  | { type: "move"; key: number; direction: -1 | 1 }
  | { type: "restore"; state: BasketState }
  | { type: "clear" };

/**
 * Is this line the card surcharge? W-P7 — the twin of `MemberDiscountBasket`'s own exclusion.
 *
 * ⚠⚠ A FEE IS NOT SHOPPING. Discounting a card surcharge means the member is charged less than the
 * acquirer charges the shop for their transaction, so a loyalty tier would eat into the cost the fee
 * exists to pass on — and on a large basket at 1.69% that is real money going the wrong way.
 *
 * ⚠ It cannot be reached today: `till/surcharge.ts` builds the fee AT CHECKOUT and never puts it in
 * basket state, so no discount action can see it. The guard is here because the reducer is the one
 * place that can be sure — and because the MAUI till, which DOES put the line in its basket, needs
 * exactly this rule. A reader comparing the two must not find it on one side only.
 *
 * ⚠ The literal, not `surcharge.ts`'s constant, on purpose: `surcharge.ts` already imports
 * `basketTotals` from here, and importing back would be a module cycle. Exported so
 * `surcharge.test.ts` can pin the two spellings together — the same discipline every other twin in
 * this codebase uses: a test, not an import.
 */
export const isCardSurcharge = (l: BasketLine): boolean => l.item.idOne?.toUpperCase() === "CARD-SURCHARGE";

/**
 * ⚠ EXPORTED SO A TEST CAN DRIVE THE REAL REDUCER, not a restatement of it.
 *
 * C2 records the weakness this closes: `basketMerge.test.ts` tests a COPY of the merge predicate
 * because the rule lives inside a `find(…)` in `addLine` and is not exported — "if the reducer's
 * condition changes and the test does not, these vectors go on passing while the tills drift again".
 * The auto-discount rules below (waiving, and not eating the resolver's own output) are exactly the
 * kind that would pass a restated test while being wrong here, so the reducer itself is under test.
 */
export function reduceBasket(state: BasketState, action: Action): BasketState {
  return reduce(state, action);
}

function reduce(state: BasketState, action: Action): BasketState {
  switch (action.type) {
    case "add": {
      const qty = action.quantity ?? 1;
      // scanning the same (plain) item again bumps its quantity
      const existing = state.lines.find((l) => l.item.idOne === action.item.idOne && !l.adjusted && !l.discount && !l.isReturn);
      if (existing)
        return {
          ...state,
          lines: state.lines.map((l) => (l === existing ? { ...l, quantity: l.quantity + qty } : l)),
        };
      const line: BasketLine = {
        key: state.nextKey,
        item: action.item,
        quantity: qty,
        pricePence: toPence(action.item.price),
        exPricePence: toPence(action.item.exPrice),
        adjusted: false,
        // ⚠ Multi-barcode: recorded only when an ADDITIONAL barcode was scanned. Stored on the NEW
        // line only — a unit merged into an existing line above keeps whatever that line recorded,
        // because the snapshot belongs to the scan that created the row.
        scannedBarcode:
          action.scannedBarcode && action.scannedBarcode !== action.item.idOne
            ? action.scannedBarcode
            : undefined,
      };
      return { lines: [...state.lines, line], nextKey: state.nextKey + 1 };
    }
    case "addReturn": {
      const line: BasketLine = {
        key: state.nextKey,
        item: action.item,
        quantity: action.quantity,
        pricePence: action.unitPricePence,
        exPricePence: action.unitExPricePence,
        adjusted: false,
        isReturn: true,
        originSaleId: action.originSaleId,
      };
      return { lines: [...state.lines, line], nextKey: state.nextKey + 1 };
    }
    // FE7: selling a gift card. The ex price is DECIDED BY THE TENANT'S VAT TREATMENT, so the caller
    // supplies it: multi-purpose → exAmount == amount (zero VAT now, VAT when the card is spent);
    // single-purpose → exAmount = amount/1.2 (VAT due at this sale, HMRC single-purpose voucher).
    // Quantity is always 1: each card is its own code, so two cards are two lines.
    case "addGiftCard": {
      const line: BasketLine = {
        key: state.nextKey,
        item: action.item,
        quantity: 1,
        pricePence: action.amountPence,
        exPricePence: action.exAmountPence,
        adjusted: false,
        giftCardCode: action.code,
      };
      return { lines: [...state.lines, line], nextKey: state.nextKey + 1 };
    }
    case "quantity":
      return {
        ...state,
        lines: state.lines
          .map((l) => (l.key === action.key ? { ...l, quantity: l.quantity + action.delta } : l))
          .filter((l) => l.quantity > 0),
      };
    /**
     * ⚠ Same `> 0` filter as the delta case, and that is the point: one place decides that a line
     * with no quantity is not a line, so typing 0 and pressing − at 1 cannot diverge.
     * ⚠ A gift-card line is never routed here — its quantity is fixed at 1 because a card is one
     * specific code, and "2 ×" would charge twice and load once (FE7). The UI does not offer the box.
     */
    case "setQuantity":
      return {
        ...state,
        lines: state.lines
          .map((l) => (l.key === action.key ? { ...l, quantity: action.quantity } : l))
          .filter((l) => l.quantity > 0),
      };
    case "adjust":
      return {
        ...state,
        lines: state.lines.map((l) => {
          if (l.key !== action.key) return l;
          // ⚠ The ex half is DERIVED from the CATALOGUE pair, keeping the item's VAT proportion. The
          // rule moved to `priceAdjust.ts` as a C2 twin of `SharedKernel.PriceAdjust`, because MAUI
          // asked for ex AND inc separately and a mistyped pair became the sale's declared VAT rate.
          // ⚠ It also MULTIPLIES BEFORE DIVIDING, which the inline `ratio` form below did not: a
          // double cannot hold 70/100, so `45 × 0.7` was 31.499… and rounded to 31 where the exact
          // answer is 31.5 → 32. Sub-penny, on manually overridden prices only — but it is the
          // arithmetic the .NET twin performs, and two tills must not round a price differently.
          // (The "form below" is gone — this comment records what it did.)
          return {
            ...l,
            pricePence: action.pricePence,
            exPricePence: exFromInc(action.pricePence, {
              incPence: toPence(l.item.price),
              exPence: toPence(l.item.exPrice),
            }),
            adjusted: true,
          };
        }),
      };
    case "applyDiscount":
      return {
        ...state,
        lines: state.lines.map((l) =>
          // FE7: never a gift-card line — a discounted card is sold for less than it can buy
          action.keys.includes(l.key) && !l.isReturn && !l.giftCardCode
            ? {
                ...l,
                discount: {
                  discountId: action.discount.id,
                  name: action.discount.name,
                  type: action.discount.type,
                  amount: action.discount.amount,
                  // Binding default 22(c). Normalised here rather than at the dialog so every
                  // caller of this action stores the same shape — the MAUI till's
                  // `DiscountAudit.NormaliseReason` is the twin, and till-design C2 pins them.
                  reason: normaliseReason(action.reason),
                  // ⚠ W-P3: present ONLY when a supervisor signed for it. `undefined` rather than
                  // null, so it is omitted from a parked basket's JSON entirely and an old parked
                  // basket round-trips byte-identically.
                  authorisedBy: action.authorisedBy ?? undefined,
                },
              }
            : l,
        ),
      };
    case "clearDiscount":
      return {
        ...state,
        lines: state.lines.map((l) =>
          l.key === action.key
            // ⚠ Clearing an AUTOMATIC discount waives it for this line. Without that the resolver puts
            // it back on the next scan and the operator cannot charge full price at all. Clearing a
            // manual one needs no flag — nothing re-applies those.
            ? { ...l, discount: undefined, autoWaived: l.autoWaived || l.discount?.auto === true }
            : l,
        ),
      };
    /**
     * D8: a £/% figure the operator typed, for the shop that has not set up a catalogue rule but
     * still has a dented box on the counter.
     *
     * ⚠⚠ IT CARRIES `AD_HOC_DISCOUNT_ID` (−1), NOT AN INVENTED POSITIVE ONE. `LineMeta.discounts[]`
     * is projected into legacy `Transaction_Discount` rows keyed on a REAL `DiscountId`, so a made-up
     * id would FK-fail the projection of the whole sale — the trap the members' sentinel already
     * exists to avoid. Checkout keeps anything without a positive id out of that array; the money
     * still travels as `DiscountPence`, and the reason and authoriser still travel as an authority.
     */
    case "applyAdHocDiscount":
      return {
        ...state,
        lines: state.lines.map((l) =>
          action.keys.includes(l.key) && !l.isReturn && !l.giftCardCode
            ? {
                ...l,
                discount: {
                  discountId: AD_HOC_DISCOUNT_ID,
                  name: action.label,
                  type: action.kind,
                  amount: action.amount,
                  reason: normaliseReason(action.reason),
                  authorisedBy: action.authorisedBy ?? undefined,
                },
              }
            : l,
        ),
      };
    /**
     * Every AUTOMATIC discount, recomputed from scratch — the member's tier and any live scheduled
     * rule, through the ONE shared resolver.
     *
     * ⚠⚠ IT REPLACES ITS OWN PREVIOUS ANSWER AND LEAVES THE OPERATOR'S ALONE. That is what `auto`
     * on the line's discount is for: a line the operator discounted is skipped (no stacking), while a
     * line this resolver discounted a moment ago is free to change — because a rule may have expired,
     * a member may have been detached, or a bigger rule may now apply. Reading "has any discount" here
     * instead would freeze the first answer and make the discount vanish on the next scan.
     *
     * ⚠ Recomputing from scratch rather than patching is deliberate: it means "member detached",
     * "rule expired at 17:00" and "a better rule arrived" all take the same code path, so there is no
     * separate clear-down to forget. The old `clearMemberDiscount` action is gone for that reason —
     * detaching a customer now just re-runs this with no member.
     */
    case "autoDiscounts": {
      const now = new Date(action.nowMs);
      return {
        ...state,
        lines: state.lines.map((l) => {
          // The operator's own discount wins outright and is never touched.
          if (l.discount && !l.discount.auto) return l;
          // ⚠ And a line whose automatic discount the operator took OFF stays off — see `autoWaived`.
          if (l.autoWaived) return l.discount ? { ...l, discount: undefined } : l;

          const got = autoDiscountForLine(
            {
              unitIncPence: l.pricePence,
              quantity: l.quantity,
              categoryId: l.item.catId,
              itemIdOne: l.item.idOne,
              isReturn: !!l.isReturn,
              hasManualDiscount: false,
              isGiftCard: !!l.giftCardCode,
              isCardSurcharge: isCardSurcharge(l),
            },
            action.member,
            action.rules,
            now,
          );

          if (!got) return l.discount ? { ...l, discount: undefined } : l;

          return {
            ...l,
            discount: {
              discountId: got.discountId,
              name: got.name,
              type: got.type,
              amount: got.amount,
              // ⚠ The tier or the rule IS the reason — nobody decided anything, so there is nothing
              // to type and nothing to refuse. It still has to be recorded: "all discounts need to
              // be tracked" includes the automatic ones, and this is the one most likely to be
              // queried later ("why is this basket 10% under?").
              reason: got.reason,
              auto: true,
            },
          };
        }),
      };
    }
    case "remove":
      return { ...state, lines: state.lines.filter((l) => l.key !== action.key) };
    case "move": {
      const i = state.lines.findIndex((l) => l.key === action.key);
      const j = i + action.direction;
      if (i < 0 || j < 0 || j >= state.lines.length) return state;
      const lines = [...state.lines];
      [lines[i], lines[j]] = [lines[j], lines[i]];
      return { ...state, lines };
    }
    case "restore":
      return action.state;
    case "clear":
      return { lines: [], nextKey: 1 };
  }
}

// ── the in-progress basket survives navigation (and a refresh) ───────────────
// The basket used to live only in TillPage's state, so switching to Cash/Inventory/Reporting
// unmounted the page and silently threw the sale away — a cashier who nipped to Inventory to check
// a price came back to an empty till. It now persists until the sale completes or the lines are
// removed, which is what a till is expected to do. A refresh or a browser crash mid-sale is
// recovered for the same reason.
//
// The stored basket is DROPPED after a trading day: a half-scanned basket reappearing on Monday
// morning would be worse than losing it, because the cashier would not necessarily notice it was
// there before adding today's items.
const STORAGE_KEY = "plutus.basket";

const MAX_AGE_MS = 12 * 60 * 60 * 1000;
const EMPTY: BasketState = { lines: [], nextKey: 1 };

function loadPersisted(): BasketState {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return EMPTY;
    const saved = JSON.parse(raw) as { at: number; state: BasketState };
    if (!saved?.state || !Array.isArray(saved.state.lines) || saved.state.lines.length === 0) return EMPTY;
    if (!(saved.at > 0) || Date.now() - saved.at > MAX_AGE_MS) { localStorage.removeItem(STORAGE_KEY); return EMPTY; }
    return saved.state;
  } catch {
    // corrupt or unreadable — start clean rather than wedge the till on a bad string
    try { localStorage.removeItem(STORAGE_KEY); } catch { /* ignore */ }
    return EMPTY;
  }
}

/** Lines in the persisted basket, WITHOUT mounting the till page — the app shell needs this to
 *  warn before navigating away (leaving abandons an unsaved basket). Applies the same expiry as
 *  loadPersisted, so a basket that would be discarded anyway doesn't raise a warning. */
export function basketLineCount(): number {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return 0;
    const saved = JSON.parse(raw) as { at: number; state: BasketState };
    if (!Array.isArray(saved?.state?.lines)) return 0;
    if (!(saved.at > 0) || Date.now() - saved.at > MAX_AGE_MS) return 0;
    return saved.state.lines.length;
  } catch {
    return 0;
  }
}

export const useBasket = () => {
  const [state, dispatch] = useReducer(reduce, undefined, loadPersisted);
  useEffect(() => {
    try {
      // An empty basket clears the key outright, so "sale completed" and "lines removed" both
      // leave nothing behind to restore.
      if (state.lines.length === 0) localStorage.removeItem(STORAGE_KEY);
      else localStorage.setItem(STORAGE_KEY, JSON.stringify({ at: Date.now(), state }));
    } catch {
      // private mode / quota — the till keeps working, it just won't survive navigation
    }
  }, [state]);
  return [state, dispatch] as const;
};

/** The longest reason stored — the twin of `SharedKernel.DiscountAudit.MaxReasonLength`.
 *
 *  ⚠ If you change this, change it there too: the reason rides in the line's `discountsJson`, and
 *  two tills truncating at different lengths write the same operator's words two different ways. */
export const MAX_DISCOUNT_REASON = 200;

/** A discount's reason, in the form it is stored in — the C2 twin of
 *  `SharedKernel.DiscountAudit.NormaliseReason`, and it must agree with it character for character.
 *
 *  ⚠ Returns undefined for anything that is not a reason. `"   "` is present to a query and blank to
 *  a human, which is exactly the empty column binding default 22(c) exists to prevent.
 *
 *  ⚠ Interior whitespace collapses so "damaged    box" and "damaged box" group together in a report
 *  instead of reading as two distinct reasons.
 *
 *  ⚠ TRUNCATED, never refused for length — losing a discount at a counter over a long paste costs
 *  more than the extra characters are worth. */
export const normaliseReason = (raw: string | undefined | null): string | undefined => {
  if (!raw) return undefined;
  const collapsed = raw.trim().replace(/\s+/g, " ");
  if (collapsed.length === 0) return undefined;
  return collapsed.length <= MAX_DISCOUNT_REASON ? collapsed : collapsed.slice(0, MAX_DISCOUNT_REASON);
};

/** Pence knocked off a line by its discount (0 when none). Matches the MAUI
 *  engine: type 0 = fixed amount per unit, else fraction of the line value.
 *
 *  ⚠ THE ARITHMETIC MOVED TO `scheduledDiscounts.discountPence` AND THIS DELEGATES — a copy removed,
 *  not added. The auto-discount resolver has to predict what a candidate rule is worth in order to
 *  pick the largest, and predicting it with a second expression is how a "largest wins" comparison
 *  chooses a different winner from the one the customer actually pays for. */
export const lineDiscountPence = (l: BasketLine): number => {
  if (!l.discount || l.isReturn) return 0;
  return discountPence(l.discount.type, l.discount.amount, l.pricePence, l.quantity);
};

/**
 * W-P3: what a discount WOULD come to, in pence, across the lines it is about to be applied to.
 *
 * ⚠⚠ IT USES `lineDiscountPence` — THE REAL ENGINE — rather than re-deriving the arithmetic. MAUI
 * makes the same choice for the same reason (till-design, binding default 22): computing "what will
 * this come to?" a second time for the permission check produces a copy that can drift from the thing
 * it is checking, and it would drift only on baskets that do not divide evenly. Building the real
 * figure and summing it cannot disagree with itself.
 */
export const plannedDiscountPence = (lines: BasketLine[], discount: Discount, keys: number[]): number => {
  // ⚠ The catalogue's `Discount` and a line's `LineDiscount` are different shapes (`id` vs
  // `discountId`), so this maps rather than spreads — a spread compiled once and would have silently
  // dropped the id the moment either type changed.
  const asLine = {
    discountId: discount.id,
    name: discount.name,
    type: discount.type,
    amount: discount.amount,
  };

  return lines
    // ⚠ Same exclusions the reducer applies — returns and gift-card lines take no discount, so
    // including them here would over-state the planned figure and refuse a legal discount.
    .filter((l) => keys.includes(l.key) && !l.isReturn && !l.giftCardCode)
    .reduce((total, l) => total + lineDiscountPence({ ...l, discount: asLine }), 0);
};

/** Signed line value in pence (returns negative). */
export const lineTotalPence = (l: BasketLine): number =>
  (l.pricePence * l.quantity - lineDiscountPence(l)) * (l.isReturn ? -1 : 1);

export const basketTotals = (lines: BasketLine[]) => ({
  totalPence: lines.reduce((t, l) => t + lineTotalPence(l), 0),
  totalExTaxPence: lines.reduce((t, l) => {
    const discount = lineDiscountPence(l);
    // apportion the discount to the ex-tax value by the line's ex/inc ratio
    const ratio = l.pricePence > 0 ? l.exPricePence / l.pricePence : 1;
    const ex = l.exPricePence * l.quantity - Math.round(discount * ratio);
    return t + ex * (l.isReturn ? -1 : 1);
  }, 0),
  units: lines.reduce((t, l) => t + l.quantity * (l.isReturn ? -1 : 1), 0),
});
