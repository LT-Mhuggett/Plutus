using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Plutus.Contracts.Client;

namespace Plutus.Frontend.AppClient.Views.MainTill.Inventory.Items
{
    /// <summary>What the operator settled on. Null from <see cref="ShowAsync"/> means they cancelled.</summary>
    public sealed record EditItemResult(
        string Name, string Brand, string Desc,
        decimal Cost, decimal Price,
        int TaxId, Guid CatId, bool StockUntracked);

    /// <summary>
    /// Edit an item — everything on one screen.
    ///
    /// ⚠⚠ THIS EXISTS BECAUSE THE SAME COMPLAINT CAME BACK THREE TIMES. Matt, on 2026-08-10 and
    /// twice on 2026-08-11: *"Edit item, I can ONLY change the tax?"* and finally, with a
    /// screenshot of my own "step 1 of 4" title, *"I am STILL just seeing the tax."*
    ///
    /// The old flow opened three action sheets — tax band, category, stock — and only then a form.
    /// I fixed the form's contents (the fields were rendering as grey placeholder text, so they
    /// looked empty) and numbered the sheets. Both were improvements and neither was the problem:
    /// **being asked three questions before you are shown the thing you asked to edit is the wrong
    /// shape**, and a number on each question only says how much further there is to go.
    ///
    /// ⚠ A PAGE, NOT A DIALOG, because the controls are the point. `InputAlert` can host label +
    /// Entry pairs and nothing else, which is exactly why tax, category and stock had to become
    /// separate sheets in the first place. A ContentPage can hold a `Picker` and a `Switch`, so the
    /// choices sit inline where they belong — and the operator sees the tax band NEXT TO the price
    /// they are setting, which is the check that catches a zero-rated book priced as standard.
    ///
    /// ⚠ MODAL, so it cannot be navigated away from half-finished, and it returns through a
    /// TaskCompletionSource — the same awaitable shape the dialogs it replaces used, so the caller
    /// did not have to change how it thinks.
    /// </summary>
    public partial class EditItemPage : ContentPage
    {
        private readonly TaskCompletionSource<EditItemResult> _result = new();
        private readonly IReadOnlyList<TaxBandDto> _bands;
        private readonly IReadOnlyList<CategoryDto> _categories;
        private readonly NumberStyles _money = NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands;

        private EditItemPage(
            ItemDto current, string barcode,
            IReadOnlyList<TaxBandDto> bands, IReadOnlyList<CategoryDto> categories)
        {
            InitializeComponent();

            _bands = bands;
            _categories = categories;

            HeaderLabel.Text = current.Name ?? "Edit item";
            BarcodeLabel.Text = string.IsNullOrWhiteSpace(barcode) ? "—" : barcode;

            // ⚠ REAL VALUES, IN THE BOXES. The previous form put them in as `Placeholder`, so every
            // editable row looked blank and the only rows showing anything were the read-only ones.
            // A form that opens empty asks the operator to retype five fields to change one — and
            // the four they leave alone are the ones that get lost.
            NameEntry.Text = current.Name ?? "";
            // ⚠ "-" is what the WEB TILL writes for an unknown brand; shown blank so the same column
            // does not end up holding "-" from one till and "" from another.
            BrandEntry.Text = current.Brand == "-" ? "" : current.Brand ?? "";
            DescEntry.Text = current.Desc ?? "";
            CostEntry.Text = current.Cost.ToString("0.00", CultureInfo.CurrentCulture);
            PriceEntry.Text = current.Price.ToString("0.00", CultureInfo.CurrentCulture);

            TaxPicker.ItemsSource = bands
                .Select(b => Plutus.Client.Core.TaxBandLabel.For(b, b.IdOne)).ToList();
            TaxPicker.SelectedIndex = IndexOfBand(current.TaxId);

            CategoryPicker.ItemsSource = categories
                .Select(c => c.Name ?? c.IdOne.ToString("D")).ToList();
            CategoryPicker.SelectedIndex = IndexOfCategory(current.CatId);

            // ⚠ The SWITCH reads the opposite way round to the stored flag — "count this item's
            // stock" ON means `StockUntracked` FALSE. Phrasing it as the thing the shopkeeper wants
            // rather than as the database's negative is worth the one inversion; getting it
            // backwards would silently start or stop counting stock across the estate.
            StockSwitch.IsToggled = !current.StockUntracked;
            StockSwitch.Toggled += (_, __) => UpdateStockHint();
            UpdateStockHint();
        }

        /// <summary>⚠ Keeps the item's CURRENT band selected even when the published list no longer
        /// offers it — falling back to index 0 would silently re-rate the item on save.</summary>
        private int IndexOfBand(int taxId)
        {
            var i = _bands.ToList().FindIndex(b => b.IdOne == taxId);
            return i >= 0 ? i : (_bands.Count > 0 ? 0 : -1);
        }

        private int IndexOfCategory(Guid catId)
        {
            var i = _categories.ToList().FindIndex(c => c.IdOne == catId);
            return i >= 0 ? i : (_categories.Count > 0 ? 0 : -1);
        }

        private void UpdateStockHint() =>
            StockHint.Text = StockSwitch.IsToggled
                ? "On hand is tracked and shown on the item list."
                : "Not counted — sells for ever, like a carrier bag.";

        /// <summary>
        /// Show the page and wait for the operator.
        ///
        /// ⚠ Returns null when they cancel, which the caller treats as "change nothing" — the same
        /// contract the action sheets had, so nothing downstream had to be rethought.
        /// </summary>
        public static async Task<EditItemResult> ShowAsync(
            ItemDto current, string barcode,
            IReadOnlyList<TaxBandDto> bands, IReadOnlyList<CategoryDto> categories)
        {
            var page = new EditItemPage(current, barcode, bands, categories);
            await Application.Current.MainPage.Navigation.PushModalAsync(page);
            return await page._result.Task;
        }

        private async void OnCancel(object sender, EventArgs e)
        {
            // ⚠ The page comes down FIRST and the result is set second. Completing the task while
            // the page is still up hands control back to a caller that may open its own dialog on
            // top of a page that is mid-teardown — which is exactly the COMException that made an
            // over-payment say "Something went wrong" (finding D, 2026-08-11).
            await Navigation.PopModalAsync();
            _result.TrySetResult(null);
        }

        private async void OnSave(object sender, EventArgs e)
        {
            var name = (NameEntry.Text ?? "").Trim();
            if (name.Length == 0) { Fail("An item needs a name."); return; }

            if (!decimal.TryParse((PriceEntry.Text ?? "").Trim(), _money, CultureInfo.CurrentCulture, out var price)
                || price < 0)
            { Fail("That price didn't look like a number."); return; }

            // ⚠ Cost is forgiving where price is not: it is not the operator's field, it comes from a
            // supplier, and a blank one must not block a price change. Blank or nonsense keeps what
            // was there — the entry was pre-filled with it, so "unchanged" is what they saw.
            if (!decimal.TryParse((CostEntry.Text ?? "").Trim(), _money, CultureInfo.CurrentCulture, out var cost)
                || cost < 0)
                cost = 0m;

            if (TaxPicker.SelectedIndex < 0 || TaxPicker.SelectedIndex >= _bands.Count)
            { Fail("Pick a tax band."); return; }

            if (CategoryPicker.SelectedIndex < 0 || CategoryPicker.SelectedIndex >= _categories.Count)
            { Fail("Pick a category."); return; }

            var result = new EditItemResult(
                Name: name,
                Brand: (BrandEntry.Text ?? "").Trim(),
                Desc: (DescEntry.Text ?? "").Trim(),
                Cost: cost,
                Price: price,
                TaxId: _bands[TaxPicker.SelectedIndex].IdOne,
                CatId: _categories[CategoryPicker.SelectedIndex].IdOne,
                // ⚠ Inverted here, once — see the constructor.
                StockUntracked: !StockSwitch.IsToggled);

            await Navigation.PopModalAsync();
            _result.TrySetResult(result);
        }

        /// <summary>⚠ ON THE PAGE, not in a dialog on top of it. A refusal that pops another modal
        /// over a form is how the operator loses sight of the field that is wrong.</summary>
        private void Fail(string message)
        {
            ErrorLabel.Text = message;
            ErrorLabel.IsVisible = true;
        }

        /// <summary>⚠ Hardware back / Esc must not leave the caller awaiting for ever. Treated as
        /// Cancel, and `TrySet` because Save may already have completed it.</summary>
        protected override bool OnBackButtonPressed()
        {
            _result.TrySetResult(null);
            return base.OnBackButtonPressed();
        }
    }
}
