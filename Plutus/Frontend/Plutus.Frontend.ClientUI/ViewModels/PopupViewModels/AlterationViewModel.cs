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
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plutus.Frontend.ClientUI.ViewModels.PopupViewModels
{
    public partial class AlterationViewModel : PopupBaseViewModel<List<BasketAlteration>>
    {
        #region Fields
        [ObservableProperty]
        private Discount _discount;
        [ObservableProperty]
        private string _selectAllText;
        #endregion

        #region Properties
        public ObservableCollection<BasketItem> BasketItems;
        
        public ObservableCollection<BasketItem> SelectedItems;
        #endregion

        public AlterationViewModel(ILogger logger, IAppState appState, LoadingViewService loadingViewService) : base(logger, appState, loadingViewService)
        {
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
        private void SelectAll()
        {
            if (SelectAllText == Strings.SelectAll)
                SelectedItems = BasketItems;

            else
                SelectedItems.Clear();
        }

        [RelayCommand]
        private void ConfirmAlteration()
        {
            List<BasketAlteration> adjustments = new List<BasketAlteration>();

            if (SelectedItems.Count() < BasketItems.Count())
            {
                foreach (var item in SelectedItems)
                {
                    BasketAlteration adjustment;
                    if (Discount.Type == 0)
                    {
                        var alterationAmount = Math.Abs(Math.Round(Discount.Amount, 2, MidpointRounding.AwayFromZero)) * -1;
                        adjustment = new BasketAlteration(new Note($"{Discount.Name}, {item.Name} {alterationAmount.ToString("C2", CultureInfo.CurrentCulture)}"), Discount, item, alterationAmount, alterationAmount);

                    }
                    else
                    {
                        var alterationAmount = Tuple.Create(Math.Abs(Math.Round(item.Price * Discount.Amount, 2, MidpointRounding.AwayFromZero)) * -1, Math.Abs(Math.Round(item.PriceExTax * Discount.Amount, 2, MidpointRounding.AwayFromZero)) * -1);
                        adjustment = new BasketAlteration(new Note($"{Discount.Name}, {item.Name} {alterationAmount.Item1.ToString("C2", CultureInfo.CurrentCulture)}"), Discount, item, alterationAmount.Item1, alterationAmount.Item2);
                    }
                    adjustments.Add(adjustment);
                }
            }
            else
            {
                BasketAlteration adjustment;
                if (Discount.Type == 0)
                {
                    var alterationAmount = Math.Abs(Math.Round(Discount.Amount * SelectedItems.Count(), 2, MidpointRounding.AwayFromZero)) * -1;
                    adjustment = new BasketAlteration(new Note($"{Discount.Name}, {alterationAmount.ToString("C2", CultureInfo.CurrentCulture)}"), Discount, SelectedItems, alterationAmount, alterationAmount);
                }
                else
                {
                    var alterationAmount = Tuple.Create(Math.Abs(Math.Round(SelectedItems.Sum(tempItem => tempItem.Price) * Discount.Amount, 2, MidpointRounding.AwayFromZero)) * -1, Math.Abs(Math.Round(SelectedItems.Sum(tempItem => tempItem.PriceExTax) * Discount.Amount, 2, MidpointRounding.AwayFromZero)) * -1);
                    adjustment = new BasketAlteration(new Note($"{Discount.Name}, {alterationAmount.Item1.ToString("C2", CultureInfo.CurrentCulture)}"), Discount, SelectedItems, alterationAmount.Item1, alterationAmount.Item2);
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
