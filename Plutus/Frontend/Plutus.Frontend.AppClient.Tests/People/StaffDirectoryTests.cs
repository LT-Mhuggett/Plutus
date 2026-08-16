using System;
using System.Linq;
using Plutus.Contracts.Client;
using Plutus.Frontend.AppClient.Services.People;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.People
{
    /// <summary>
    /// The till's Users screen (WP8, step 24).
    ///
    /// ⚠ What is pinned here is the part that decides what a manager SEES and what the till will
    /// SEND. The dialogs are not testable without a device and are not tested.
    /// </summary>
    public class StaffDirectoryTests
    {
        private static EmployeeDto Person(
            string first = "Ann", string last = "Shah", string email = "ann@kapow.example",
            bool active = true) =>
            new(Guid.NewGuid(), first, last, email, "07700 900000", active);

        // ── the list ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⚠ ACTIVE FIRST. A leaver stays on the legacy list for ever, and a screen used to set a new
        /// starter's password should not open on a list of people who have gone.
        /// </summary>
        [Fact]
        public void Current_staff_come_before_leavers()
        {
            var ordered = StaffDirectory.InReadingOrder(new[]
            {
                Person("Zoe", "Ahmed", active: false),
                Person("Bob", "Brown", active: true),
            });

            Assert.Equal("Bob Brown", ordered[0].DisplayName);
            Assert.Equal("Zoe Ahmed", ordered[1].DisplayName);
        }

        /// <summary>⚠ Then by name, case-insensitively — a list that sorts "ann" after "Zoe" looks
        /// broken and makes somebody scroll twice.</summary>
        [Fact]
        public void Staff_are_then_ordered_by_name_ignoring_case()
        {
            var ordered = StaffDirectory.InReadingOrder(new[]
            {
                Person("zoe", "Ahmed"), Person("Ann", "Blake"), Person("bob", "Carr"),
            });

            Assert.Equal(new[] { "Ann Blake", "bob Carr", "zoe Ahmed" },
                ordered.Select(e => e.DisplayName));
        }

        [Fact]
        public void No_staff_is_an_empty_list_not_a_crash() =>
            Assert.Empty(StaffDirectory.InReadingOrder(null));

        /// <summary>
        /// ⚠⚠ A LEAVER IS MARKED ON THE LINE. Without it somebody sets a password for a person who
        /// cannot sign in and blames the password — then does it again, and stops believing the till.
        /// </summary>
        [Fact]
        public void A_leaver_is_marked_and_a_current_employee_is_not()
        {
            Assert.DoesNotContain("(left)", StaffDirectory.StaffLine(Person()));
            Assert.Contains("(left)", StaffDirectory.StaffLine(Person(active: false)));
        }

        /// <summary>⚠ A row with nothing in it still needs a tappable line — a blank entry reads as
        /// a broken list, and the legacy table has rows like this.</summary>
        [Fact]
        public void A_person_with_no_name_or_email_still_reads_as_something()
        {
            var line = StaffDirectory.StaffLine(Person(first: "", last: "", email: ""));

            Assert.Contains("(no name)", line);
            Assert.Contains("no email", line);
        }

        /// <summary>
        /// ⚠⚠ THE 100-ROW CAP IS SURFACED. The legacy list is ordered by `CreatedAt`, so on a big
        /// business it is the NEWEST staff who fall off — exactly the person somebody opened this
        /// screen to set up. A silent truncation reads as "the till has lost them".
        /// </summary>
        [Fact]
        public void A_full_page_warns_that_newer_staff_may_be_missing()
        {
            var warning = StaffDirectory.TruncationWarning(StaffDirectory.PageSize);

            Assert.NotNull(warning);
            Assert.Contains("portal", warning);
        }

        /// <summary>⚠ And a short list says nothing — a warning on every shop with nine staff is
        /// noise that trains people to dismiss it.</summary>
        [Fact]
        public void A_short_list_carries_no_warning() =>
            Assert.Null(StaffDirectory.TruncationWarning(StaffDirectory.PageSize - 1));

        // ── adding somebody ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⚠⚠ AN EMAIL IS REQUIRED because it is what they sign in with and what `SetPassword`
        /// matches on. Somebody created without one is a row that looks like success and can never
        /// sign in anywhere.
        /// </summary>
        [Theory]
        [InlineData("Ann", "Shah", "ann@kapow.example", true)]
        [InlineData("", "Shah", "ann@kapow.example", false)]
        [InlineData("Ann", "", "ann@kapow.example", false)]
        [InlineData("Ann", "Shah", "", false)]
        [InlineData("Ann", "Shah", "   ", false)]
        public void Adding_somebody_needs_both_names_and_an_email(
            string first, string last, string email, bool ok) =>
            Assert.Equal(ok, StaffDirectory.CanCreate(first, last, email));

        /// <summary>
        /// ⚠ THE CHECK IS DELIBERATELY LOOSE. A till is not the authority on what an address may
        /// contain, and a clever pattern would reject somebody's real address on their first day.
        /// It catches the mistake that actually happens — a NAME typed into the email box.
        /// </summary>
        [Theory]
        [InlineData("ann@kapow.example", true)]
        [InlineData("a@b", true)]
        [InlineData("ann+rota@kapow.co.uk", true)]
        [InlineData("Ann Shah", false)]        // the mistake this exists for
        [InlineData("ann.kapow.example", false)]
        [InlineData("@kapow.example", false)]  // nothing before the @
        [InlineData("ann@", false)]            // nothing after it
        [InlineData("ann @kapow.example", false)]
        public void The_email_check_catches_a_name_but_allows_real_addresses(string email, bool ok) =>
            Assert.Equal(ok, StaffDirectory.LooksLikeAnEmail(email));

        // ── passwords ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⚠⚠ THE MISMATCH IS ITS OWN MESSAGE. Somebody who mistyped the second box and is told the
        /// password is "too short" will lengthen a password that was never the problem — and the new
        /// starter still cannot sign in tomorrow.
        /// </summary>
        [Fact]
        public void A_mismatch_says_it_is_a_mismatch_not_a_length()
        {
            var problem = StaffDirectory.PasswordProblem("correct-horse", "correct-hosre");

            Assert.Contains("match", problem, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("characters", problem, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void A_short_password_says_how_long_it_needs_to_be()
        {
            var problem = StaffDirectory.PasswordProblem("abc", "abc");

            Assert.Contains(StaffDirectory.MinimumPasswordLength.ToString(), problem);
        }

        [Fact]
        public void A_good_matching_password_has_no_problem()
        {
            Assert.Null(StaffDirectory.PasswordProblem("correct-horse", "correct-horse"));
            Assert.True(StaffDirectory.CanSetPassword("correct-horse", "correct-horse"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void An_empty_password_is_refused(string password)
        {
            Assert.NotNull(StaffDirectory.PasswordProblem(password, password));
            Assert.False(StaffDirectory.CanSetPassword(password, password));
        }

        /// <summary>⚠ Case matters, and whitespace is significant — "Ab12345678 " and "Ab12345678"
        /// are different passwords, and folding them here would let one be set and the other typed.</summary>
        [Theory]
        [InlineData("Ab12345678", "ab12345678")]
        [InlineData("Ab12345678", "Ab12345678 ")]
        public void Passwords_are_compared_exactly(string a, string b) =>
            Assert.False(StaffDirectory.CanSetPassword(a, b));
    }
}
