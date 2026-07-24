using System;
using Microsoft.Maui.Controls;
using Plutus.Frontend.ClientUI.Domain.Models;

namespace Plutus.Frontend.ClientUI.XAML.Extensions
{
    public class BasketDataTemplateSelector : DataTemplateSelector
    {
        public DataTemplate BasketItemTemplate { get; set; }
        public DataTemplate BasketItemReturnTemplate { get; set; }
        public DataTemplate BasketNoteTemplate { get; set; }

        protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
        {
            if (item is BasketReturnItem)
                return BasketItemReturnTemplate;
            else if(item is BasketItem)
                return BasketItemTemplate;
            else if (item is BasketNote)
                return BasketNoteTemplate;
            else
                throw new ArgumentException("object is not a type that has a valid Template", nameof(item));
        }
    }
}
