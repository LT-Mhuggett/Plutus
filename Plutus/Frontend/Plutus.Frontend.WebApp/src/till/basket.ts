import { useReducer } from "react";
import type { Discount, Item } from "../api.ts";
import { toPence } from "../money.ts";

export interface LineDiscount {
  discountId: number;
  name: string;
  /** 0 = fixed pence off per unit; else fraction (0.10 = 10%) */
  type: number;
  amount: number;
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
}

export interface BasketState {
  lines: BasketLine[];
  nextKey: number;
}

type Action =
  | { type: "add"; item: Item; quantity?: number }
  | { type: "addReturn"; item: Item; quantity: number; unitPricePence: number; unitExPricePence: number; originSaleId: string }
  | { type: "quantity"; key: number; delta: number }
  | { type: "adjust"; key: number; pricePence: number }
  | { type: "applyDiscount"; discount: Discount; keys: number[] }
  | { type: "clearDiscount"; key: number }
  | { type: "remove"; key: number }
  | { type: "move"; key: number; direction: -1 | 1 }
  | { type: "restore"; state: BasketState }
  | { type: "clear" };

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
    case "quantity":
      return {
        ...state,
        lines: state.lines
          .map((l) => (l.key === action.key ? { ...l, quantity: l.quantity + action.delta } : l))
          .filter((l) => l.quantity > 0),
      };
    case "adjust":
      return {
        ...state,
        lines: state.lines.map((l) => {
          if (l.key !== action.key) return l;
          // keep the tax proportion: scale exPrice by the original ex/inc ratio
          const ratio = l.item.price > 0 ? l.item.exPrice / l.item.price : 1;
          return { ...l, pricePence: action.pricePence, exPricePence: Math.round(action.pricePence * ratio), adjusted: true };
        }),
      };
    case "applyDiscount":
      return {
        ...state,
        lines: state.lines.map((l) =>
          action.keys.includes(l.key) && !l.isReturn
            ? {
                ...l,
                discount: {
                  discountId: action.discount.id,
                  name: action.discount.name,
                  type: action.discount.type,
                  amount: action.discount.amount,
                },
              }
            : l,
        ),
      };
    case "clearDiscount":
      return { ...state, lines: state.lines.map((l) => (l.key === action.key ? { ...l, discount: undefined } : l)) };
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

export const useBasket = () => useReducer(reduce, { lines: [], nextKey: 1 });

/** Pence knocked off a line by its discount (0 when none). Matches the MAUI
 *  engine: type 0 = fixed amount per unit, else fraction of the line value. */
export const lineDiscountPence = (l: BasketLine): number => {
  if (!l.discount || l.isReturn) return 0;
  if (l.discount.type === 0) return Math.round(l.discount.amount * 100) * l.quantity;
  return Math.round(l.pricePence * l.quantity * l.discount.amount);
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
