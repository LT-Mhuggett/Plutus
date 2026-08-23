using System;

namespace Plutus.Frontend.AppClient.Models
{
    /// <summary>
    /// **What a basket line knows about the thing it is selling.**
    ///
    /// ⚠⚠ THIS REPLACES `Database.Models.ItemModel` ON THE BASKET — L5/L6, 2026-08-23. That model is
    /// an EF entity from the legacy SQLite schema, with a navigation property to `TaxModel`, audit
    /// columns, `Brand`, `Desc`, `Cost`, `Amount` and a dozen other fields. A basket line used
    /// **five** of them.
    ///
    /// ⚠ AND THE DATA WAS ALREADY MODERN. `TillViewModel.FindItem` reads the **v2 store**
    /// (`TillStoreAccess`) and then built an `ItemModel` from the result purely to hand to
    /// `BasketItem` — the legacy type was a costume v2 data wore. Every other construction site did
    /// the same. Nothing was ever loaded from the legacy database to fill one.
    ///
    /// ⚠ THAT IS WHAT WAS PINNING `Helpers/Database/Database.cs` AND THE 55-FILE `Plutus/Data/Database`
    /// PROJECT (L5, L6): not the data, just the shape of the box it travelled in.
    ///
    /// ⚠⚠ A RECORD, AND DELIBERATELY NOT AN ENTITY. Nothing here is tracked, saved or lazily loaded.
    /// A basket line is a snapshot of what is being charged; an entity on a basket invites somebody
    /// to "just save it", which is how a till ends up writing to a database the platform does not read.
    /// </summary>
    [Serializable]
    public sealed class TillItem
    {
        /// <summary>The canonical item code — `ItemModel.Id`, `CatalogueItem.IdOne`. ⚠ NOT the scanned
        /// barcode: `BasketItem.ScannedBarcode` holds that, and the two differ on a multi-barcode item.</summary>
        public string Id { get; set; }

        public string Name { get; set; }

        /// <summary>Inc-VAT unit price. ⚠ Decimal here because that is what the v2 catalogue and the
        /// legacy model both carried; the BASKET's own money is integer pence (step 11b), and
        /// `BasketItem` converts on the way in.</summary>
        public decimal Price { get; set; }

        /// <summary>Ex-VAT unit price. ⚠ The PAIR is what makes the VAT band derivable — see
        /// `VatRateHistory.Assess`, which reads inc and ex together and refuses to guess from one.</summary>
        public decimal ExPrice { get; set; }

        /// <summary>
        /// The VAT band's NAME, for the receipt's tax column.
        ///
        /// ⚠ A STRING, WHERE THE LEGACY MODEL HAD A `TaxModel` NAVIGATION PROPERTY. The only thing
        /// ever read off it was `.Name` — so carrying the entity meant an EF include, a nullable
        /// reference to dereference, and a second legacy type on the basket, all for one label.
        /// </summary>
        public string VatName { get; set; }
    }
}
