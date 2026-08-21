using System;
using System.Threading.Tasks;
using System.Globalization;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Plutus.Contracts.Client;

namespace Plutus.Frontend.AppClient.Views.CustomViews
{
    /// <summary>
    /// One sale, opened from a report — **the drill-down** (§5c item 5).
    ///
    /// ⚠⚠ **THIS IS WHAT TURNS A REPORT INTO AN ANSWER.** Matt, 2026-08-18, on MAUI's reports: the
    /// set did not match the web till *and drill-down was absent entirely*. A takings figure tells a
    /// manager the day is £40 light; only the sale behind a row tells them why — which lines, which
    /// operator, how it was paid, what has already been given back.
    ///
    /// ⚠ BUILT IN CODE, like every other table on this till. MAUI bindings fail SILENTLY — a binding
    /// to a property that no longer exists renders BLANK and never throws — and every figure on this
    /// dialog is somebody's money. Built in code, a typo is a compile error. (`table-standard.md`.)
    ///
    /// ⚠ A `ScrollView` WITH THE CAP ON THE SCROLLER, copied from `InputAlert` rather than invented:
    /// capping the inner stack caps the CONTENT, leaving the scroller nothing to scroll and clipping
    /// exactly as before — which is how the item editor lost five of its eight fields and was
    /// reported as *"I can ONLY change the tax"* (2026-08-11). A sale with thirty lines is ordinary,
    /// so this dialog scrolls or it is useless.
    ///
    /// ⚠ READ-ONLY, deliberately. Refunding is `pos.refund` and belongs on the till screen against a
    /// basket, with the ceiling and the cross-till cap applied. A "refund this" button here would be
    /// a second door to the money with none of that behind it.
    /// </summary>
    public class SaleDetailAlert : ContentView
    {
        private readonly VerticalStackLayout _main = new() { Padding = 10, Spacing = 4 };
        private readonly ScrollView _scroller;

        /// <summary>The web till's exact wording (`SaleDetailDialog`), so an operator who moves
        /// between tills mid-shift reads the same button on both.</summary>
        private const string PrintLabel = "Print copy receipt";

        /// <summary>Raised when the operator closes it — wired to the ✕ AND to the Close button, so
        /// both exits behave identically (till-design **D4**).</summary>
        public event EventHandler CloseRequested;

        public SaleDetailAlert(SaleDto sale, string tillLabel, Func<Task> printCopy = null)
        {
            // ⚠⚠ A SOLID BACKGROUND AND A CENTRED BOX — WITHOUT BOTH, THIS DIALOG IS TRANSPARENT.
            // Reported by Matt on the first hand-run of §G41 (2026-08-18): *"When I open a sales day in
            // reports. I get a transparent screen"* — the sale's text drew straight over the report
            // table behind it, unreadable and full-window wide.
            //
            // ⚠ `AlertDialogBase` supplies only the 40%-black scrim; the CONTENT's own background is
            // the dialog. `InputAlert.xaml` sets `BackgroundColor="White"` on **both** its ScrollView
            // and its inner stack, plus Center/Center options, and it does that because a `ContentView`
            // defaults to transparent and stretches. Copied here rather than re-derived — see
            // `repo-runbook.md` "copy these shapes, don't invent".
            //
            // ⚠ `ThemeSurface`, NOT a hard-coded white — and only because the ink moved with it in the
            // same build. `Styles.xaml` now gives every `Label` its colour from `ThemeInk`, so this
            // dialog's text and its background come from one scheme. Pairing is the whole rule: a
            // themed surface under default-coloured ink is the unreadable-label fault (1.74.0).
            //
            // ⚠ `SetDynamicResource`, not a fetched value: a colour read once here would be frozen at
            // construction and would not follow a scheme applied afterwards.
            _scroller = new ScrollView
            {
                Content = _main,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            };

            _scroller.SetDynamicResource(VisualElement.BackgroundColorProperty, "ThemeSurface");
            _main.SetDynamicResource(VisualElement.BackgroundColorProperty, "ThemeSurface");

            // ⚠ A ✕ ON EVERY BOX — D4, and the shared helper rather than a hand-rolled glyph so this
            // one cannot drift from the other twenty-four.
            _main.Children.Add(global::CustomViews.DialogHeader.For(
                "Sale", (_, e) => CloseRequested?.Invoke(this, e)));

            if (sale is null)
            {
                _main.Children.Add(Muted("That sale couldn't be read."));
                Content = _scroller;
                return;
            }

            // ── what and when ──────────────────────────────────────────────────────────────────
            _main.Children.Add(Fact("When",
                sale.OccurredAtUtc.ToLocalTime().ToString("dddd d MMMM yyyy, HH:mm", CultureInfo.CurrentCulture)));

            // ⚠ THE BUSINESS DAY IS NOT THE CALENDAR DAY and is shown separately. A sale rung up at
            // 00:30 belongs to the previous trading day, and the Z-read it falls under is the only
            // thing that reconciles.
            if (!string.IsNullOrWhiteSpace(sale.BusinessDay))
                _main.Children.Add(Fact("Business day", sale.BusinessDay));

            if (!string.IsNullOrWhiteSpace(tillLabel))
                _main.Children.Add(Fact("Till", tillLabel));

            // ⚠ "(not recorded)" rather than a blank. A blank operator reads as a screen fault; the
            // truth is that some imported legacy sales genuinely have nobody against them.
            _main.Children.Add(Fact("Operator",
                string.IsNullOrWhiteSpace(sale.OperatorName) ? "(not recorded)" : sale.OperatorName));

            // ── the lines ──────────────────────────────────────────────────────────────────────
            _main.Children.Add(Heading("Lines"));

            if (sale.Lines is null || sale.Lines.Count == 0)
            {
                _main.Children.Add(Muted("No lines recorded against this sale."));
            }
            else
            {
                foreach (var line in sale.Lines)
                {
                    // ⚠ QTY × NAME, then the line's money — the shape of a receipt, because that is
                    // what the operator is holding when they look this up.
                    var name = string.IsNullOrWhiteSpace(line.ItemName)
                        ? (line.ItemIdOne ?? "(unknown item)")
                        : line.ItemName;

                    _main.Children.Add(Row(
                        $"{line.Qty} × {name}",
                        Gbp(line.LineGrossPence)));

                    // ⚠ A DISCOUNT IS SAID OUT LOUD on its own line. Finding W's lesson: an operator
                    // who cannot see that a discount was applied applies another one by hand.
                    if (line.DiscountPence != 0)
                        _main.Children.Add(Sub($"discount {Gbp(-Math.Abs(line.DiscountPence))}"));

                    // ⚠ The line's VAT and its RATE, because a VAT query is the second reason
                    // anybody opens a sale. Basis points → a percentage for display only: 2000 is 20%.
                    _main.Children.Add(Sub(
                        $"VAT {line.VatRateBp / 100m:0.##}% · {Gbp(line.VatAmountPence)}"));
                }
            }

            // ── how it was paid ────────────────────────────────────────────────────────────────
            _main.Children.Add(Heading("Paid"));

            if (sale.Tenders is null || sale.Tenders.Count == 0)
            {
                // ⚠ NOT AN ERROR. Legacy imported sales carry no tender breakdown at all, and saying
                // "not recorded" is the truth; inventing "Cash" would be a wrong statement about
                // money that a VAT return would then be reconciled against.
                _main.Children.Add(Muted("How this was paid was not recorded."));
            }
            else
            {
                foreach (var t in sale.Tenders)
                {
                    _main.Children.Add(Row(t.TenderType ?? "(unknown)", Gbp(t.AmountPence)));

                    // ⚠ CHANGE GIVEN is part of the answer to "what happened at this till". A £20
                    // note against a £13.99 sale is £20 tendered and £6.01 back, not £13.99 taken.
                    if (t.ChangePence != 0)
                        _main.Children.Add(Sub($"change {Gbp(t.ChangePence)}"));
                }
            }

            // ── what has already gone back ─────────────────────────────────────────────────────
            if (sale.Adjustments is { Count: > 0 })
            {
                _main.Children.Add(Heading("Already refunded or voided"));

                foreach (var a in sale.Adjustments)
                {
                    _main.Children.Add(Row(
                        string.IsNullOrWhiteSpace(a.Type) ? "Adjustment" : a.Type,
                        // ⚠ SHOWN AS A NEGATIVE, whatever sign the wire used. `AmountPence` is
                        // summed with `Math.Abs` by `AlreadyRefundedPence`, so the contract itself
                        // does not promise a sign — and money going back must never read as money
                        // coming in.
                        Gbp(-Math.Abs(a.AmountPence))));

                    // ⚠ THE REASON, which is the whole point of the audit trail. A refund with no
                    // recorded reason is the fault §F was written to catch.
                    if (!string.IsNullOrWhiteSpace(a.Reason))
                        _main.Children.Add(Sub(a.Reason));
                }

                _main.Children.Add(Row("Refunded so far", Gbp(-sale.AlreadyRefundedPence), bold: true));
            }

            // ── the totals ─────────────────────────────────────────────────────────────────────
            _main.Children.Add(Heading("Total"));
            _main.Children.Add(Row("VAT", Gbp(sale.VatPence)));
            _main.Children.Add(Row("Gross", Gbp(sale.GrossPence), bold: true));

            // ⚠⚠ A REFUND IS ITSELF A SALE WITH A NEGATIVE GROSS, and it says so rather than leaving
            // a manager to work out why a "sale" is a minus figure. Offering a refund as something to
            // refund AGAINST cost £13.99 twice on 2026-08-10.
            if (sale.GrossPence < 0)
                _main.Children.Add(Muted(
                    "This is a refund — it is recorded as a sale with a negative total."));

            // ── the two exits ────────────────────────────────────────────────────────────────────
            //
            // ⚠⚠ **PRINT COPY RECEIPT LIVES HERE, AND ONLY HERE.** Matt, 2026-08-21, on the standalone
            // button the Reports tab briefly had: *"Why is there a button there in MAUI and not in the
            // webtill? Reprinting receipts needs to be done from reports and looking at the specific
            // sales in a day."*
            //
            // He is right, and the button he saw was a bad fix. Reprint had become unreachable when
            // the Statistics tab was dropped, and moving it to the Reports toolbar restored the
            // capability while inventing a MAUI-only control — a second, parallel way to find a sale
            // (a picker of "recent sales") sitting next to the report that already lists them. The web
            // till has never had one: it reprints from `SaleDetailDialog`, off a report row, which is
            // where an operator is already standing when a customer asks.
            //
            // ⚠ SAME LABEL AS THE WEB TILL ("Print copy receipt"), under the 2026-08-19 look-and-feel
            // ruling, and the same order — Close, then print.
            var buttons = new Grid { ColumnSpacing = 6, Margin = new Thickness(0, 10, 0, 0) };
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

            // ⚠ A SECOND, OBVIOUS EXIT as well as the ✕. Matt on the price-adjust box: *"I know you
            // can click outside of the box to close it but its not intuative."*
            var close = new Button { Text = "Close" };
            close.Clicked += (_, e) => CloseRequested?.Invoke(this, e);
            buttons.Add(close, 0, 0);

            // ⚠ ONLY IF SOMEBODY CAN PRINT. `printCopy` is null on any caller that has no printer path
            // to offer, and a dead button is worse than no button — it tells an operator a copy is
            // available and then does nothing when the customer is waiting.
            if (printCopy is not null)
            {
                var print = new Button { Text = PrintLabel };
                buttons.Add(print, 1, 0);

                // ⚠⚠ NOT `async void`, and DISABLED WHILE IT RUNS. A reprint reaches the network and
                // then the agent, so it is slow enough to be pressed twice — and two presses is two
                // papers for one purchase, which is the shape of the double refund `ReceiptReprint`'s
                // header records. ⚠ The `finally` restores the button even when the print throws:
                // a stuck "Printing…" would leave the operator unable to try again.
                print.Clicked += async (_, _) =>
                {
                    if (!print.IsEnabled) return;

                    print.IsEnabled = false;
                    print.Text = "Printing…";
                    try
                    {
                        await printCopy();
                    }
                    catch (Exception ex)
                    {
                        Services.Analytics.CrashLog.Write("SaleDetailAlert.PrintCopy", ex);
                    }
                    finally
                    {
                        print.Text = PrintLabel;
                        print.IsEnabled = true;
                    }
                };
            }

            _main.Children.Add(buttons);

            Content = _scroller;
        }

        /// <summary>
        /// ⚠ WIDTH IS A REQUEST, HEIGHT IS A MAXIMUM, and the cap goes on the SCROLLER — copied from
        /// `InputAlert`, whose header records what capping the inner stack did to the item editor.
        /// </summary>
        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);

            if (width > 0) _main.WidthRequest = Math.Max(320, width / 2);
            if (height > 0) _scroller.MaximumHeightRequest = height * 0.9;
        }

        private static string Gbp(long pence) =>
            (pence / 100m).ToString("C2", CultureInfo.CurrentCulture);

        private static Label Heading(string text) => new()
        {
            Text = text,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(0, 10, 0, 2),
        };

        /// <summary>⚠ `Muted` uses OPACITY, never a hard-coded grey. Store Information shipped with
        /// every label light-grey on near-white and unreadable (1.74.0) — opacity follows whatever
        /// the theme's ink colour is.</summary>
        private static Label Muted(string text) => new() { Text = text, Opacity = 0.7 };

        private static Label Sub(string text) => new()
        {
            Text = text,
            Opacity = 0.7,
            Margin = new Thickness(16, 0, 0, 2),
        };

        /// <summary>A label and a figure, the figure right-aligned so a column of money can be read
        /// down — the same rule as every numeric column in `table-standard.md`.</summary>
        private static View Row(string label, string value, bool bold = false)
        {
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            grid.Add(new Label
            {
                Text = label,
                FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
            }, 0, 0);

            grid.Add(new Label
            {
                Text = value,
                HorizontalTextAlignment = TextAlignment.End,
                FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
            }, 1, 0);

            return grid;
        }

        private static View Fact(string label, string value) => Row(label, value);
    }
}
