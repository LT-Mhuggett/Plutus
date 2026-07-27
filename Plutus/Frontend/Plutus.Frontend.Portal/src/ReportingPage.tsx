import { useState } from "react";
import Dashboard from "./Dashboard.tsx";
import VatPage from "./VatPage.tsx";
import ItemsSoldPage from "./ItemsSoldPage.tsx";

// All the portal's analytics reports under one "Reporting" tab (Summary = the dashboard).
// Banking, Stock, Prices etc. stay as their own top-level tabs (they carry actions, not just
// reporting). Each sub-view keeps its own look — the till has its own Reporting tab too.
const SUBTABS = ["Summary", "VAT", "Items sold"] as const;
type SubTab = (typeof SUBTABS)[number];

export default function ReportingPage() {
  const [sub, setSub] = useState<SubTab>("Summary");
  return (
    <>
      <nav className="subtabs">
        {SUBTABS.map((s) => (
          <button key={s} className={s === sub ? "subtab active" : "subtab"} onClick={() => setSub(s)}>{s}</button>
        ))}
      </nav>
      {sub === "Summary" && <Dashboard />}
      {sub === "VAT" && <VatPage />}
      {sub === "Items sold" && <ItemsSoldPage />}
    </>
  );
}
