import { useState } from "react";
import SummaryReport from "./SummaryReport.tsx";
import CustomReport from "../StatisticsPage.tsx";
import VatReport from "./VatReport.tsx";

const SUBTABS = ["Summary", "Custom", "VAT"] as const;
type SubTab = (typeof SUBTABS)[number];

export default function ReportingPage() {
  const [sub, setSub] = useState<SubTab>("Summary");

  return (
    <section className="panel">
      <div className="panel-head">
        <h2>Reporting</h2>
        <nav className="subtabs">
          {SUBTABS.map((s) => (
            <button key={s} className={s === sub ? "subtab active" : "subtab"} onClick={() => setSub(s)}>
              {s}
            </button>
          ))}
        </nav>
      </div>
      {sub === "Summary" ? <SummaryReport /> : sub === "Custom" ? <CustomReport /> : <VatReport />}
    </section>
  );
}
