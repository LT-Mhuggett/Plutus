using System.ComponentModel;
using Database.Models;
using Moq;
using Plutus.Frontend.AppClient.Models;
using Plutus.Frontend.AppClient.ViewModels;

namespace Plutus.Frontend.AppClient.Tests.ViewModels
{
    /// <summary>
    /// AppViewModel (and BaseViewModel, which it derives from) are constructible in a plain xUnit
    /// process because their constructors only call AppServices.Get&lt;ILogger&gt;() and set plain
    /// fields - unlike most other ViewModels (AddEditViewModel, ViewAllViewModel, SalesReportsViewModel,
    /// StockOuttakeViewModel, TillViewModel, and the StackLayout-taking ones), none of which can be
    /// constructed here because their constructors call MainThread.BeginInvokeOnMainThread, which
    /// throws System.Runtime.InteropServices.COMException ("ClassFactory cannot supply requested
    /// class") without a live WinUI3 dispatcher - the same underlying wall as the BindableObject
    /// constructors documented in ValidationGroupBehaviorTests. See coverlet.runsettings for the
    /// resulting exclusion list.
    /// </summary>
    public class AppViewModelTests
    {
        public AppViewModelTests()
        {
            TestServices.Logger = new Mock<Plutus.Frontend.AppClient.Services.Analytics.ILogger>().Object;
        }

        private static ItemModel MakeItem() => new ItemModel
        {
            Id = "1",
            Name = "Widget",
            Price = 10m,
            Vat = new TaxModel { Name = "Standard", Rate = 0.2 }
        };

        [Fact]
        public void Construction_AssignsSessionIdAndEmptyEmployeeList()
        {
            var vm = new AppViewModel();
            Assert.NotEqual(Guid.Empty, vm.SessionId);
            Assert.Empty(vm.Employees);
        }

        [Fact]
        public void EmployeeId_WithSingleEmployee_ReturnsItsId()
        {
            var vm = new AppViewModel();
            vm.Employees.Add(new EmployeeModel { Id = "E1" });
            Assert.Equal("E1", vm.EmployeeId);
        }

        [Fact]
        public void EmployeeId_WithMultipleEmployees_Throws()
        {
            var vm = new AppViewModel();
            vm.Employees.Add(new EmployeeModel { Id = "E1" });
            vm.Employees.Add(new EmployeeModel { Id = "E2" });
            Assert.Throws<NotImplementedException>(() => vm.EmployeeId);
        }

        [Fact]
        public void ToolbarItemsChanged_SetTrue_RaisesToolbarItemChangedEvent()
        {
            var vm = new AppViewModel();
            var raised = false;
            vm.ToolbarItemChanged += () => raised = true;

            vm.ToolbarItemsChanged = true;

            Assert.True(raised);
            Assert.True(vm.ToolbarItemsChanged);
        }

        [Fact]
        public void ToolbarItemsChanged_SetFalse_DoesNotRaiseEvent()
        {
            var vm = new AppViewModel();
            var raised = false;
            vm.ToolbarItemChanged += () => raised = true;

            // Already false by default, so SetProperty's equality check short-circuits before onChanged runs.
            vm.ToolbarItemsChanged = false;

            Assert.False(raised);
        }

        [Fact]
        public void CurrentLoadingItemIsBlank_ReflectsCurrentLoadingItem()
        {
            var vm = new AppViewModel();
            Assert.True(vm.CurrentLoadingItemIsBlank);

            vm.CurrentLoadingItem = "Loading...";
            Assert.False(vm.CurrentLoadingItemIsBlank);

            vm.CurrentLoadingItem = "";
            Assert.True(vm.CurrentLoadingItemIsBlank);
        }

        [Fact]
        public void GetMapper_MapsBasketItemToBasketReturnItemAndBack()
        {
            var vm = new AppViewModel();
            var basketItem = new BasketItem(MakeItem(), 2);

            var returnItem = vm.GetMapper.Map<BasketReturnItem>(basketItem);
            Assert.Equal("Widget", returnItem.Name);
            Assert.Equal(2, returnItem.Quantity);

            var backToItem = vm.GetMapper.Map<BasketItem>(returnItem);
            Assert.Equal("Widget", backToItem.Name);
            Assert.Equal(2, backToItem.Quantity);
        }

        [Fact]
        public void Title_Set_RaisesPropertyChanged()
        {
            var vm = new AppViewModel();
            var raised = new List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            vm.Title = "Hello";
            vm.Icon = "icon.png";

            Assert.Contains(nameof(AppViewModel.Title), raised);
            Assert.Contains(nameof(AppViewModel.Icon), raised);
        }

        [Fact]
        public void Title_SetToSameValue_DoesNotRaisePropertyChanged()
        {
            var vm = new AppViewModel { Title = "Hello" };
            var raised = new List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            vm.Title = "Hello";

            Assert.Empty(raised);
        }

        [Fact]
        public void IsBusy_StaticProperty_RaisesStaticPropertyChangedAndPersistsAcrossInstances()
        {
            // IsBusy is backed by a static field shared by every BaseViewModel subclass, so this
            // resets it to false afterwards regardless of outcome to avoid leaking state into other tests.
            var raised = new List<string?>();
            PropertyChangedEventHandler handler = (_, e) => raised.Add(e.PropertyName);
            AppViewModel.StaticPropertyChanged += handler;
            try
            {
                AppViewModel.IsBusy = true;
                Assert.True(AppViewModel.IsBusy);
                Assert.Contains(nameof(AppViewModel.IsBusy), raised);
            }
            finally
            {
                AppViewModel.IsBusy = false;
                AppViewModel.StaticPropertyChanged -= handler;
            }
        }

        [Fact]
        public void Dispose_ClearsTitleAndIcon()
        {
            var vm = new AppViewModel { Title = "Hello", Icon = "icon.png" };
            vm.Dispose();

            Assert.Null(vm.Title);
            Assert.Null(vm.Icon);
        }

        [Fact]
        public void Dispose_CalledTwice_DoesNotThrow()
        {
            var vm = new AppViewModel();
            vm.Dispose();
            var ex = Record.Exception(() => vm.Dispose());
            Assert.Null(ex);
        }
    }
}
