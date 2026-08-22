using Plutus.Frontend.AppClient.Models;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.Helpers.Extensions.XAML
{
    public class BasketDataTemplateSelector : DataTemplateSelector
    {
        public DataTemplate BasketItemTemplate { get; set; }
        public DataTemplate BasketItemReturnTemplate { get; set; }
        public DataTemplate BasketNoteTemplate { get; set; }

        protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
        {
            // ⚠⚠ CHOSEN ON `IsReturn`, NOT ON THE RUNTIME TYPE (step 11b, 2026-08-22). This was
            // `item is BasketReturnItem` FIRST and `is BasketItem` second, and the order was
            // load-bearing: the return type derived from the item type, so reversing the two cases
            // would have rendered every return with the SALE template — right money, wrong words,
            // silently. `IBasketRecord`'s seam comment named this selector as one of the two things
            // that had to move before the subclass could go.
            if (item is BasketItem basketItem)
                return basketItem.IsReturn ? BasketItemReturnTemplate : BasketItemTemplate;

            if (item is BasketNote)
                return BasketNoteTemplate;

            throw new ArgumentException("object is not a type that has a valid Template", nameof(item));
        }
    }
}
