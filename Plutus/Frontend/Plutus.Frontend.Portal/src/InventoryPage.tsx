import { useEffect, useState } from "react";
import StockPage from "./StockPage.tsx";
import InventoryItems from "./InventoryItems.tsx";
import CategoryManager from "./CategoryManager.tsx";
import { useNav } from "./nav.tsx";

// WP4.1/4.2/4.4: the renamed "Inventory" tab now covers the whole catalogue side of inventory —
// item add/edit (Items), the stock ledger (adjust/count/transfer/locations), and the category
// manager. `focus` lets a deep-link land on a specific sub-tab (e.g. the negative-stock report
// jumps to the ledger).
const SUBTABS = ["Items", "Stock ledger", "Categories"] as const;
type SubTab = (typeof SUBTABS)[number];

export default function InventoryPage() {
  const { focus } = useNav();
  const [sub, setSub] = useState<SubTab>("Items");
  useEffect(() => {
    if (focus === "ledger") setSub("Stock ledger");
    else if (focus === "categories") setSub("Categories");
    else if (focus === "items") setSub("Items");
  }, [focus]);

  return (
    <>
      <nav className="subtabs">
        {SUBTABS.map((s) => (
          <button key={s} className={s === sub ? "subtab active" : "subtab"} onClick={() => setSub(s)}>{s}</button>
        ))}
      </nav>
      {sub === "Items" && <InventoryItems />}
      {sub === "Stock ledger" && <StockPage />}
      {sub === "Categories" && <CategoryManager />}
    </>
  );
}
