using Database.Enums;
using Database.Models;
using Microsoft.Maui.Devices.Sensors;
using Moq;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.ViewModels.FirstTimeStartUp;

namespace Plutus.Frontend.AppClient.Tests.ViewModels
{
    public class SetupViewModelTests
    {
        public SetupViewModelTests()
        {
            TestServices.Logger = new Mock<Plutus.Frontend.AppClient.Services.Analytics.ILogger>().Object;
        }

        [Fact]
        public void Construction_SetsTranslatedTitleAndDefaultServerOptions()
        {
            var vm = new SetupViewModel();

            // ⚠ MARKED LEGACY 2026-08-08 (Matt: the till starts at the PORTAL, not the local app).
            // The prefix is asserted rather than tolerated: this screen builds a STANDALONE till —
            // a locally-invented store and admin with no tenant, no till record and no device
            // credential — and it completes successfully, which is exactly what makes it dangerous.
            // Nobody should reach it by accident, so if the marker is ever dropped, this fails.
            Assert.StartsWith("(Legacy)", vm.Title);
            Assert.Contains("SetUpTitle".Translate(), vm.Title);

            Assert.Equal(2, vm.ServerOptions.Count);
            Assert.Contains(vm.ServerOptions, o => o.Key == "Local Application" && o.Value == DatabaseProvider.Sqlite);
            // ⚠ "Cloud" is offered and was NEVER implemented — DatabaseProvider.Cloud throws. The
            // option's presence is the reason the whole screen is retired rather than repaired:
            // enrolment (WP4 / the Connect to Plutus tab) is what replaces it.
            Assert.Contains(vm.ServerOptions, o => o.Key == "Cloud" && o.Value == DatabaseProvider.Cloud);
        }

        [Fact]
        public void CreateCommand_CanExecute_RequiresMatchingNonEmptyPasswords()
        {
            var vm = new SetupViewModel();
            Assert.False(vm.CreateCommand.CanExecute(null));

            vm.Password = "secret";
            vm.PasswordConf = "different";
            Assert.False(vm.CreateCommand.CanExecute(null));

            vm.PasswordConf = "secret";
            Assert.True(vm.CreateCommand.CanExecute(null));
        }

        [Fact]
        public void Employee_Store_Placemark_Setters_RaisePropertyChanged()
        {
            var vm = new SetupViewModel();
            var raised = new List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            vm.Employee = new EmployeeModel();
            vm.Store = new StoreModel();
            vm.Placemarks = new List<Placemark>();
            vm.Placemark = new Placemark();
            vm.ServerOption = vm.ServerOptions[0];

            Assert.Contains(nameof(SetupViewModel.Employee), raised);
            Assert.Contains(nameof(SetupViewModel.Store), raised);
            Assert.Contains(nameof(SetupViewModel.Placemarks), raised);
            Assert.Contains(nameof(SetupViewModel.Placemark), raised);
            Assert.Contains(nameof(SetupViewModel.ServerOption), raised);
        }

        [Fact]
        public void UseAddressCommand_Account_CopiesPlacemarkIntoEmployeeAddress()
        {
            var vm = new SetupViewModel
            {
                Placemark = new Placemark
                {
                    SubThoroughfare = "1",
                    Thoroughfare = "Test Street",
                    SubLocality = "Test Area",
                    Locality = "Test City",
                    PostalCode = "12345",
                    CountryName = "Testland"
                }
            };

            vm.UseAddressCommand.Execute("Account");

            Assert.Equal("1 Test Street", vm.Employee.AdLine1);
            Assert.Equal("Test Area", vm.Employee.AdLine2);
            Assert.Equal("Test City", vm.Employee.City);
            Assert.Equal("12345", vm.Employee.PostCode);
            Assert.Equal("Testland", vm.Employee.Country);
        }

        [Fact]
        public void UseAddressCommand_Store_CopiesPlacemarkIntoStoreAddress()
        {
            var vm = new SetupViewModel
            {
                Placemark = new Placemark
                {
                    SubThoroughfare = "2",
                    Thoroughfare = "Other Street",
                    SubLocality = "Other Area",
                    Locality = "Other City",
                    PostalCode = "54321",
                    CountryName = "Otherland"
                }
            };

            vm.UseAddressCommand.Execute("Store");

            Assert.Equal("2 Other Street", vm.Store.AdLine1);
            Assert.Equal("Other Area", vm.Store.AdLine2);
            Assert.Equal("Other City", vm.Store.City);
            Assert.Equal("54321", vm.Store.PostCode);
            Assert.Equal("Otherland", vm.Store.Country);
        }

        [Fact]
        public void UseAddressCommand_UnknownArgument_DoesNothing()
        {
            var vm = new SetupViewModel();
            var ex = Record.Exception(() => vm.UseAddressCommand.Execute("SomethingElse"));
            Assert.Null(ex);
        }

        [Fact]
        public void LocationCommand_And_CreateCommand_AreLazilyCreatedAndCached()
        {
            var vm = new SetupViewModel();
            Assert.Same(vm.LocationCommand, vm.LocationCommand);
            Assert.Same(vm.CreateCommand, vm.CreateCommand);
            Assert.Same(vm.UseAddressCommand, vm.UseAddressCommand);
        }
    }
}
