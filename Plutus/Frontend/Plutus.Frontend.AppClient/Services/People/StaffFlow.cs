using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Helpers.Validators;
using CustomViews.Structs;
using Plutus.Frontend.AppClient.Services.Analytics;

namespace Plutus.Frontend.AppClient.Services.People
{
    /// <summary>
    /// **Who works here, add somebody, set a password — WP8 / step 24.**
    ///
    /// ⚠⚠ EXTRACTED FROM `LoginViewModel` ON 2026-08-21, because it now has TWO entry points. Matt, of
    /// MAUI's missing app bar: *"It is also missing the users etc."* It was reachable ONLY from the
    /// **login screen's people icon** — so a supervisor who was already signed in had to **sign out to
    /// add somebody**, which is absurd and is not what the web till does: it puts Users behind the 👥
    /// in the app bar, available while working.
    ///
    /// ⚠ THE LOGIN-SCREEN DOOR STAYS, and it is not redundant. Its own header records why: *"the
    /// person who needs it is standing at a till nobody can get into"*. Both doors, one flow.
    ///
    /// ⚠ ONE IMPLEMENTATION, TWO DOORS — never two implementations. A second copy of "how staff are
    /// created" is a C2 problem inside one assembly, and this one writes credentials.
    ///
    /// ⚠⚠ NO `Modal.ShowAsync` WRAPPER — `InputAlertHelper` gates internally, and the redundant outer
    /// guard is what deadlocked the checkout on 2026-08-13.
    /// </summary>
    public static class StaffFlow
    {
        /// <summary>
        /// ⚠⚠ EVERY DIALOG ON THIS FLOW GOES THROUGH HERE. `App.Current.MainPage` is null whenever
        /// there is no MAUI host — which includes the test that guards this very flow against the
        /// crash it used to cause — so an unguarded `DisplayAlert` throws from inside the `catch` that
        /// was supposed to contain the failure. That is how the original people-icon crash worked, and
        /// it would have come back by a different route.
        /// </summary>
        private static async Task SayAsync(string title, string message)
        {
            if (App.Current?.MainPage is Page page)
                await page.DisplayAlert(title, message, "OK");
        }

        /// <summary>⚠ Same guard as <see cref="SayAsync"/>. Null means "nobody chose anything", which
        /// every caller already handles.</summary>
        private static async Task<string> AskAsync(string title, params string[] choices) =>
            App.Current?.MainPage is Page page
                ? await Helpers.CustomViews.ChoiceHelper.AskAsync(title, "Cancel", null, choices)
                : null;

        /// <summary>⚠ NEVER THROWS to its caller — both doors are `async void` handlers, and an escape
        /// from one closes the till.</summary>
        public static async Task ShowAsync()
        {
            try
            {
                var (staff, problem) = await StaffDirectory.LoadAsync();

                if (staff is null)
                {
                    await SayAsync("Users", problem ?? "Plutus can't be reached from this till right now.");
                    return;
                }

                const string addSomebody = "Add somebody";
                var choices = new List<string> { addSomebody };
                choices.AddRange(staff.Select(StaffDirectory.StaffLine));

                // ⚠ The cap is SAID, not swallowed — the legacy list is ordered by CreatedAt, so it
                // is the newest staff who fall off, which is exactly who this screen is opened for.
                var warning = StaffDirectory.TruncationWarning(staff.Count);
                if (warning is not null) await SayAsync("Users", warning);

                var picked = await AskAsync("Users", choices.ToArray());

                if (string.IsNullOrWhiteSpace(picked) || picked == "Cancel") return;

                if (picked == addSomebody) { await AddSomebodyAsync(); return; }

                // ⚠ BY INDEX. Two people can share a name and an email is not guaranteed unique on
                // the legacy table, so matching the label back would set the wrong person's password.
                var index = choices.IndexOf(picked) - 1;
                if (index >= 0 && index < staff.Count) await SetPasswordAsync(staff[index]);
            }
            catch (Exception ex)
            {
                Analytics.CrashLog.Write("StaffFlow.Show", ex);
                await SayAsync("Users", "That didn't work. Nothing has been changed.");
            }
        }

        /// <summary>
        /// ⚠ THE PASSWORD IS ASKED FOR HERE, in the same flow. A person created without one cannot
        /// sign in anywhere — see `StaffDirectory.CreateAsync`.
        /// </summary>
        private static async Task AddSomebodyAsync()
        {
            var required = new IValidator[] { new RequiredValidator() };

            var fields = new ViewElementData[]
            {
                new(1, "First name", "", required, false, true),
                new(2, "Last name", "", required, false, true),
                // ⚠ This is what they SIGN IN WITH, and what SetPassword matches on.
                new(3, "Email", "", required, false, true),
                new(4, "Mobile (optional)", "", null, false, true),
                // ⚠ Masked, and asked twice — a mistyped password on a new starter's account is
                // indistinguishable from "the till is broken" on their first shift.
                new(5, "Password", "", required, true, true),
                new(6, "Password again", "", required, true, true),
            };

            var answers = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                fields, "Add", true, "Add somebody", "Cancel");

            // ⚠ `Count == 0`, NOT `is null` — the helper returns an EMPTY dictionary on back-out
            // (`?? new Dictionary<…>()` in `InputAlertHelper.ShowAsync`), never null. The old check
            // was dead; the validators below are what actually caught the cancel.
            if (answers.Count == 0) return;

            answers.TryGetValue(1, out string first);
            answers.TryGetValue(2, out string last);
            answers.TryGetValue(3, out string email);
            answers.TryGetValue(4, out string mobile);
            answers.TryGetValue(5, out string password);
            answers.TryGetValue(6, out string again);

            if (!StaffDirectory.CanCreate(first, last, email))
            {
                await SayAsync("Users", "A first name, a last name and an email address are all needed — the email is "
                    + "what they sign in with.");
                return;
            }

            if (StaffDirectory.PasswordProblem(password, again) is string bad)
            {
                await SayAsync("Users", bad);
                return;
            }

            var (ok, problem) = await StaffDirectory.CreateAsync(
                first, last, email, mobile, password);

            await SayAsync("Users", ok ? $"{first.Trim()} can now sign in on any till." : problem);
        }

        /// <summary>Set an existing person's password. ⚠ It takes effect at their NEXT sign-in.</summary>
        private static async Task SetPasswordAsync(Plutus.Contracts.Client.EmployeeDto who)
        {
            var required = new IValidator[] { new RequiredValidator() };

            var fields = new ViewElementData[]
            {
                new(1, "New password", "", required, true, true),
                new(2, "New password again", "", required, true, true),
            };

            var answers = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                fields, "Set password", true, who.DisplayName, "Cancel");

            // ⚠ `Count == 0`, NOT `is null` — the helper returns an EMPTY dictionary on back-out.
            if (answers.Count == 0) return;

            answers.TryGetValue(1, out string password);
            answers.TryGetValue(2, out string again);

            if (StaffDirectory.PasswordProblem(password, again) is string bad)
            {
                await SayAsync("Users", bad);
                return;
            }

            var (ok, problem) = await StaffDirectory.SetPasswordAsync(who, password);

            // ⚠ "NEXT time they sign in" is not padding. Their session is a bearer token with no
            // denylist, so somebody already signed in on another till stays signed in — and a
            // manager who expected otherwise would think the change had not saved.
            await SayAsync("Users", ok
                ? $"Done. {who.DisplayName} uses the new password next time they sign in."
                : problem);
        }
    }
}
