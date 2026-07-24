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

        [Fact]
        public void CanLogin_WhenBothFieldsEmpty_ReturnsTrue()
        {
            var vm = new LoginViewModel();
            Assert.True(vm.CanLogin());
        }

        [Theory]
        [InlineData("user@example.com", null)]
        [InlineData(null, "password")]
        [InlineData("user@example.com", "password")]
        public void CanLogin_WhenEitherFieldSet_ReturnsFalse(string? email, string? password)
        {
            var vm = new LoginViewModel { Email_Userid = email!, Password = password! };
            Assert.False(vm.CanLogin());
        }

        [Fact]
        public void LoginCommand_CanExecute_TracksCanLogin()
        {
            var vm = new LoginViewModel();
            Assert.True(vm.LoginCommand.CanExecute(null));

            vm.Email_Userid = "user@example.com";
            Assert.False(vm.LoginCommand.CanExecute(null));
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
        public void ShowLoggedUsersCommand_Execute_ThrowsNotImplemented()
        {
            // ExecuteShowLoggedUsers is an explicit stub ("Implement Show Logged Users Page").
            var vm = new LoginViewModel();
            Assert.Throws<NotImplementedException>(() => vm.ShowLoggedUsersCommand.Execute(null));
        }
    }
}
