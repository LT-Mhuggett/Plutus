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
                    // ⚠⚠ SAY WHICH FAILURE IT IS. This message used to blame the network for every
                    // null, including a 403 — so an operator without `support.tickets` was told the
                    // connection was down and went to check the broadband. Matt hit exactly that:
                    // *"I still have a Plutus cannot be reached when I select the help button"*, on a
                    // till that was online and had just fetched its own name from the same server.
                    //
                    // ⚠ NOT SIGNED IN is a third answer again, and the commonest of the three on a
                    // till left on the login screen.
                    var signedIn = App.GetViewModel()?.SignedInOperator is not null;
                    var message = !signedIn
                        ? "Sign in first — a support ticket is raised as a person, not as a till."
                        : "Plutus can't be reached, or this account isn't allowed to raise tickets. "
                          + "Nothing has been sent. If the till is online, ask for the support permission.";

                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), message, "OK".Translate());
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

        /// <summary>
        /// Read a ticket, answer it, or settle whether it should close.
        ///
        /// ⚠⚠ WP-TICKETS (2026-08-21) added three things here, all of which Matt asked for:
        /// the thread now **marks itself read** (which is what clears the ❓ badge on every till in
        /// the shop), a **closed ticket says when it closed**, and either side can **ask to close**.
        /// </summary>
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

            // ⚠⚠ MARK READ ON OPEN — this is what clears the badge, here and on the till beside this
            // one. ⚠ Fire and forget, and never mind the outcome: failing to RECORD a read must not
            // stop somebody reading. The worst case is a badge that clears on the next beat.
            _ = MarkReadAsync(ticket.Id);

            var body = SupportDesk.ThreadText(thread);

            // ⚠ A CLOSED TICKET OFFERS NO REPLY BOX rather than a Send the server would refuse.
            // ⚠ AND IT SAYS WHEN — Matt: *"there is nothing visual within the ticket itself?"*
            if (SupportLabels.IsClosed(ticket.Status))
            {
                var when = ticket.ClosedAtUtc is DateTime c
                    ? $" It was closed on {SharedKernel.ApiTime.AsLocal(c):dd MMM yyyy}."
                    : "";

                await Application.Current.MainPage.DisplayAlert(ticket.Subject,
                    body + $"\n\n🔒 This ticket is closed.{when} Raise a new one if you still need help.",
                    "OK".Translate());
                return;
            }

            // ⚠⚠ THE STANDING CLOSURE REQUEST DECIDES WHAT THIS DIALOG OFFERS. Support asking to
            // close is a QUESTION, and until now it was a question the till never showed anybody.
            var asked = ticket.ClosureRequestedByOperator;

            const string replyChoice = "Reply";
            const string askClose = "Ask to close";
            const string yesClose = "Yes, close it";
            const string keepOpen = "Keep it open";

            var note = asked switch
            {
                true => "\n\n⚠ Plutus support has asked to close this. If it is sorted, say so — "
                        + "otherwise keep it open and tell them why.",
                false => "\n\nYou have asked to close this — waiting for Plutus support.",
                _ => "",
            };

            var choices = asked switch
            {
                true => new[] { replyChoice, yesClose, keepOpen },
                false => new[] { replyChoice, keepOpen },
                _ => new[] { replyChoice, askClose },
            };

            var picked = await Helpers.CustomViews.ChoiceHelper.AskAsync(
                ticket.Subject, "Close".Translate(), null, choices);

            if (picked == askClose) { await SettleAsync(ticket.Id, close: false, ask: true); return; }
            if (picked == yesClose) { await SettleAsync(ticket.Id, close: true, ask: false); return; }
            if (picked == keepOpen) { await SettleAsync(ticket.Id, close: false, ask: false); return; }
            if (picked != replyChoice) return;

            // ⚠ The thread body is shown ABOVE the choices rather than inside them — a `ChoiceAlert`
            // is a list of options, and a twelve-line conversation crammed into its title would push
            // the buttons off a till screen.
            _ = note;

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

        /// <summary>
        /// Record that this shop has read a thread — WP-TICKETS, 2026-08-21.
        ///
        /// ⚠ NEVER THROWS AND NEVER BLOCKS. It runs fire-and-forget from the moment the thread
        /// opens: failing to record a read must not stop somebody reading, and the badge clears on
        /// the next heartbeat anyway.
        /// </summary>
        private static async Task MarkReadAsync(Guid ticketId)
        {
            try
            {
                var api = await Services.Connectivity.PlutusApi.GetOperatorAsync();
                if (api is null) return;
                await api.MarkTicketReadAsync(ticketId);
            }
            catch (Exception ex)
            {
                Analytics.CrashLog.Write("SupportFlow.MarkRead", ex);
            }
        }

        /// <summary>
        /// Settle whether a ticket should close — ask, agree, or keep it open.
        /// </summary>
        /// <param name="close">Agree to support's request. ⚠ Only legal while THEY have asked; the
        /// server refuses otherwise, which is what stops a shop closing its own open incident.</param>
        /// <param name="ask">Ask them to close it.</param>
        private static async Task SettleAsync(Guid ticketId, bool close, bool ask)
        {
            try
            {
                var api = await Services.Connectivity.PlutusApi.GetOperatorAsync();

                if (api is null)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "That needs a connection to Plutus. Nothing has been changed.", "OK".Translate());
                    return;
                }

                var ok = close
                    ? await api.AcceptTicketCloseAsync(ticketId)
                    : ask
                        ? await api.RequestTicketCloseAsync(ticketId)
                        : await api.KeepTicketOpenAsync(ticketId);

                // ⚠ THE WORDS DIFFER PER ACTION. "Done" after asking to close and after closing are
                // very different outcomes to a shop, and one message for both would leave somebody
                // unsure whether their ticket had just ended.
                await Application.Current.MainPage.DisplayAlert(
                    ok ? "Support" : "Hmm".Translate(),
                    ok
                        ? close
                            ? "Closed. Raise a new ticket if you need anything else."
                            : ask
                                ? "Plutus support has been asked to close this."
                                : "Kept open — Plutus support will carry on with it."
                        : "That didn't work. Nothing has been changed.",
                    "OK".Translate());
            }
            catch (Exception ex)
            {
                Analytics.CrashLog.Write("SupportFlow.Settle", ex);
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "That didn't work. Nothing has been changed.", "OK".Translate());
            }
        }
    }
}
