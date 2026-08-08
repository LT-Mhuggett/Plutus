import { useState } from "react";
import SummaryReport from "./SummaryReport.tsx";
import CustomReport from "./CustomReport.tsx";
import VatReturn from "./VatReturn.tsx";
import ItemsSoldPage from "./ItemsSoldPage.tsx";
import { CategorySalesReport, BestSellersReport, NegativeStockReport } from "./ReportsExtra.tsx";

// All the portal's analytics reports under one "Reporting" tab. Summary is the rich company
// overview (WP3.1, ported from the till); Custom is the sales-line drill-down (WP3.2); VAT is the
// till-style band view (WP3.5). The Dashboard TAB keeps the simpler rollup chart + KPI pills.
const SUBTABS = ["Summary", "Custom", "VAT", "Items sold", "Category sales", "Best sellers", "Negative stock"] as const;
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
      {sub === "Summary" && <SummaryReport />}
      {sub === "Custom" && <CustomReport />}
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
