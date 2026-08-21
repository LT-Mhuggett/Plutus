import { useEffect, useState } from "react";
import {
  ApiError, fetchCarrierBags, gbp, putCarrierBag, withdrawCarrierBag, type CarrierBagRow,
} from "./api.ts";
import DataTable, { type Column } from "./DataTable.tsx";
import { ask } from "./Ask.tsx";

/**
 * Carrier bags — set here, sold on every till. Ruling 2026-08-19.
 *
 * ⚠⚠ Matt: *"Can you add the carrier bag decision to the portal? That creates the 5p and 20p bags at
 * the back and that pushes down to the tills… This would be cleaner than creating a bag at each till."*
 * Before this, each till stored ONE bag barcode as a local device preference, so a five-till shop set
 * it five times, the tills could disagree, and a new till sold no bags until somebody remembered — and
 * it is how a till came to hold `"001"`, a barcode no item has.
 *
 * ⚠⚠ **BOTH PRICES, NOT ONE OR THE OTHER** — the answer to Matt's question. A shop normally sells a
 * cheap single-use bag at the statutory minimum AND a dearer bag for life, side by side. They are
 * different products, so this is a list and not a pair of settings.
 *
 * ⚠⚠ **NO STATUTORY PRICE IS SUGGESTED HERE.** England's minimum rose from 5p to 10p on 21 May 2021
 * and the four nations have moved at different times, so the screen asks rather than prefilling: a
 * figure baked into a build is wrong the next time Parliament moves, and an owner would read a
 * prefilled box as advice.
 *
 * ⚠ Standard-rated (20%), unlike a gift-card activation — see `SharedKernel.CarrierBags`.
 */
export default function CarrierBagsSection() {
  const [bags, setBags] = useState<CarrierBagRow[] | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const [price, setPrice] = useState("");
  const [name, setName] = useState("");

  /**
   * ⚠ A 403 HERE IS A SENTENCE, NOT A NUMBER. The list itself is readable by anyone in the portal —
   * it is only what the shop sells — but changing it needs `portal.company.manage`, and a manager who
   * cannot would otherwise be shown the bare string "403" and raise a support call.
   */
  const readError = (e: unknown) =>
    e instanceof ApiError && e.status === 403
      ? "Only someone who can manage company settings may change the bags."
      : String(e instanceof Error ? e.message : e);

  const load = () =>
    fetchCarrierBags()
      .then((rows) => { setBags(rows); setError(""); })
      .catch((e) => setError(readError(e)));

  useEffect(() => { void load(); }, []);

  const add = async () => {
    // ⚠ Typed in POUNDS because that is how a price list is written, and stored in PENCE because that
    // is how money is held everywhere else here. The conversion happens once, on this line.
    const pounds = Number(price.replace(/[£\s,]/g, ""));
    if (!Number.isFinite(pounds) || pounds <= 0) {
      setError("Enter the bag price, for example 0.10");
      return;
    }
    const pence = Math.round(pounds * 100);

    // ⚠ A PRICE ALREADY ON SALE IS A RENAME, NOT A SECOND BAG — say so first, because "Add" reads
    // like it will create one, and an owner correcting a name should know which row is about to move.
    const clash = bags?.find((b) => b.pricePence === pence);
    if (clash) {
      const go = await ask.confirm({
        title: `Rename the ${gbp(pence)} bag?`,
        body: (
          <p>
            A bag at {gbp(pence)} is already on sale as “{clash.name}”. A bag’s price is its identity,
            so this renames that one rather than adding a second.
          </p>
        ),
        confirmLabel: "Rename it",
      });
      if (!go) return;
    }

    setBusy(true);
    try {
      await putCarrierBag(pence, name.trim());
      setPrice("");
      setName("");
      await load();
      setError("");
    } catch (e) {
      setError(readError(e));
    } finally {
      setBusy(false);
    }
  };

  const withdraw = async (bag: CarrierBagRow) => {
    const go = await ask.confirm({
      title: `Stop selling the ${gbp(bag.pricePence)} bag?`,
      // ⚠ Say what happens to the history, because "will this wreck last month's VAT return" is the
      // question an owner is really asking, and the honest answer is no — it is withdrawn, not deleted.
      body: (
        <p>
          Every till stops offering “{bag.name}”. Sales already taken keep it on their receipts and in
          past reports — nothing is deleted.
        </p>
      ),
      confirmLabel: "Stop selling it",
      danger: true,
    });
    if (!go) return;

    setBusy(true);
    try {
      await withdrawCarrierBag(bag.pricePence);
      await load();
    } catch (e) {
      setError(readError(e));
    } finally {
      setBusy(false);
    }
  };

  const columns: Column<CarrierBagRow>[] = [
    { key: "name", label: "Bag" },
    { key: "pricePence", label: "Price", numeric: true, render: (b) => gbp(b.pricePence) },
    // ⚠ The barcode is shown because a shop may want it on a shelf label, and because it makes the
    // "price is the identity" rule visible rather than a hidden convention.
    { key: "idOne", label: "Barcode", render: (b) => <code>{b.idOne}</code> },
  ];

  return (
    // Collapsed <details> like every other Company section — the Locations idiom (2026-08-20).
    <details className="card store-card">
      <summary><strong>Carrier bags</strong></summary>
      <p className="muted">
        Set the bags here and every till offers them — there is nothing to configure at a till. Each
        bag sells as a real line, standard-rated, in its own “Carrier bags” category, so bag charges
        never inflate a product category and the bags stay out of the till’s Inventory list.
      </p>
      <p className="muted small">
        Most shops sell two: a single-use bag at the statutory minimum where they trade, and a dearer
        bag for life. That minimum differs by nation and has changed over time, so Plutus does not
        assume a figure — check the current one and enter it.
      </p>

      {error !== "" && <p className="error">{error}</p>}

      {bags === null ? (
        <p className="muted">Loading…</p>
      ) : (
        <DataTable
          columns={columns}
          rows={bags}
          getKey={(b) => b.idOne}
          initialSortKey="pricePence"
          emptyText="No bags yet — the Bag button is hidden on every till until you add one."
          rowActions={(b) => (
            <button type="button" className="ghost small" disabled={busy} onClick={() => void withdraw(b)}>
              Stop selling
            </button>
          )}
        />
      )}

      <div className="toolbar">
        <label>
          Price
          {/* ⚠ No placeholder price. A suggested "0.10" is a compliance figure Plutus cannot stand
              behind for every nation, and an owner would take it as advice. */}
          <input
            className="small"
            value={price}
            onChange={(e) => setPrice(e.target.value)}
            inputMode="decimal"
            aria-label="Bag price in pounds"
          />
        </label>
        <label className="grow">
          Name shown on the receipt
          <input
            className="small"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Single-use carrier bag"
            aria-label="Bag name"
          />
        </label>
        <button type="button" className="primary small" disabled={busy} onClick={() => void add()}>
          Add bag
        </button>
      </div>
      <p className="muted small">
        Leave the name blank and the price is used. Changing a price means adding the new bag and
        stopping the old one — they are different products, and the old one is on receipts already.
      </p>
    </details>
  );
}
