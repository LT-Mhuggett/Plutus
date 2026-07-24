using Moq;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.ViewModels.FirstTimeStartUp;

namespace Plutus.Frontend.AppClient.Tests.ViewModels
{
    public class RecoveryViewModelTests
    {
        public RecoveryViewModelTests()
        {
            TestServices.Logger = new Mock<Plutus.Frontend.AppClient.Services.Analytics.ILogger>().Object;
        }

        [Fact]
        public void Construction_SetsTranslatedTitleAndEmptyIcon()
        {
            var vm = new RecoveryViewModel();

            Assert.Equal("RecoveryTitle".Translate(), vm.Title);
            Assert.Equal("", vm.Icon);
        }

        [Fact]
        public void RecoverDbCommand_IsLazilyCreatedAndCached()
        {
            var vm = new RecoveryViewModel();

            var first = vm.RecoverDbCommand;
            var second = vm.RecoverDbCommand;

            Assert.Same(first, second);
        }
    }
}
