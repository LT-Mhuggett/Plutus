using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Plutus.Client.Core;
using Plutus.Frontend.AppClient.Helpers.Extensions;

namespace Plutus.Frontend.AppClient.Views.CustomViews
{
    /// <summary>
    /// ONE-SCREEN CHECKOUT — every method on screen at once, with a live Paid / Remaining / Change.
    /// §5c item 2.
    ///
    /// ⚠⚠ MATT, 2026-08-19: *"I need the functionality and look and feel to be the same across both
    /// tills. So if a user swaps between the two, it doesnt matter and they would understand how to use
    /// it."* That ruling **superseded** 2026-08-17's *"parity in FUNCTIONALITY, not in how the functions
    /// operate"*, which was the only thing justifying MAUI's sequential tender prompts against the web
    /// till's one screen. This is the screen.
    ///
    /// ⚠⚠ **IT IS `CheckoutDialog.tsx`, DELIBERATELY** — same rows, same "rest" buttons, same
    /// Paid/Remaining/Change block, same refusal sentences, the gift card behind a *"Pay with a gift
    /// card"* button below the tenders (Matt, 2026-08-19: *"There is no point showing it all, unless you
    /// have a card"*). An operator moving between tills mid-shift should not notice which one they are
    /// standing at.
    ///
    /// ⚠⚠ **AND IT MAKES THE DOUBLE-TAKE BUG IMPOSSIBLE BY CONSTRUCTION.** The sequential loop asked for
    /// a method, then an amount, over and over — so a capped tender could be picked twice and take its
    /// cap each time (the money defect of 2026-08-19, fixed there with an accumulating guard). Here each
    /// method has exactly ONE box, so there is no second pass to take. The guard in `TenderLoop` stays
    /// for as long as the loop does; this removes the shape of the bug rather than defending against it.
    ///
    /// ⚠⚠ **ALL ARITHMETIC IS `Client.Core.TenderSettlement`**, the C2 twin of the web till's
    /// `tendering.ts`, mutation-checked on both sides. **Nothing here computes money.** A viewmodel that
    /// re-derived "remaining" would be a third copy, and two tills that disagree by a penny disagree on
    /// every VAT return afterwards.
    ///
    /// ⚠ BUILT IN CODE, not XAML, like every other money surface on this till: MAUI bindings fail
    /// SILENTLY, and a blank cell here is somebody's money.
    ///
    /// ⚠ D4: a visible ✕, Escape cancels, and **the caller must handle "backed out"** — this raises
    /// <see cref="CloseRequested"/> for every exit and hands back <see langword="null"/> payments.
    /// </summary>
    public class CheckoutAlert : ContentView
    {
        /// <summary>One tender row on screen.</summary>
        public sealed class Row
        {
            public int PayId { get; init; }
            public string Name { get; init; }
            public bool IsChangeable { get; init; }

            /// <summary>
            /// The most this row may take, or null for uncapped.
            ///
            /// ⚠ Three different things arrive here and they all mean "this row cannot take more than
            /// that": a customer's credit balance, a gift card's remaining value, and — on a refund —
            /// what this tender actually took on the original sale (finding Y).
            /// </summary>
            public long? CapPence { get; init; }

            /// <summary>Shown after the name, e.g. "(up to £5.00)" — the web till says this too.</summary>
            public string Hint { get; init; }
        }

        private readonly Grid _root = new() { Padding = 10, RowSpacing = 6 };
        private readonly List<Row> _rows;
        private readonly Dictionary<int, Entry> _boxes = new();
        private readonly Label _note = new() { FontSize = 12 };

        /// <summary>
        /// WP14 — which machine the cashier should reach for. ⚠ Its own label, never merged into
        /// <see cref="_note"/>: that one is rewritten every time the card fee goes on or comes off
        /// (<see cref="SetTotal"/>), so sharing it would make the gateway line flicker away the
        /// instant somebody typed in the card row — which is the exact moment it is being read.
        /// </summary>
        private readonly Label _cardHint = new() { FontSize = 12 };

        /// <summary>
        /// What the basket owes RIGHT NOW — not readonly, because the card fee can change it while the
        /// screen is open. See <see cref="SetTotal"/>.
        /// </summary>
        private long _totalPence;

        /// <summary>Whether the card row currently holds an amount — see <see cref="CardTenderedChanged"/>.</summary>
        private bool _cardTendered;

        /// <summary>The heading's own label, so <see cref="SetTotal"/> can restate the figure.</summary>
        private readonly Label _headerTitle;

        private readonly Label _paid = new() { FontSize = 14 };
        private readonly Label _remaining = new() { FontSize = 14, FontAttributes = FontAttributes.Bold };
        private readonly Label _change = new() { FontSize = 14, FontAttributes = FontAttributes.Bold };
        private readonly Label _refusal = new() { FontSize = 12 };
        private readonly Button _complete = new();

        /// <summary>Closed with no sale — wired to the ✕ and to Cancel, so both exits behave alike (D4).</summary>
        public event EventHandler CloseRequested;

        /// <summary>The operator pressed Complete and the screen balances.</summary>
        public event EventHandler CompleteRequested;

        /// <summary>Open the gift-card box — the caller scans, checks the balance and reopens this.</summary>
        public event EventHandler GiftCardRequested;

        /// <summary>
        /// The card row went from empty to holding an amount, or back — **the card fee follows this**.
        ///
        /// ⚠⚠ WHY IT IS AN EVENT AND NOT COMPUTED HERE: the fee is a real BASKET LINE (so it is taxed,
        /// printed and reported like anything else), and the basket is bound to the till screen. A view
        /// must not reach into it. So this says *"a card is now being used"*, the caller adds or removes
        /// the fee line and calls <see cref="SetTotal"/>, and this screen restates the figures.
        ///
        /// ⚠ It fires only on the EDGE, not on every keystroke — the fee is charged once for using a
        /// card, and a split card payment must not be charged twice (the rule `TenderLoop` states as
        /// "at most once").
        /// </summary>
        public event EventHandler<bool> CardTenderedChanged;

        /// <summary>
        /// The basket owes a different amount now — the card fee went on or came off.
        ///
        /// ⚠ MUST BE CALLED ON THE UI THREAD. It rewrites the labels a person is reading.
        /// ⚠ It restates the heading too, because *"Checkout — £13.65"* with a £0.35 fee applied and a
        /// heading still saying £13.30 is the surcharge complaint in its purest form.
        /// </summary>
        public void SetTotal(long totalPence, string note)
        {
            _totalPence = totalPence;

            _note.Text = note ?? string.Empty;
            _note.IsVisible = !string.IsNullOrWhiteSpace(note);

            if (_headerTitle != null) _headerTitle.Text = Heading();

            Recalculate();
        }

        /// <param name="totalPence">⚠ NEGATIVE MEANS REFUND, matching the web till and the wire.</param>
        /// <param name="rows">The methods to offer, in the order they should appear.</param>
        /// <param name="note">Optional line above the rows — the card-fee itemisation, the refund
        /// explanation, the gateway hint. ⚠ Said BEFORE the sale completes, never after.</param>
        /// <param name="offerGiftCard">Whether to show the "Pay with a gift card" button below the
        /// tenders. ⚠ False while a card is already attached, while the basket SELLS a card, and on a
        /// refund — the same three cases the web till suppresses it in.</param>
        /// <param name="card">WP14 — how this tenant takes card payments. ⚠ Never null: the caller
        /// resolves it through <see cref="PaymentGateway"/>, which answers standalone for a failed
        /// lookup, an offline till and a tenant that has chosen no provider alike.</param>
        public CheckoutAlert(
            long totalPence, IReadOnlyList<Row> rows, string note, bool offerGiftCard,
            CardPaymentDisplay card)
        {
            _totalPence = totalPence;
            _rows = rows?.ToList() ?? new List<Row>();

            _root.SetDynamicResource(VisualElement.BackgroundColorProperty, "ThemeSurface");

            var stack = new VerticalStackLayout { Spacing = 6 };

            stack.Children.Add(global::CustomViews.DialogHeader.For(
                Heading(), (_, e) => CloseRequested?.Invoke(this, e), out _headerTitle));

            // ⚠ A LABEL THAT STAYS, not a one-off line: the card fee appears and disappears as the card
            // row is filled and cleared, and it must say so on the screen the operator is reading.
            _note.SetDynamicResource(Label.TextColorProperty, "ThemeInkMuted");
            _note.Text = note ?? string.Empty;
            _note.IsVisible = !string.IsNullOrWhiteSpace(note);
            stack.Children.Add(_note);

            // ⚠ WP14, and it sits WHERE THE WEB TILL PUTS IT — after the fee/refund notes, above the
            // tender rows. An operator moving between tills mid-shift reads the same sentence in the
            // same place (Matt, 2026-08-19). ⚠ Always shown, including the standalone default: the
            // sentence a cashier needs most is the one that says nobody is going to drive the
            // terminal for them.
            _cardHint.SetDynamicResource(Label.TextColorProperty, "ThemeInkMuted");
            _cardHint.FormattedText = Formatted(CardHintFor(card, refunding: totalPence < 0));
            stack.Children.Add(_cardHint);

            foreach (var row in _rows) stack.Children.Add(TenderRow(row));

            // ⚠⚠ BELOW THE TENDERS AND CLOSED BY DEFAULT — Matt, 2026-08-19: *"can the Giftcare, go
            // below and say 'Pay with Gift Card' button, which then pops the box to scan. There is no
            // point showing it all, unless you ahve a card."* Most sales involve no gift card, and a
            // permanently-open scan box is read past on every single sale.
            if (offerGiftCard)
            {
                var giftCard = new Button { Text = "🎁 Pay with a gift card", Margin = new Thickness(0, 4, 0, 0) };
                giftCard.Clicked += (_, e) => GiftCardRequested?.Invoke(this, e);
                stack.Children.Add(giftCard);
            }

            stack.Children.Add(Divider());
            stack.Children.Add(_paid);
            stack.Children.Add(_remaining);
            stack.Children.Add(_change);
            stack.Children.Add(_refusal);

            _refusal.SetDynamicResource(Label.TextColorProperty, "ThemeInkMuted");
            _paid.SetDynamicResource(Label.TextColorProperty, "ThemeInkMuted");

            var cancel = new Button { Text = "Cancel".Translate() };
            cancel.Clicked += (_, e) => CloseRequested?.Invoke(this, e);

            _complete.Clicked += (_, e) => CompleteRequested?.Invoke(this, e);

            var actions = new Grid { ColumnSpacing = 6, Margin = new Thickness(0, 8, 0, 0) };
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            actions.Add(cancel, 0, 0);
            actions.Add(_complete, 1, 0);
            stack.Children.Add(actions);

            _root.Add(stack, 0, 0);
            Content = _root;

            Recalculate();
        }

        /// <summary>
        /// What the operator typed, per pay method.
        ///
        /// ⚠ RAW TEXT, not pence — the caller hands it straight to `TenderSettlement.ParseAmounts`, so
        /// there is exactly one place that decides what "4.5" means.
        /// </summary>
        /// <summary>What the basket owes right now — ⚠ changes with the card fee, see <see cref="SetTotal"/>.</summary>
        public long TotalPence => _totalPence;

        /// <summary>The rows on screen, so the host can settle against the same list it drew.</summary>
        public IReadOnlyList<Row> Rows => _rows;

        public IReadOnlyDictionary<int, string> Amounts =>
            _boxes.ToDictionary(kv => kv.Key, kv => kv.Value.Text ?? string.Empty);

        /// <summary>Can the sale actually be completed right now? ⚠ The button's own answer, not a second one.</summary>
        public bool CanComplete => _complete.IsEnabled;

        // ── WP14: what the screen says about cards ────────────────────────────

        /// <summary>
        /// One run of the card sentence, and whether it is bold.
        ///
        /// ⚠⚠ A PLAIN RECORD AND NOT A `Span`, DELIBERATELY. `FormattedString` derives from
        /// `Element`, so merely constructing one spins up the MAUI handler registry and throws a
        /// `COMException` outside a UI host — which is to say the sentence would have been
        /// untestable, on a screen whose whole family of defects (2026-08-10, ×3) shipped because
        /// the checkout could only be checked by a person clicking it. The view turns these into
        /// spans; the decision about what to say never touches a MAUI type.
        /// </summary>
        public sealed record HintSpan(string Text, bool Bold);

        /// <summary>Turn the composed sentence into something a `Label` can show. ⚠ The only part of
        /// WP14 that needs a UI host, and it decides nothing.</summary>
        private static FormattedString Formatted(IReadOnlyList<HintSpan> spans)
        {
            var text = new FormattedString();
            foreach (var s in spans)
            {
                text.Spans.Add(new Span
                {
                    Text = s.Text,
                    FontAttributes = s.Bold ? FontAttributes.Bold : FontAttributes.None,
                });
            }
            return text;
        }

        /// <summary>
        /// The card-payment sentence, WORD FOR WORD the web till's `CheckoutDialog.tsx`.
        ///
        /// ⚠⚠ THE DECISION IS NOT MADE HERE. Which of the three cases applies is
        /// <see cref="PaymentGateway.Resolve"/>'s answer, already taken before this is called — this
        /// only turns a <see cref="CardPaymentDisplay"/> into English. That split is the house rule
        /// (C1): the rule returns a verdict, the client composes the sentence, so nothing in
        /// `Client.Core` carries a translatable string.
        ///
        /// ⚠⚠ AND THE RULE IS THE THING THAT MUST NOT DRIFT, not the wording. `PaymentGateway`'s own
        /// header says it mirrors the web till, *"where the same three cases are decided inline"* —
        /// so today the decision genuinely exists twice, in two languages, with nothing pinning the
        /// copies. **C2 row added 2026-08-21.** A till that reads "waiting for the terminal" while
        /// the other says "use the terminal by hand" is a queue and a confused cashier.
        ///
        /// ⚠ Bold on the provider name, because the web till bolds it — and the provider name is the
        /// only part of this line that differs between two shops.
        ///
        /// ⚠ Static and free of any MAUI construction so it can be tested without a UI host, which
        /// is the whole reason `Settle` was pulled out of the screen too.
        /// </summary>
        /// <param name="card">⚠ Null is tolerated and reads as standalone — the caller should never
        /// pass it, but a blank line here would be a screen that says nothing about cards at all.</param>
        /// <param name="refunding">Changes only the verb: money going back is not "approved".</param>
        public static IReadOnlyList<HintSpan> CardHintFor(CardPaymentDisplay card, bool refunding)
        {
            // ⚠ "No provider chosen" is the ONLY case that is standalone AND not pending — a named
            // provider without an integration is standalone too, and it must say the provider's name.
            // ⚠ Tested by shape, not by comparing `Label` against `StandaloneLabel`: a tenant is free
            // to call their provider anything, and one who typed "your card terminal" as a label
            // would silently take the wrong branch.
            if (card is null || (card.Flow == CardFlow.Standalone && !card.PendingIntegration))
            {
                // ⚠ The tenant has chosen no provider. Say what to DO, not what is unconfigured — a
                // cashier does not care what the till failed to look up.
                return new[]
                {
                    new HintSpan(refunding
                        ? "💳 Card: refund on the chip & pin terminal, confirm it went through, then complete."
                        : "💳 Card: take payment on the chip & pin terminal, confirm it's approved, then complete.",
                        Bold: false),
                };
            }

            var spans = new List<HintSpan>
            {
                new("💳 Card via ", Bold: false),
                new(card.Label, Bold: true),
            };

            // ⚠⚠ THE CASE THAT EARNS THIS WHOLE FEATURE. A named provider with no wired integration
            // is STILL the standalone flow — so a cashier who reads only "Card via Worldpay" waits
            // for a terminal prompt that is never coming, with a customer in front of them.
            if (card.PendingIntegration)
            {
                spans.Add(new HintSpan(
                    " (integration pending — use the terminal and confirm approval as usual)", Bold: false));
            }

            spans.Add(new HintSpan(".", Bold: false));
            return spans;
        }

        // ── rows ──────────────────────────────────────────────────────────────

        private View TenderRow(Row row)
        {
            var grid = new Grid { ColumnSpacing = 6 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

            var name = new Label { VerticalOptions = LayoutOptions.Center };
            name.SetDynamicResource(Label.TextColorProperty, "ThemeInk");

            // ⚠ SAY THE CEILING ON THE ROW, exactly as the web till does. Matt, 2026-08-19, about the
            // credit row: *"There is No point putting the full number in."* A number the redeem would
            // refuse should never be offered, and the operator should be able to see why.
            name.Text = string.IsNullOrWhiteSpace(row.Hint) ? row.Name : $"{row.Name}  {row.Hint}";

            var box = new Entry
            {
                Placeholder = "0.00",
                Keyboard = Keyboard.Numeric,
                HorizontalTextAlignment = TextAlignment.End,
            };
            // ⚠ Recalculated on every keystroke, which is what makes "Remaining" LIVE. The whole
            // complaint this screen answers was a till that took £2 and said nothing.
            box.TextChanged += (_, _) => Recalculate();
            _boxes[row.PayId] = box;

            // ⚠ "rest" fills THIS row from what the others hold — never a toggle, and capped at what
            // the method can actually give. `TenderSettlement.RestFor` owns both rules.
            var rest = new Button { Text = "rest", FontSize = 12, Padding = new Thickness(8, 2) };
            rest.Clicked += (_, _) => FillRest(row);

            grid.Add(name, 0, 0);
            grid.Add(rest, 1, 0);
            grid.Add(box, 2, 0);
            return grid;
        }

        private void FillRest(Row row)
        {
            var parsed = TenderSettlement.ParseAmounts(Amounts);

            // ⚠ An unreadable box means "rest" cannot know what the others hold, so it does nothing
            // rather than guessing — filling from a half-read screen would put a number in that the
            // operator did not choose and the arithmetic does not agree with.
            if (!parsed.Valid) return;

            var own = parsed.PerMethod.TryGetValue(row.PayId, out var mine) ? mine : 0;
            var rest = TenderSettlement.RestFor(Math.Abs(_totalPence), parsed.Paid, own, row.CapPence);

            _boxes[row.PayId].Text = (rest / 100m).ToString("0.00", CultureInfo.CurrentCulture);
        }

        // ── the live figures ──────────────────────────────────────────────────

        /// <summary>
        /// Re-read every box and restate what the screen owes.
        ///
        /// ⚠ EVERY FIGURE COMES FROM `TenderSettlement`. Nothing is computed here — see the class
        /// remarks. This method only decides what to SHOW.
        /// </summary>
        private void Recalculate()
        {
            var parsed = TenderSettlement.ParseAmounts(Amounts);
            var methods = _rows.Select(r => new TenderMethodRef(r.PayId, r.IsChangeable)).ToList();
            var s = TenderSettlement.Assess(_totalPence, parsed, methods);

            var refunding = s.Refunding;

            // ⚠⚠ THE CARD FEE FOLLOWS THE CARD ROW, and only on the EDGE. Raised before the labels are
            // written so the caller can add the fee line and call `SetTotal`, which comes back through
            // here with the new figure — an operator must never read a total that excludes a fee the
            // sale is about to charge.
            //
            // ⚠ `_cardTendered` is compared before it is assigned, so a split card payment (two
            // keystrokes in one card box) fires once. `TenderLoop` states the same rule for the
            // sequential flow: the fee applies AT MOST ONCE.
            //
            // ⚠ Never on a refund — a shop does not charge a fee to give money back.
            if (!refunding)
            {
                var cardNow = _rows
                    .Where(r => SharedKernel.Tenders.FromMethodName(r.Name) == SharedKernel.Tenders.Card)
                    .Any(r => parsed.Valid && parsed.PerMethod.TryGetValue(r.PayId, out var pence) && pence > 0);

                if (cardNow != _cardTendered)
                {
                    _cardTendered = cardNow;
                    CardTenderedChanged?.Invoke(this, cardNow);
                }
            }

            _paid.Text = parsed.Valid
                ? $"{(refunding ? "Refunding" : "Paid")}   {Gbp(s.Paid)}"
                : $"{(refunding ? "Refunding" : "Paid")}   —";

            _remaining.Text = s.Remaining > 0
                ? $"{(refunding ? "Still to refund" : "Remaining")}   {Gbp(s.Remaining)}"
                : string.Empty;
            _remaining.IsVisible = s.Remaining > 0;

            // ⚠ CHANGE AND "CHANGE NOT AVAILABLE" ARE THE SAME LINE, in the web till's words. An
            // overpay that cannot be given back is not a smaller kind of change, it is a refusal, and
            // the operator needs to read why before they open the drawer.
            if (s.Overpay > 0)
            {
                _change.IsVisible = true;
                _change.Text = s.ChangeOk
                    ? $"Change   {Gbp(s.Overpay)}"
                    : $"Change (not available on chosen methods)   {Gbp(s.Overpay)}";
                _change.SetDynamicResource(Label.TextColorProperty, s.ChangeOk ? "ThemeInk" : "ThemeDanger");
            }
            else
            {
                _change.IsVisible = false;
            }

            // ⚠⚠ SAY WHY, never just grey the button out. Matt, 2026-08-11, on this till: over-paying
            // by card said only *"Something went wrong"*. A disabled button with no sentence is the
            // same fault in a quieter form.
            var refusal = TenderSettlement.Refusal(s, parsed, Gbp);
            _refusal.Text = refusal ?? string.Empty;
            _refusal.IsVisible = refusal != null;

            _complete.IsEnabled = refusal == null;
            _complete.Text = refunding ? "Complete refund" : "Complete sale";
        }

        private string Heading() =>
            _totalPence < 0
                ? $"Refund — {Gbp(Math.Abs(_totalPence))}"
                : $"Checkout — {Gbp(_totalPence)}";

        /// <summary>⚠ The till's own culture, like every other figure on screen.</summary>
        private static string Gbp(long pence) => (pence / 100m).ToString("C2", CultureInfo.CurrentCulture);

        private static Label Muted(string text)
        {
            var label = new Label { Text = text, FontSize = 12 };
            label.SetDynamicResource(Label.TextColorProperty, "ThemeInkMuted");
            return label;
        }

        private static View Divider()
        {
            var line = new BoxView { HeightRequest = 1, Margin = new Thickness(0, 6, 0, 2) };
            line.SetDynamicResource(BoxView.ColorProperty, "ThemeLine");
            return line;
        }
    }
}
