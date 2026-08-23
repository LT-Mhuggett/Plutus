using System.ComponentModel;
using Mapster;
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

        private static Plutus.Frontend.AppClient.Models.TillItem MakeItem() => new Plutus.Frontend.AppClient.Models.TillItem
        {
            Id = "1",
            Name = "Widget",
            Price = 10m,
            VatName = "Standard"
        };

        // ⚠⚠ FOUR `Employees`/`EmployeeId` TESTS WENT WITH THE PROPERTIES — L9, 2026-08-23.
        // One of them, `EmployeeId_WithNoLegacyEmployee_IsNullRatherThanACrash`, pinned the fix for
        // the read that CLOSED THE APPLICATION on a portal-provisioned till. Step 21 made null a
        // normal answer; removing the legacy login removed the question, so the pin has nothing left
        // to protect. Named here because a deleted test is a deleted requirement.
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

        /// <summary>
        /// ⚠⚠ THE MAPSTER COPY IS STILL LOAD-BEARING, so it is still pinned (step 11b, 2026-08-22).
        ///
        /// This was `Adapt_MapsBasketItemToBasketReturnItemAndBack` and it guarded a hop ACROSS an
        /// inheritance chain. The subclass is gone, but the copy is not: `ExecuteReturnSelected`
        /// still adapts the selected line into a NEW `BasketItem` before marking it, because the
        /// basket may already hold the sale line for that item — flagging that one in place would
        /// turn a sale the customer is buying into a refund under their hands.
        ///
        /// ⚠ `BasketItem` HAS NO PARAMETERLESS CONSTRUCTOR, and Mapster does not attempt
        /// constructor-parameter matching unless told to. Without `MapToConstructor` in
        /// `BasketMapsterConfig` this throws at runtime — in the refund flow.
        /// </summary>
        [Fact]
        public void Adapt_CopiesABasketItem()
        {
            var basketItem = new BasketItem(MakeItem(), 2);

            var copy = basketItem.Adapt<BasketItem>();

            Assert.Equal("Widget", copy.Name);
            Assert.Equal(2, copy.Quantity);

            // ⚠ A DIFFERENT OBJECT, which is the whole reason the copy exists.
            Assert.NotSame(basketItem, copy);

            // ⚠ AND A COPY OF A SALE IS A SALE. If `IsReturn` ever came across as true, every refund
            // would produce two return lines instead of one.
            Assert.False(copy.IsReturn);
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
