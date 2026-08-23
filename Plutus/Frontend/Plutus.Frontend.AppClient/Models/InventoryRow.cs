using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Plutus.Frontend.AppClient.Models
{
    /// <summary>
    /// **One line on the inventory browse list.**
    ///
    /// ⚠⚠ THIS IS THE RESHAPE THE LEGACY MODEL WAS WAITING FOR. `ItemModel.StockDisplay` carried its
    /// own apology: *"a compromise, made because this model goes when the inventory screen is
    /// reshaped (cutover step 25). ⚠ It must NEVER be written back."* A display string, marked
    /// `[NotMapped]`, bolted onto an EF entity so a list could bind to it. That is now here, on a type
    /// that has no database behind it to be written back to.
    ///
    /// ⚠ A BROWSE ROW IS NOT A SALE LINE, which is why this is separate from `TillItem`. The basket
    /// carries what is being charged and must never hold UI state; this list carries what is being
    /// looked at and must. Merging them would put a mutable, notifying display field on every basket
    /// line — and put price arithmetic on a screen that only browses.
    ///
    /// ⚠ THE PRICES HERE ARE FOR DISPLAY ONLY. `EffectivePricePairAsync` decides what a line actually
    /// costs at basket-add, and it is the only thing allowed to — a schedule, a member tier or a
    /// manual override can all move the number between browsing and selling.
    /// </summary>
    public sealed class InventoryRow : INotifyPropertyChanged
    {
        private string _stockDisplay;

        /// <summary>The item code — `CatalogueItem.IdOne`. ⚠ What the basket and every screen key on.</summary>
        public string Id { get; set; }

        public string Name { get; set; }

        /// <summary>⚠ SEARCHED, not shown. `ItemSearch` matches on it, so without it this list and
        /// the scan box answered the same query differently — schema v5 exists for that.</summary>
        public string Brand { get; set; }

        public string Desc { get; set; }

        /// <summary>Inc-VAT price, for display. See the class remarks.</summary>
        public decimal Price { get; set; }

        /// <summary>Ex-VAT price, derived from the rate for display. See the class remarks.</summary>
        public decimal ExPrice { get; set; }

        /// <summary>The VAT band's name, for the list's tax column.</summary>
        public string VatName { get; set; }

        /// <summary>
        /// "∞", "—", or a counted level once the server has answered.
        ///
        /// ⚠⚠ IT MUST RAISE PropertyChanged. The stock levels arrive AFTER the rows are drawn — a
        /// second call, on a background thread — and the list is bound to this. A plain property here
        /// would leave every row reading "—" for ever, which looks exactly like a shop with no stock.
        /// </summary>
        public string StockDisplay
        {
            get => _stockDisplay;
            set
            {
                if (_stockDisplay == value) return;
                _stockDisplay = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StockDisplay)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
