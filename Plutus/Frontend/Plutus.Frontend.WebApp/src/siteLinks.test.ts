import { describe, expect, it } from "vitest";
import { externalUrl } from "./siteLinks.ts";

/**
 * ⚠ The point of these vectors is that a BAD value must read as "unset" and hide the link, never as
 * a link that goes nowhere. A dead "Switch to portal" on a login screen is worse than no link at
 * all: it is offered to somebody who is already stuck.
 */
describe("externalUrl", () => {
  it("accepts an absolute https URL", () => {
    expect(externalUrl("https://admin.plutus.huggett.dscloud.me")).toBe("https://admin.plutus.huggett.dscloud.me");
  });

  it("accepts http, for a LAN preview", () => {
    expect(externalUrl("http://10.1.1.40:5274")).toBe("http://10.1.1.40:5274");
  });

  it("trims trailing slashes so the link never doubles up", () => {
    expect(externalUrl("https://admin.example.com///")).toBe("https://admin.example.com");
  });

  it("trims surrounding whitespace — a build var picks it up easily", () => {
    expect(externalUrl("  https://admin.example.com  ")).toBe("https://admin.example.com");
  });

  it("treats unset, empty and whitespace as unset", () => {
    expect(externalUrl(undefined)).toBeNull();
    expect(externalUrl("")).toBeNull();
    expect(externalUrl("   ")).toBeNull();
  });

  it("⚠ refuses a RELATIVE or scheme-less value rather than shipping a broken link", () => {
    // The likely mistakes when somebody sets the var by hand.
    expect(externalUrl("admin.plutus.huggett.dscloud.me")).toBeNull();
    expect(externalUrl("/portal")).toBeNull();
    expect(externalUrl("//admin.example.com")).toBeNull();
  });

  it("⚠ refuses a javascript: URL — this value ends up in an href", () => {
    expect(externalUrl("javascript:alert(1)")).toBeNull();
  });
});
