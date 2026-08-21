using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using CustomViews.Structs;
using Plutus.Client.Core;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Helpers.Validators;
using Plutus.Frontend.AppClient.Services.Analytics;

namespace Plutus.Frontend.AppClient.Services.Support
{
    /// <summary>
    /// **Raise a ticket with Plutus, or read and answer one — OP4 / WP6.3.**
    ///
    /// ⚠⚠ EXTRACTED FROM `SettingsViewModel` ON 2026-08-21, because it now has TWO entry points. Matt:
    /// *"The help needs to be in the top corner of the MAUI till like the web till."* It is reached
    /// from the **❓ in the app bar** (`Controls.TillAppBar`) and from **Settings → Help**, and the web
    /// till has exactly the same pair.
    ///
    /// ⚠ ONE IMPLEMENTATION, TWO DOORS — never two implementations. A second copy of "how a till
    /// raises a ticket" is a C2 problem inside one app, which is the least defensible kind: the two
    /// copies would be in the same language, in the same assembly, and still able to disagree.
    ///
    /// ⚠⚠ NO `Modal.ShowAsync` WRAPPER HERE. `InputAlertHelper` gates on that semaphore internally,
    /// and a redundant guard at the call site is the exact defect that stopped the till taking money
    /// on 2026-08-13 — the flow waited on a semaphore it already held, no exception, nothing in any
    /// log. `DisplayAlert` and `ChoiceHelper` are awaited in sequence, as every other flow does.
    ///
    /// ⚠ ONLINE ONLY, and it says so. A ticket is somebody asking for help NOW; queueing it would tell
    /// them it had been sent.
    /// </summary>
    public static class SupportFlow
    {
        /// <summary>⚠ NEVER THROWS to its caller — both entry points are `async void` handlers, and an
        /// escape from one closes the till.</summary>
        public static async Task ShowAsync()
        {
            try
            {
                var tickets = await SupportDesk.LoadAsync();

                if (tickets is null)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "Plutus can't be reached, so a ticket can't be raised or read right now. "
                        + "Nothing has been sent — try again when the connection is back.",
                        "OK".Translate());
                    return;
                }

                // ⚠ The new-ticket option is FIRST and always present. Somebody opening this screen
                // with a problem should not have to read a list of old tickets to find it.
                var newTicket = "Raise a new ticket";
                var choices = new List<string> { newTicket };
                choices.AddRange(tickets.Select(SupportDesk.TicketLine));

                var picked = await Helpers.CustomViews.ChoiceHelper.AskAsync(
                    "Help and support", "Cancel".Translate(), null, choices.ToArray());

                if (string.IsNullOrWhiteSpace(picked) || picked == "Cancel".Translate()) return;

                if (picked == newTicket) { await RaiseTicketAsync(); return; }

                // ⚠ BY INDEX, not by matching the label back. Two tickets can share a subject AND a
                // status, and a by-text lookup would silently open the wrong conversation.
                var index = choices.IndexOf(picked) - 1;
                if (index >= 0 && index < tickets.Count) await OpenTicketAsync(tickets[index]);
            }
            catch (Exception ex)
            {
                CrashLog.Write("Settings.HelpAndSupport", ex);
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "That didn't work. Nothing has been sent.", "OK".Translate());
            }
        }

        /// <summary>⚠ Subject AND body, both required — a subject alone makes somebody at Plutus ask
        /// what the problem is, which costs the shop another day.</summary>
        private static async Task RaiseTicketAsync()
        {
            var required = new IValidator[] { new RequiredValidator() };

            var fields = new ViewElementData[]
            {
                new(1, "What's it about?", "", required, false, true),
                new(2, "What's happening?", "", required, false, true),
            };

            var answers = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                fields, "Send to Plutus", true, "Raise a ticket", "Cancel".Translate());

            // ⚠ `Count == 0`, NOT `is null` — the helper returns an EMPTY dictionary on back-out
            // (`?? new Dictionary<…>()` in `InputAlertHelper.ShowAsync`), never null.
            if (answers.Count == 0) return;

            answers.TryGetValue(1, out string subject);
            answers.TryGetValue(2, out string body);

            if (!SupportDesk.CanRaise(subject, body))
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "A ticket needs both a subject and a description.", "OK".Translate());
                return;
            }

            // ⚠ ASKED SEPARATELY, because it changes how fast somebody at Plutus picks it up and the
            // operator should be making that choice deliberately rather than ticking past it.
            var urgent = await Application.Current.MainPage.DisplayAlert("How urgent is it?",
                "Is this stopping you trading?", "Yes — we can't trade", "No — it can wait");

            if (await SupportDesk.RaiseAsync(subject, body, urgent))
            {
                await Application.Current.MainPage.DisplayAlert("Sent",
                    "Plutus has your ticket. The reply appears here, under Help and support.",
                    "OK".Translate());
            }
            else
            {
                // ⚠ NEVER "sent" unless it was. See `SupportDesk.RaiseAsync`.
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "That didn't reach Plutus, so nothing has been sent. Try again in a moment.",
                    "OK".Translate());
            }
        }

        /// <summary>Read a ticket, and answer it if it is still open.</summary>
        private static async Task OpenTicketAsync(Plutus.Contracts.Client.SupportTicketDto ticket)
        {
            var thread = await SupportDesk.ThreadAsync(ticket.Id);

            if (thread is null)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "Plutus can't be reached, so that conversation can't be opened right now.",
                    "OK".Translate());
                return;
            }

            var body = SupportDesk.ThreadText(thread);

            // ⚠ A CLOSED TICKET OFFERS NO REPLY BOX rather than a Send the server would refuse.
            if (SupportLabels.IsClosed(ticket.Status))
            {
                await Application.Current.MainPage.DisplayAlert(ticket.Subject,
                    body + "\n\nThis ticket is closed. Raise a new one if you still need help.",
                    "OK".Translate());
                return;
            }

            if (!await Application.Current.MainPage.DisplayAlert(ticket.Subject, body,
                    "Reply", "Close"))
                return;

            var fields = new ViewElementData[]
            {
                new(1, "Your reply", "", new IValidator[] { new RequiredValidator() }, false, true),
            };

            var answers = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                fields, "Send reply", true, ticket.Subject, "Cancel".Translate());

            // ⚠ `Count == 0`, NOT `is null` — the helper returns an EMPTY dictionary on back-out.
            if (answers.Count == 0) return;
            answers.TryGetValue(1, out string reply);

            if (string.IsNullOrWhiteSpace(reply)) return;

            var sent = await SupportDesk.ReplyAsync(ticket.Id, reply);

            await Application.Current.MainPage.DisplayAlert(
                sent ? "Sent" : "Hmm".Translate(),
                sent
                    ? "Plutus has your reply."
                    : "That didn't reach Plutus, so nothing has been sent. Try again in a moment.",
                "OK".Translate());
        }
    }
}
