import { useEffect, useState } from "react";
import SummaryReport from "./SummaryReport.tsx";
import CustomReport from "./CustomReport.tsx";
import VatReturn from "./VatReturn.tsx";
import ItemsSoldPage from "./ItemsSoldPage.tsx";
import { CategorySalesReport, BestSellersReport, NegativeStockReport } from "./ReportsExtra.tsx";
import { useNav } from "./nav.tsx";
import { dayFromFocus } from "./dayDrill.ts";

// All the portal's analytics reports under one "Reporting" tab. Summary is the rich company
// overview (WP3.1, ported from the till); Custom is the sales-line drill-down (WP3.2); VAT is the
// till-style band view (WP3.5). The Dashboard TAB keeps the simpler rollup chart + KPI pills.
const SUBTABS = ["Summary", "Custom", "VAT", "Items sold", "Category sales", "Best sellers", "Negative stock"] as const;
type SubTab = (typeof SUBTABS)[number];

export default function ReportingPage() {
  const [sub, setSub] = useState<SubTab>("Summary");
  const { focus } = useNav();

  // ⚠⚠ A DAY DRILL LANDS ON **Custom**, WHEREVER IT CAME FROM (WP-DRILL, 2026-08-21). Matt asked for
  // it from both charts: *"when I click on a day in the dashboard it needs to take be to reporting
  // filtered on the sales taken on THAT day"* and *"In reporting, I need to be able to click on a
  // day and it shows all the sales from that specific day."* One destination, two doors.
  const day = dayFromFocus(focus);

  // ⚠ THE SUBTAB SWITCH IS AN EFFECT, not a render-time assignment. Setting state during render is
  // the React warning that becomes a real bug under StrictMode's double-invoke, and the drill from
  // the Dashboard arrives on the SAME render that mounts this page.
  useEffect(() => {
    if (day) setSub("Custom");
  }, [day]);

  return (
    <>
      <nav className="subtabs">
        {SUBTABS.map((s) => (
          <button key={s} className={s === sub ? "subtab active" : "subtab"} onClick={() => setSub(s)}>{s}</button>
        ))}
      </nav>
      {sub === "Summary" && <SummaryReport />}
      {/* ⚠ `day` ONLY WHILE THE CUSTOM TAB IS THE DRILL'S TARGET. Leaving the hint attached would
          re-narrow the range to that one day every time the operator came back to this subtab,
          having widened it — a filter that silently reapplies itself is worse than one that does
          not work. */}
      {sub === "Custom" && <CustomReport day={day ?? undefined} />}
      {/* WP2c: the VAT return only. Bands, corrections and the rules explainer live on the
          dedicated VAT tab — this stays so nothing moved out from under anyone. */}
      {sub === "VAT" && <VatReturn />}
      {sub === "Items sold" && <ItemsSoldPage />}
      {sub === "Category sales" && <CategorySalesReport />}
      {sub === "Best sellers" && <BestSellersReport />}
      {sub === "Negative stock" && <NegativeStockReport />}
    </>
  );
}
