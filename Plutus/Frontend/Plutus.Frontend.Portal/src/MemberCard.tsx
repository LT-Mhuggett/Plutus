import { useEffect, useState } from "react";
import { fetchCompanies } from "./api.ts";
import Barcode39 from "./Barcode39.tsx";

// FE2: a printable loyalty card. Sized CR80 (85.6 × 54 mm — the standard bank/loyalty card), so it
// prints onto card stock or adhesive card blanks from an ordinary printer; no hardware dependency
// (the FE3 agent could drive a dedicated card printer later). The barcode payload carries the "C"
// prefix so a till scan is recognised as a member card rather than a product or a receipt.

export interface MemberCardData {
  name: string;
  memberNo: string;
  memberBarcode: string;      // "C" + memberNo, straight from the API
  tier?: string | null;
  renewalDay?: string | null;
}

export default function MemberCard({ data, onClose }: { data: MemberCardData; onClose: () => void }) {
  const [company, setCompany] = useState("");
  useEffect(() => {
    void fetchCompanies().then((cs) => setCompany(cs[0]?.name ?? "")).catch(() => undefined);
  }, []);

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog">
        <h3 className="no-print">Membership card</h3>
        <p className="muted small no-print">
          Print onto card stock at 100% scale (no “fit to page”, or the barcode narrows and may not
          scan). Check the printed barcode with a scanner before running a batch.
        </p>

        {/* the only element that reaches the printer — see .member-card in portal.css */}
        <div className="member-card" id="member-card">
          <div className="mc-head">
            <span className="mc-company">{company || "Membership"}</span>
            {data.tier && <span className="mc-tier">{data.tier}</span>}
          </div>
          <div className="mc-name">{data.name}</div>
          <div className="mc-barcode">
            <Barcode39 value={data.memberBarcode} height={38} fit showText={false} />
          </div>
          <div className="mc-foot">
            <span className="mc-no">{data.memberNo}</span>
            {data.renewalDay && <span className="mc-renew">valid to {data.renewalDay}</span>}
          </div>
        </div>

        <div className="dialog-actions no-print">
          <button className="ghost" onClick={onClose}>Close</button>
          <button className="primary" onClick={() => window.print()}>Print card</button>
        </div>
      </div>
    </div>
  );
}
