using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Contracts.Client;

namespace Plutus.Frontend.AppClient.Services.People
{
    /// <summary>
    /// WP8 / step 24 — the till's Users screen: who works here, add somebody, set a password.
    ///
    /// ⚠⚠ ROLES AND PERMISSIONS ARE DELIBERATELY ABSENT. The MAUI parity target is the **web till's**
    /// smaller surface, not the portal's: who exists, and what their password is. Who may do what
    /// stays in the portal, where the change is audited and the person making it can see the whole
    /// estate — a till is the wrong place to widen somebody's authority from.
    ///
    /// ⚠ The decisions are pure and testable; the dialogs are not and are not tested.
    /// </summary>
    public static class StaffDirectory
    {
        /// <summary>⚠ The web till's page size, and a REAL cap — see <see cref="TruncationWarning"/>.</summary>
        public const int PageSize = 100;

        /// <summary>
        /// ⚠ ACTIVE FIRST, THEN BY NAME. A leaver stays on the legacy list for ever, and a screen
        /// used to set a new starter's password should not open on a list of people who left.
        /// </summary>
        public static IReadOnlyList<EmployeeDto> InReadingOrder(IEnumerable<EmployeeDto> staff) =>
            (staff ?? Enumerable.Empty<EmployeeDto>())
                .OrderByDescending(e => e.Active)
                .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>
        /// One line in the staff picker.
        ///
        /// ⚠ A LEAVER IS MARKED. Without it, somebody sets a password for a person who cannot sign
        /// in and blames the password — the same call twice, and the second time nobody believes the
        /// till.
        /// </summary>
        public static string StaffLine(EmployeeDto e)
        {
            if (e is null) throw new ArgumentNullException(nameof(e));

            var name = string.IsNullOrWhiteSpace(e.DisplayName) ? "(no name)" : e.DisplayName;
            var email = string.IsNullOrWhiteSpace(e.Email) ? "no email" : e.Email;

            return e.Active ? $"{name} — {email}" : $"{name} — {email} (left)";
        }

        /// <summary>
        /// ⚠⚠ THE CAP IS SURFACED, NEVER SILENT. The legacy list is ordered by `CreatedAt`, so on a
        /// business with more than 100 staff it is the **newest** people who fall off — exactly the
        /// ones somebody opens this screen to set up. Saying so is the difference between "the till
        /// is broken" and "use the portal for this one".
        /// </summary>
        public static string TruncationWarning(int shown) =>
            shown >= PageSize
                ? $"Showing the first {PageSize} people. Newer staff may not be listed — use the portal."
                : null;

        /// <summary>
        /// Is this enough to create somebody?
        ///
        /// ⚠ AN EMAIL IS REQUIRED because it is what they SIGN IN WITH, and it is what
        /// `SetPassword` matches on. A person created without one cannot be given a password and
        /// cannot ever sign in — a row that looks like success and is useless.
        /// </summary>
        public static bool CanCreate(string firstName, string lastName, string email) =>
            !string.IsNullOrWhiteSpace(firstName)
            && !string.IsNullOrWhiteSpace(lastName)
            && LooksLikeAnEmail(email);

        /// <summary>
        /// ⚠ DELIBERATELY LOOSE. The till is not the authority on what an address may contain, and a
        /// clever pattern here would reject somebody's real address on their first day. It catches
        /// the mistake that actually happens — a name typed into the email box.
        /// </summary>
        public static bool LooksLikeAnEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;

            var at = email.Trim().IndexOf('@');

            return at > 0 && at < email.Trim().Length - 1 && !email.Trim().Contains(' ');
        }

        /// <summary>
        /// ⚠ THE TILL'S MINIMUM, and it is a floor rather than a policy. The platform decides what it
        /// really accepts; this only stops the till sending something it knows will be refused, so
        /// the operator is told before a round trip instead of after one.
        /// </summary>
        public const int MinimumPasswordLength = 8;

        /// <summary>
        /// ⚠ DEFINED BY <see cref="PasswordProblem"/>, deliberately. These two had the comparison
        /// written out twice, and a mutation proved it: changing one to case-insensitive left the
        /// other correct, so the till would have accepted a pair it then described as matching. One
        /// rule, one place — the check and the message can no longer disagree.
        /// </summary>
        public static bool CanSetPassword(string password, string confirmation) =>
            PasswordProblem(password, confirmation) is null;

        /// <summary>
        /// Why a password was refused, or null when it is fine.
        ///
        /// ⚠ THE MISMATCH IS NAMED SEPARATELY from "too short". Somebody who mistyped the second box
        /// and is told the password is too short will lengthen a password that was never the problem.
        /// </summary>
        public static string PasswordProblem(string password, string confirmation)
        {
            if (string.IsNullOrWhiteSpace(password)) return "A password is needed.";

            if (password.Length < MinimumPasswordLength)
                return $"A password needs at least {MinimumPasswordLength} characters.";

            return string.Equals(password, confirmation, StringComparison.Ordinal)
                ? null
                : "Those two passwords don't match.";
        }

        // ── the flow ──────────────────────────────────────────────────────────────────────────

        /// <summary>Who works here, or null with a reason when the platform could not be asked.</summary>
        public static async Task<(IReadOnlyList<EmployeeDto> Staff, string Problem)> LoadAsync(
            CancellationToken ct = default)
        {
            var api = await Storage.TillPlacement.TryCreateApiAsync(ct).ConfigureAwait(false);
            if (api is null) return (null, "Plutus can't be reached from this till right now.");

            if (await Storage.TillPlacement.BusinessIdAsync(ct).ConfigureAwait(false) is not Guid business)
                return (null, "This till hasn't learnt which business it belongs to yet.");

            var (staff, problem) = await api.GetEmployeesAsync(business, ct).ConfigureAwait(false);

            // ⚠ An empty list and a failed call are DIFFERENT ANSWERS — `GetLegacyAsync` is built
            // around exactly that, and flattening them here would undo it.
            return staff is null ? (null, problem) : (InReadingOrder(staff), null);
        }

        /// <summary>
        /// Add somebody, and give them their password in the same breath.
        ///
        /// ⚠⚠ THE PASSWORD IS PART OF CREATING THEM, not a second errand. A person created without
        /// one cannot sign in anywhere, and a till that stopped after the create would leave a row
        /// that looks finished and is not — on the one screen whose whole purpose is getting a new
        /// starter working.
        ///
        /// ⚠ THE CREATE IS REPORTED EVEN WHEN THE PASSWORD FAILS. They now exist; telling somebody
        /// it all failed invites them to add the same person again, and the legacy controller will
        /// happily hold two.
        /// </summary>
        public static async Task<(bool Ok, string Problem)> CreateAsync(
            string firstName, string lastName, string email, string mobile, string password,
            CancellationToken ct = default)
        {
            var api = await Storage.TillPlacement.TryCreateApiAsync(ct).ConfigureAwait(false);
            if (api is null) return (false, "Plutus can't be reached, so nobody has been added.");

            if (await Storage.TillPlacement.BusinessIdAsync(ct).ConfigureAwait(false) is not Guid business)
                return (false, "This till hasn't learnt which business it belongs to yet.");

            var storeId = await Storage.TillPlacement.StoreIdAsync(ct: ct).ConfigureAwait(false) ?? 0;

            // ⚠ The id is minted HERE — the legacy POST does not generate one, and two creates that
            // both omitted it would collide on Guid.Empty.
            var id = Guid.NewGuid();

            var (created, problem) = await api.CreateEmployeeAsync(
                new CreateEmployeeRequest(
                    id, firstName.Trim(), lastName.Trim(), email.Trim(),
                    string.IsNullOrWhiteSpace(mobile) ? "-" : mobile.Trim(),
                    business, storeId),
                business, ct).ConfigureAwait(false);

            if (!created) return (false, problem ?? "Plutus refused the new person.");

            var (passwordSet, passwordProblem) = await api.SetEmployeePasswordAsync(
                id, email.Trim(), password, business, ct).ConfigureAwait(false);

            return passwordSet
                ? (true, null)
                : (false, $"{firstName.Trim()} has been added, but the password didn't save "
                        + $"({passwordProblem ?? "Plutus refused it"}). Set it from the list.");
        }

        /// <summary>Set an existing person's password. ⚠ Takes effect at their NEXT sign-in.</summary>
        public static async Task<(bool Ok, string Problem)> SetPasswordAsync(
            EmployeeDto who, string password, CancellationToken ct = default)
        {
            if (who is null) return (false, "Nobody was chosen.");

            var api = await Storage.TillPlacement.TryCreateApiAsync(ct).ConfigureAwait(false);
            if (api is null) return (false, "Plutus can't be reached, so nothing has changed.");

            if (await Storage.TillPlacement.BusinessIdAsync(ct).ConfigureAwait(false) is not Guid business)
                return (false, "This till hasn't learnt which business it belongs to yet.");

            var (ok, problem) = await api.SetEmployeePasswordAsync(
                who.Id, who.Email, password, business, ct).ConfigureAwait(false);

            return ok ? (true, null) : (false, problem ?? "Plutus refused the new password.");
        }
    }
}
