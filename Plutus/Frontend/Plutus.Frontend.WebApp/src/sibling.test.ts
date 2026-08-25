import { describe, expect, it } from "vitest";
import { configuredPortalUrl, hasPortalAccess, portalHostFor } from "./sibling.ts";

/**
 * Getting from the till to the portal.
 *
 * ⚠⚠ WHY THESE VECTORS. On 2026-08-25 the till moved to its own subdomain and the derivation here
 * still said "prepend `admin.`" — which on `till.plutus.example` produces
 * **`admin.till.plutus.example`**, a host that does not exist, on the one button whose job is to get
 * somebody OUT of the till. It was masked live because the build sets `VITE_PORTAL_URL`, i.e. the
 * broken branch was simply never reached. Its mirror image in the portal (`auth.ts tillUrl()`) had
 * the same fault the same day, and Matt found that one by clicking it.
 *
 * ⚠ The lesson in test form: a fallback that is confidently wrong is worse than one that returns
 * null, because null hides the button and wrong sends somebody to a dead host.
 */

describe("portalHostFor — deriving the portal from the till's own host", () => {
  it("⚠ till.X gives admin.X, NOT admin.till.X", () => {
    expect(portalHostFor("https:", "till.plutus.huggett.dscloud.me"))
      .toBe("https://admin.plutus.huggett.dscloud.me");
  });

  it("is idempotent when already on the portal", () => {
    expect(portalHostFor("https:", "admin.plutus.huggett.dscloud.me"))
      .toBe("https://admin.plutus.huggett.dscloud.me");
  });

  it("⚠ returns null for the BARE host — that is the public landing page now, not a till", () => {
    // The old rule prepended `admin.` to anything. Guessing from an unknown host is what produced
    // a dead link; null hides the button instead.
    expect(portalHostFor("https:", "plutus.huggett.dscloud.me")).toBeNull();
  });

  it("returns null on localhost and bare IPs, so dev does not get a bogus link", () => {
    expect(portalHostFor("http:", "localhost:5273")).toBeNull();
    expect(portalHostFor("http:", "10.1.1.40:5273")).toBeNull();
    expect(portalHostFor("https:", "")).toBeNull();
  });

  it("keeps the protocol it was given", () => {
    expect(portalHostFor("http:", "till.example.test")).toBe("http://admin.example.test");
  });
});

describe("configuredPortalUrl — what a build-time value has to survive", () => {
  it("takes an absolute http(s) URL and trims trailing slashes", () => {
    expect(configuredPortalUrl("https://admin.example.com//")).toBe("https://admin.example.com");
    expect(configuredPortalUrl("  http://10.1.1.40:5274  ")).toBe("http://10.1.1.40:5274");
  });

  it("treats unset or empty as unset", () => {
    expect(configuredPortalUrl(undefined)).toBeNull();
    expect(configuredPortalUrl("   ")).toBeNull();
  });

  it("⚠ refuses a scheme-less or relative value rather than shipping a broken link", () => {
    expect(configuredPortalUrl("admin.example.com")).toBeNull();
    expect(configuredPortalUrl("/portal")).toBeNull();
    expect(configuredPortalUrl("//admin.example.com")).toBeNull();
  });

  it("⚠ refuses javascript: — this value goes straight into an href", () => {
    expect(configuredPortalUrl("javascript:alert(1)")).toBeNull();
  });
});

describe("hasPortalAccess", () => {
  it("any portal.* permission, or platform-admin, means there is something to see", () => {
    expect(hasPortalAccess(["pos.sell", "portal.reports.view"])).toBe(true);
    expect(hasPortalAccess(["platform-admin"])).toBe(true);
  });

  it("a plain cashier gets no button — it would only be noise", () => {
    expect(hasPortalAccess(["pos.sell", "pos.stock.adjust"])).toBe(false);
    expect(hasPortalAccess([])).toBe(false);
  });
});
