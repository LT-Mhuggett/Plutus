// WP14 — how the operator is going to take a card payment.
//
// ⚠⚠ THE C2 TWIN of `src/Plutus.Client.Core/PaymentGateway.cs`, which MAUI uses. Every rule below is
// deliberately identical to it, and its .NET tests (`PaymentGatewayTests.cs`) are mirrored in
// `cardPayment.test.ts`.
//
// ⚠⚠ WHY THIS FILE EXISTS AT ALL, 2026-08-21. Until today these three cases were decided INLINE in
// `CheckoutDialog.tsx` — a ternary on `!gateway || gateway.provider === "standalone"` — while the
// .NET side had a named function with fifteen tests. `PaymentGateway.cs`'s own header said so:
// *"this mirrors the web till's CheckoutDialog.tsx, where the same three cases are decided inline"*.
// A rule that exists in two languages with a COMMENT between them is the exact fault C2 exists to
// prevent, and four of the five frontend twins spent months in that state before anybody compared
// them mechanically. So: same shape, same names, same vectors, pinned by tests on both sides.
//
// ⚠⚠ THE RULE IS NOT "read `provider`". Every provider on the platform reports `integrated: false`
// today, so a till that switched on the provider name alone would sit waiting for a terminal that
// will never respond, with a customer in front of it. What decides the flow is `integrated`; the
// provider only decides the wording.

import type { ActiveGateway } from "../api.ts";

export type CardFlow =
  /** The operator uses their own chip & pin machine and confirms approval on the till.
   *  ⚠ The DEFAULT and the fallback — the flow that works when nothing else does, because the only
   *  thing it depends on is a human. */
  | "standalone"
  /** The till drives the terminal and waits for its result. ⚠ Requires a wired integration;
   *  nothing on the platform has one yet. */
  | "integrated";

/** What the checkout screen should say and do about cards. */
export interface CardPaymentDisplay {
  flow: CardFlow;
  /** Provider name for the screen, e.g. "Worldpay". Never empty. */
  label: string;
  /** The tenant has chosen a real provider but no integration is wired yet, so the operator still
   *  confirms manually — the checkout must SAY so, or a cashier looking at "Card via Worldpay"
   *  waits for a terminal prompt that is never coming. */
  pendingIntegration: boolean;
}

/** ⚠ Shown when we have nothing better. Not "Unknown": the operator does not care what the till
 *  failed to fetch, they care which machine to reach for.
 *  ⚠ Verbatim `PaymentGateway.StandaloneLabel`. */
export const STANDALONE_LABEL = "your card terminal";

/** ⚠ The wire value that means "no integration". Verbatim
 *  `SharedKernel.PaymentProviderCatalogue.Standalone`, which the server compares against too. */
const STANDALONE_PROVIDER = "standalone";

/**
 * Turn the server's answer into a decision. Never throws.
 *
 * ⚠ NULL IS A NORMAL INPUT and it means standalone, not "error". A till that has never been online,
 * one whose poll just failed, and one belonging to a tenant that picked the standalone default are
 * all the same thing to a cashier: take payment on the machine and confirm it. Blocking card sales
 * because a *cosmetic* lookup failed would be a self-inflicted outage.
 *
 * ⚠ `undefined` is accepted as well as `null` — the dialog's state starts undefined before its
 * first fetch resolves, and .NET's nullable has only the one empty value to mirror.
 */
export function resolveCardPayment(
  gateway: ActiveGateway | null | undefined,
): CardPaymentDisplay {
  // Standalone whenever there is no answer, no provider, or the provider IS standalone.
  if (
    !gateway ||
    !gateway.provider ||
    !gateway.provider.trim() ||
    gateway.provider.trim().toLowerCase() === STANDALONE_PROVIDER
  ) {
    return { flow: "standalone", label: STANDALONE_LABEL, pendingIntegration: false };
  }

  const label = gateway.label && gateway.label.trim() ? gateway.label.trim() : gateway.provider.trim();

  // ⚠ A chosen provider with no wired integration is STILL the standalone flow. The only difference
  // is that the screen names the provider and says the integration is pending, so the cashier knows
  // to use the terminal by hand rather than wait.
  return gateway.integrated
    ? { flow: "integrated", label, pendingIntegration: false }
    : { flow: "standalone", label, pendingIntegration: true };
}

/**
 * Does this reading mean "the tenant has chosen no provider at all"?
 *
 * ⚠ NOT `label === STANDALONE_LABEL`. A tenant is free to call their provider anything, and one who
 * typed "your card terminal" as their own label would silently take the wrong branch. The
 * no-provider case is the only one that is standalone AND not pending.
 *
 * ⚠ MAUI decides the same thing the same way, in `CheckoutAlert.CardHintFor`.
 */
export const isUnconfigured = (card: CardPaymentDisplay): boolean =>
  card.flow === "standalone" && !card.pendingIntegration;
