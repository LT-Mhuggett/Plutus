using NatApp.Plutus.Models;
using System;
using System.Collections.Generic;
using System.Text;
using Xamarin.Forms;

namespace NatApp.Plutus.Helpers.Extensions.XAML
{
    public class BasketDataTemplateSelector : DataTemplateSelector
    {
        public DataTemplate BasketItemTemplate { get; set; }
        public DataTemplate BasketItemReturnTemplate { get; set; }
        public DataTemplate BasketNoteTemplate { get; set; }

        protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
        {
            if (item is BasketReturnItem basketReturnItem)
                return BasketItemReturnTemplate;
            else if(item is BasketItem basketItem)
                return BasketItemTemplate;
            else if (item is BasketNote basketNote)
                return BasketNoteTemplate;
            else
                throw new ArgumentException("object is not a type that has a valid Template", "item");
        }
    }
}
