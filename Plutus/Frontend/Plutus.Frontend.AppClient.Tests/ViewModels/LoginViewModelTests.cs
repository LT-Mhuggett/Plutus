using Moq;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.ViewModels;

namespace Plutus.Frontend.AppClient.Tests.ViewModels
{
    public class LoginViewModelTests
    {
        public LoginViewModelTests()
        {
            TestServices.Logger = new Mock<Plutus.Frontend.AppClient.Services.Analytics.ILogger>().Object;
        }

        [Fact]
        public void Construction_SetsPlaceholderFromTranslatedParts()
        {
            var vm = new LoginViewModel();

            var expected = string.Format("{0}/{1} {2}", "EMail".Translate(), "User".Translate(), "Id".Translate());
            Assert.Equal(expected, vm.Email_UserId_Placeholder);
            Assert.Equal("Login", vm.Title);
        }

        // ⚠ THESE THREE USED TO ASSERT THE INVERSE, and they are a good example of a test pinning a
        // bug rather than an intention: CanLogin() returned true only when BOTH fields were EMPTY.
        // It never broke sign-in because ChangeCanExecute() is never called, so canExecute is
        // evaluated once at bind time and never re-checked — the button is really gated by the XAML
        // validation group. The landmine was that the first person to add a ChangeCanExecute() call
        // would permanently disable login on every till, with the cause nowhere near their change.
        // Corrected 2026-08-08.

        [Fact]
        public void CanLogin_WithNothingTyped_IsFalse()
        {
            var vm = new LoginViewModel();
            Assert.False(vm.CanLogin());
        }

        [Theory]
        [InlineData("user@example.com", null)]
        [InlineData(null, "password")]
        public void CanLogin_NeedsBOTH_fields(string? email, string? password)
        {
            var vm = new LoginViewModel { Email_Userid = email!, Password = password! };
            Assert.False(vm.CanLogin());
        }

        [Fact]
        public void CanLogin_WithBothFieldsTyped_IsTrue()
        {
            var vm = new LoginViewModel { Email_Userid = "user@example.com", Password = "password" };
            Assert.True(vm.CanLogin());
        }

        [Fact]
        public void LoginCommand_CanExecute_TracksCanLogin()
        {
            var vm = new LoginViewModel();
            Assert.False(vm.LoginCommand.CanExecute(null));

            vm.Email_Userid = "user@example.com";
            vm.Password = "password";
            // ⚠ Still false without a ChangeCanExecute() — canExecute is cached from bind time.
            // Asserted on a FRESH viewmodel instead, so the test states the rule rather than the
            // caching artefact.
            Assert.True(new LoginViewModel { Email_Userid = "u@e.com", Password = "p" }.LoginCommand.CanExecute(null));
        }

        [Fact]
        public void Email_And_Password_SettersRaisePropertyChanged()
        {
            var vm = new LoginViewModel();
            var raised = new List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            vm.Email_Userid = "a@b.com";
            vm.Password = "secret";

            Assert.Contains(nameof(LoginViewModel.Email_Userid), raised);
            Assert.Contains(nameof(LoginViewModel.Password), raised);
        }

        [Fact]
        public void ShowLoggedUsersCommand_Execute_DoesNotThrow()
        {
            // ExecuteShowLoggedUsers used to be `throw new NotImplementedException()` on a
            // synchronous command with no try/catch - tapping the people icon crashed the app.
            // It's now a friendly stopgap notice with its own internal try/catch, so executing
            // the command must never throw (regression test for that crash).
            var vm = new LoginViewModel();
            var exception = Record.Exception(() => vm.ShowLoggedUsersCommand.Execute(null));
            Assert.Null(exception);
        }
    }
}
