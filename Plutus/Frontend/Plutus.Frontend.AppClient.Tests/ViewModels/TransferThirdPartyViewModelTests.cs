using Moq;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.ViewModels.FirstTimeStartUp;

namespace Plutus.Frontend.AppClient.Tests.ViewModels
{
    public class TransferThirdPartyViewModelTests
    {
        public TransferThirdPartyViewModelTests()
        {
            TestServices.Logger = new Mock<Plutus.Frontend.AppClient.Services.Analytics.ILogger>().Object;
        }

        [Fact]
        public void Construction_SetsTranslatedTitleAndEmptyIcon()
        {
            var vm = new TransferThirdPartyViewModel();

            Assert.Equal("TPT".Translate(), vm.Title);
            Assert.Equal("", vm.Icon);
        }

        [Fact]
        public void CopperCommand_IsLazilyCreatedAndCached()
        {
            var vm = new TransferThirdPartyViewModel();

            Assert.Same(vm.CopperCommand, vm.CopperCommand);
        }
    }
}
