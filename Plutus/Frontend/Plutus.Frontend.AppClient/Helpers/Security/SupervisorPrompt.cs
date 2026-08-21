using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CustomViews.Structs;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Helpers.Validators;

namespace Plutus.Frontend.AppClient.Helpers.Security
{
    /// <summary>
    /// Ask a supervisor for their credentials, once.
    ///
    /// ⚠ THIS EXISTS BECAUSE THE OLD PROMPT COULD NOT SUCCEED.
    /// `Authorisation.RequestAuthorisedUserInput` declared `string authEmpId = default`, looped
    /// `while (string.IsNullOrEmpty(authEmpId))`, and **never assigned it anywhere** — so a
    /// supervisor entering correct credentials was simply asked again, for ever, and the only way
    /// out was Cancel. It also verified the password against a LOCAL employee row, which a
    /// portal-provisioned till does not have.
    ///
    /// ⚠ THIS ASKS ONCE AND RETURNS. No loop: retrying is the operator's decision, not a `while`
    /// they cannot escape. Verification is not done here — it belongs to
    /// `OperatorLogin.AuthoriseOverrideAsync`, which checks the roster, refuses self-authorisation
    /// and applies the supervisor's own ceiling. This collects two strings and nothing more.
    /// </summary>
    internal static class SupervisorPrompt
    {
        /// <summary>Null when the supervisor cancelled — which must leave the basket untouched.</summary>
        internal static async Task<(string EmailOrId, string Password)?> AskAsync()
        {
            var required = new IValidator[] { new RequiredValidator() };

            var fields = new ViewElementData[]
            {
                new(1, string.Format("IdArg".Translate(), "Employee".Translate()), "", required, false, true),
                // ⚠ Masked. A supervisor's password must not be readable over their shoulder by the
                // operator whose refund they are authorising.
                new(2, "Password".Translate(), "", required, true, true),
            };

            var answers = await CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                fields, "Confirm".Translate(), true, "AuthReq".Translate(), "Cancel".Translate());

            // ⚠ `Count == 0`, NOT `is null` — `InputAlertHelper.ShowAsync` ends
            // `await popUp.PageClosedTask ?? new Dictionary<…>()`, so backing out yields an EMPTY
            // dictionary and this method NEVER sees null. The old `is null` was a dead check that
            // only appeared to work because the whitespace test below catches the same case.
            if (answers.Count == 0) return null;

            answers.TryGetValue(1, out string emailOrId);
            answers.TryGetValue(2, out string password);

            if (string.IsNullOrWhiteSpace(emailOrId) || string.IsNullOrWhiteSpace(password)) return null;

            return (emailOrId, password);
        }
    }
}
