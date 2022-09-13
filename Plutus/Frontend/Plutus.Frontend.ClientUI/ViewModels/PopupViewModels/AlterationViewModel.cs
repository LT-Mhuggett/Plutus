using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plutus.Entities.Models;
using Plutus.Frontend.ClientUI.Core;
using Plutus.Frontend.ClientUI.Core.Enum;
using Plutus.Frontend.ClientUI.Core.EventArgs;
using Plutus.Frontend.ClientUI.Core.Models;
using Plutus.Frontend.ClientUI.Domain.Models;
using Plutus.Frontend.ClientUI.Resources.I18N_L10N;
using Plutus.Frontend.ClientUI.Services.Analytics;
using Plutus.Frontend.ClientUI.Services.Loading;
using Syncfusion.Maui.ListView;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Plutus.Frontend.ClientUI.ViewModels.PopupViewModels
{
    public partial class AlterationViewModel : PopupBaseViewModel<List<BasketAlteration>>
    {
        #region Fields
        [ObservableProperty]
        private Discount _discount;
        [ObservableProperty]
        private string _selectAllText;
        private ObservableCollection<object> _selectedItems;
        #endregion

        #region Properties
        public ObservableCollection<BasketItem> BasketItems { get; set; }
        
        public ObservableCollection<object> SelectedItems
        {
            get => _selectedItems;
            set
            {
                if (_selectedItems != value)
                    _selectedItems = value;
            }
        }
        #endregion

        public AlterationViewModel(ILogger logger, IAppState appState, LoadingViewService loadingViewService) : base(logger, appState, loadingViewService)
        {
            BasketItems = new ObservableCollection<BasketItem>();
            SelectedItems = new ObservableCollection<object>();
            SelectAllText = Strings.SelectAll;
        }


        #region Commands
        [RelayCommand]
        private void BasketItemSelectionChanged()
        {
            if(SelectedItems.Count() == BasketItems.Count())
            {
                SelectAllText = Strings.UnselectAll;
            }
            else
            {
                SelectAllText = Strings.SelectAll;
            }
        }

        [RelayCommand]
        private void SelectAll(object sfListViewParameter)
        {
            if (sfListViewParameter is SfListView sfListView)
            {
                if (SelectAllText == Strings.SelectAll)
                {
                    sfListView.SelectAll();
                    SelectAllText = Strings.UnselectAll;
                }
                else
                {
                    SelectedItems.Clear();
                    SelectAllText = Strings.SelectAll;
                }
            }
        }

        [RelayCommand]
        private void ConfirmAlteration()
        {
            List<BasketAlteration> adjustments = new List<BasketAlteration>();

            if (SelectedItems.Count() < BasketItems.Count())
            {
                foreach (var item in SelectedItems)
                {
                    if (item is BasketItem basketItem)
                    {
                        BasketAlteration adjustment;
                        if (Discount.Type == 0)
                        {
                            var alterationAmount = Math.Abs(Math.Round(Discount.Amount, 2, MidpointRounding.AwayFromZero)) * -1;
                            adjustment = new BasketAlteration(new Note($"{Discount.Name}, {basketItem.Name} {alterationAmount.ToString("C2", CultureInfo.CurrentCulture)}"), Discount, basketItem, alterationAmount, alterationAmount);

                        }
                        else
                        {
                            var alterationAmount = Tuple.Create(Math.Abs(Math.Round(basketItem.Price * Discount.Amount, 2, MidpointRounding.AwayFromZero)) * -1, Math.Abs(Math.Round(basketItem.PriceExTax * Discount.Amount, 2, MidpointRounding.AwayFromZero)) * -1);
                            adjustment = new BasketAlteration(new Note($"{Discount.Name}, {basketItem.Name} {alterationAmount.Item1.ToString("C2", CultureInfo.CurrentCulture)}"), Discount, basketItem, alterationAmount.Item1, alterationAmount.Item2);
                        }
                        adjustments.Add(adjustment);
                    }
                }
            }
            else
            {
                BasketAlteration adjustment;
                if (Discount.Type == 0)
                {
                    var alterationAmount = Math.Abs(Math.Round(Discount.Amount * SelectedItems.Count(), 2, MidpointRounding.AwayFromZero)) * -1;
                    adjustment = new BasketAlteration(new Note($"{Discount.Name}, {alterationAmount.ToString("C2", CultureInfo.CurrentCulture)}"), Discount, SelectedItems.OfType<BasketItem>().ToList(), alterationAmount, alterationAmount);
                }
                else
                {
                    var alterationAmount = Tuple.Create(Math.Abs(Math.Round(SelectedItems.OfType<BasketItem>().Sum(tempItem => tempItem.Price) * Discount.Amount, 2, MidpointRounding.AwayFromZero)) * -1, Math.Abs(Math.Round(SelectedItems.OfType<BasketItem>().Sum(tempItem => tempItem.PriceExTax) * Discount.Amount, 2, MidpointRounding.AwayFromZero)) * -1);
                    adjustment = new BasketAlteration(new Note($"{Discount.Name}, {alterationAmount.Item1.ToString("C2", CultureInfo.CurrentCulture)}"), Discount, SelectedItems.OfType<BasketItem>().ToList(), alterationAmount.Item1, alterationAmount.Item2);
                }
                adjustments.Add(adjustment);
            }
            OnCloseRequest(this, new PopupCloseRequestEventArgs<List<BasketAlteration>>(this, new PopupReturnValue<List<BasketAlteration>>(PopupReturnStatus.Completed, adjustments)));
        }

        [RelayCommand]
        private void CancelAlteration()
        {
            OnCloseRequest(this, new PopupCloseRequestEventArgs<List<BasketAlteration>>(this, new PopupReturnValue<List<BasketAlteration>>(PopupReturnStatus.Canceled, null)));
        }
        #endregion
    }
}
