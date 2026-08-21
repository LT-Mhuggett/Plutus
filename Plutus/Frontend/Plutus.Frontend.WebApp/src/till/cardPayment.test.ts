import { describe, expect, it } from "vitest";
import type { ActiveGateway } from "../api.ts";
import {
  isUnconfigured,
  resolveCardPayment,
  STANDALONE_LABEL,
} from "./cardPayment.ts";

/**
 * ⚠⚠ THE MIRROR OF `tests/Plutus.Tests.Unit/PaymentGatewayTests.cs`, case for case. MAUI decides
 * these three cases with `Client.Core.PaymentGateway.Resolve`; this till decides them with
 * `resolveCardPayment`. Two tills that disagree here means one of them hangs at the payment step —
 * a cashier waiting for a terminal prompt that is never coming, with a customer in front of them.
 *
 * ⚠ Nothing here is money. The fee arithmetic is `surcharge.ts` (twin of `CardSurchargeVat`); this
 * decides only WHICH MACHINE the operator should reach for, and what the screen calls it.
 *
 * ⚠ The .NET file also covers `FetchAsync` — a 500 and a dead socket both resolving to standalone
 * rather than blocking the sale. There is no TS twin of that half: the web till's dialog holds
 * `gateway` as state that starts undefined and stays undefined when the fetch rejects, so the
 * "no answer" case below IS its fetch-failure case.
 */
const gw = (o: Partial<ActiveGateway>): ActiveGateway => ({
  provider: "",
  label: "",
  integrated: false,
  ...o,
});

describe("resolveCardPayment", () => {
  /**
   * ⚠ THE ONE THAT MATTERS. A real provider that isn't wired yet is still the manual flow — but the
   * screen names it and flags the integration as pending, so the cashier reaches for the terminal
   * instead of waiting for a prompt.
   */
  it("a chosen provider with no integration is still the manual flow", () => {
    const d = resolveCardPayment(gw({ provider: "worldpay", label: "Worldpay", integrated: false }));

    expect(d.flow).toBe("standalone");
    expect(d.label).toBe("Worldpay");
    expect(d.pendingIntegration).toBe(true);
  });

  it("a wired integration drives the terminal", () => {
    const d = resolveCardPayment(gw({ provider: "worldpay", label: "Worldpay", integrated: true }));

    expect(d.flow).toBe("integrated");
    expect(d.pendingIntegration).toBe(false);
  });

  /**
   * No answer is a NORMAL input, not an error: never-been-online, failed poll, and "this tenant
   * chose standalone" are the same instruction to a cashier.
   */
  it.each([[null], [undefined]])(
    "no answer at all (%s) resolves to standalone without pretending something is pending",
    (answer) => {
      const d = resolveCardPayment(answer as null | undefined);

      expect(d.flow).toBe("standalone");
      expect(d.label).toBe(STANDALONE_LABEL);
      // ⚠ NOT pending — there is no integration to be waiting for, and saying otherwise would put a
      // permanent "integration pending" notice on a tenant that never chose a provider.
      expect(d.pendingIntegration).toBe(false);
    },
  );

  it.each([
    ["standalone"],
    ["Standalone"], // the wire is a string; casing must not change the flow
    ["  standalone "],
    [""],
    ["   "],
  ])("the standalone provider (%j) resolves to standalone however it is written", (provider) => {
    const d = resolveCardPayment(gw({ provider, label: "ignored", integrated: false }));

    expect(d.flow).toBe("standalone");
    expect(d.label).toBe(STANDALONE_LABEL);
    expect(d.pendingIntegration).toBe(false);
  });

  /**
   * ⚠ Even if the server ever marked the standalone "provider" as integrated, there is nothing to
   * drive — standalone IS the human flow by definition.
   */
  it("standalone marked integrated is still the human flow", () => {
    expect(
      resolveCardPayment(gw({ provider: "standalone", label: "Standalone", integrated: true })).flow,
    ).toBe("standalone");
  });

  /** A provider with no label falls back to the key rather than showing "Card via ". */
  it("a missing label falls back to the provider key", () => {
    expect(resolveCardPayment(gw({ provider: "sumup", label: "  ", integrated: false })).label)
      .toBe("sumup");
  });
});

describe("isUnconfigured", () => {
  /**
   * ⚠⚠ NOT BY COMPARING THE LABEL. A tenant is free to call their provider anything, and one who
   * typed the standalone wording as their own label must still read as configured — otherwise a
   * shop with a real integration is told to use its own chip & pin machine.
   */
  it("a provider labelled like the standalone default is still configured", () => {
    const d = resolveCardPayment(
      gw({ provider: "worldpay", label: STANDALONE_LABEL, integrated: true }),
    );

    expect(d.label).toBe(STANDALONE_LABEL);
    expect(isUnconfigured(d)).toBe(false);
  });

  it("only the no-provider case is unconfigured", () => {
    expect(isUnconfigured(resolveCardPayment(null))).toBe(true);

    // Pending is NOT unconfigured — the shop chose a provider, it just isn't wired.
    expect(
      isUnconfigured(resolveCardPayment(gw({ provider: "worldpay", label: "Worldpay" }))),
    ).toBe(false);
  });
});
