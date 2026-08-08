import { useState } from "react";
import VatReturn from "./VatReturn.tsx";
import VatBands from "./VatBands.tsx";
import VatCorrections from "./VatCorrections.tsx";
import VatRules from "./VatRules.tsx";

// WP2c — the portal's VAT home (Matt, 2026-08-08: "a specific VAT page on the portal that explains
// and references which VAT rules are being used").
//
// VAT used to be one report buried under Reporting. It is now its own tab, because the portal is
// the SOURCE OF VAT TRUTH — the bands the tills apply are edited here, and a shopkeeper needs to
// see the rules and the citations, not just a total.
//
//   Return      — the figures for the period, on the basis HMRC requires
//   Bands       — the rates and classes the tills apply (portal.company.manage)
//   Corrections — restating past returns filed on the old, wrong basis
//   Rules       — every rule the software applies, with its HMRC source
//
// Reporting → VAT still shows the Return, so nothing moved out from under anyone.

const SUBTABS = ["Return", "Bands", "Corrections", "Rules"] as const;
type SubTab = (typeof SUBTABS)[number];

export default function VatPage() {
  const [sub, setSub] = useState<SubTab>("Return");
  return (
    <>
      <nav className="subtabs">
        {SUBTABS.map((s) => (
          <button key={s} className={s === sub ? "subtab active" : "subtab"} onClick={() => setSub(s)}>{s}</button>
        ))}
      </nav>
      {sub === "Return" && <VatReturn />}
      {sub === "Bands" && <VatBands />}
      {sub === "Corrections" && <VatCorrections />}
      {sub === "Rules" && <VatRules />}
    </>
  );
}
