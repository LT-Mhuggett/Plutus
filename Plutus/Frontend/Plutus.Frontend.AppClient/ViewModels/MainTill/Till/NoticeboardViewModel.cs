using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Services.Notices;

namespace Plutus.Frontend.AppClient.ViewModels.MainTill.Till
{
    /// <summary>One row on the till's noticeboard, flattened for binding.</summary>
    ///
    /// ⚠⚠ FLATTENED ON PURPOSE. MAUI bindings fail **silently** — a binding to a property that is not
    /// there renders blank and never throws — so binding straight to `PickNoteDto`/`AnnouncementDto`
    /// puts two wire contracts one rename away from a blank banner nobody notices. One row shape, with
    /// the text already assembled, is the shape the XAML can only get right.
    public sealed class NoticeRow
    {
        public Guid Id { get; init; }
        public string Headline { get; init; } = "";
        public string Detail { get; init; } = "";

        /// <summary>⚠ Pick notes can be acknowledged; announcements cannot, and the button is hidden
        /// rather than disabled. A greyed-out button invites somebody to keep pressing it.</summary>
        public bool CanAck { get; init; }

        public Color Accent { get; init; } =
            Helpers.Extensions.XAML.MaterialIconGlyphConverter.ThemeColour("ThemeUnknown", Colors.SlateGray);
    }

    /// <summary>
    /// The banner at the top of the till: pick-from-floor notes and platform announcements (WP5b).
    ///
    /// ⚠ It reads <see cref="Noticeboard"/>, which the 60s cadence fills. It does NOT poll — a screen
    /// with its own timer is how four timers become five, and `TillCadence` exists to stop that.
    /// </summary>
    public sealed class NoticeboardViewModel : BaseViewModel
    {
        public NoticeboardViewModel()
        {
            AckCommand = new Command<NoticeRow>(async row => await AckAsync(row));
            Redraw();
        }

        public ObservableCollection<NoticeRow> Rows { get; } = new();

        /// <summary>⚠ The whole banner collapses when there is nothing to say. A till that keeps a
        /// permanent empty strip at the top has lost the pixels for good, and an operator stops
        /// seeing a region that never changes.</summary>
        public bool HasAnything => Rows.Count > 0;

        /// <summary>⚠ SHOWN ONLY ALONGSIDE SOMETHING. "This may be out of date" over an empty board
        /// is a warning about nothing, every time a shop's line drops.</summary>
        public bool ShowStale => _stale && Rows.Count > 0;

        private bool _stale;

        public ICommand AckCommand { get; }

        /// <summary>
        /// Rebuild the rows from the board. ⚠ MUST be called on the UI thread — it writes an
        /// `ObservableCollection` a `CollectionView` is bound to, and doing that off the UI thread is
        /// the kind of fault that works in testing and throws on a shop floor.
        /// </summary>
        public void Redraw()
        {
            var board = Noticeboard.State;
            _stale = board.Stale;

            Rows.Clear();

            // ⚠ ANNOUNCEMENTS FIRST. They are platform-wide and explain why something else is
            // failing; a pick note is a job, and a job can wait behind the reason the card machine
            // is down.
            foreach (var a in board.Announcements)
            {
                var incident = string.Equals(a.Severity?.Trim(), "Incident", StringComparison.OrdinalIgnoreCase);

                Rows.Add(new NoticeRow
                {
                    Id = a.Id,

                    // ⚠ The same glyphs the web till uses (binding default 10 — when in doubt, match
                    // it). Two tills in one shop showing the same incident differently is how staff
                    // learn to treat one of them as unreliable.
                    Headline = $"{(incident ? "⛔" : "🛠")} {a.Title}".Trim(),
                    Detail = a.Body ?? "",
                    CanAck = false,

                    // ⚠ Colour is a HINT, never the message. An unrecognised severity still shows
                    // (see `NoticesClient.ShowsOnATill`) and simply gets the maintenance accent — it
                    // must not be dropped for want of a colour it has no entry for.
                    Accent = incident
                        ? Helpers.Extensions.XAML.MaterialIconGlyphConverter.ThemeColour("ThemeDanger", Colors.Firebrick)
                        : Helpers.Extensions.XAML.MaterialIconGlyphConverter.ThemeColour("ThemeWarn", Colors.DarkOrange),
                });
            }

            foreach (var n in board.PickNotes)
            {
                Rows.Add(new NoticeRow
                {
                    Id = n.Id,
                    Headline = "🛒 Pick from the shop floor",

                    // ⚠ The order number is the fallback, not nothing. A note with no message is
                    // still a job somebody has to do, and "" would render an empty blue bar.
                    Detail = string.IsNullOrWhiteSpace(n.Message)
                        ? $"Web order {n.WooOrderId}"
                        : n.Message,
                    CanAck = true,
                    Accent = Helpers.Extensions.XAML.MaterialIconGlyphConverter.ThemeColour("ThemeAccent", Colors.SteelBlue),
                });
            }

            OnPropertyChanged(nameof(HasAnything));
            OnPropertyChanged(nameof(ShowStale));
        }

        /// <summary>
        /// ⚠ AN ACK NEEDS THE NETWORK and says so when it fails. `NoticesClient.AckAsync` explains
        /// why it is not queued: an ack claims a human took stock off a shelf, and it has to reach
        /// the shop waiting to ship the order.
        /// </summary>
        private async System.Threading.Tasks.Task AckAsync(NoticeRow row)
        {
            if (row is null || !row.CanAck) return;

            if (await Noticeboard.AckAsync(row.Id).ConfigureAwait(false))
            {
                MainThread.BeginInvokeOnMainThread(Redraw);
                return;
            }

            // ⚠ SAY SO. A silent no-op leaves the note on screen and teaches the operator the button
            // does nothing — and the note reappearing on the next beat looks like the same bug twice.
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (Application.Current?.MainPage is Page page)
                {
                    await page.DisplayAlert("Hmm".Translate(),
                        "Plutus couldn't record that. The note stays until it gets through — "
                        + "try again when the connection is back.",
                        "OK".Translate());
                }
            });
        }
    }
}
