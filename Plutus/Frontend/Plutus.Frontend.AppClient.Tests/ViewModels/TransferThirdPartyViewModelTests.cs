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

            // ⚠ MARKED LEGACY 2026-08-08 (Matt: "this needs to move to the portal"). Importing a
            // third party's data is a central concern — it belongs to the NatApp translation agent,
            // which already moves legacy shop data into the backend, not to one device's local
            // database. Asserted so the marker cannot quietly disappear before the screen does.
            Assert.StartsWith("(Legacy)", vm.Title);
            Assert.Contains("TPT".Translate(), vm.Title);
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
