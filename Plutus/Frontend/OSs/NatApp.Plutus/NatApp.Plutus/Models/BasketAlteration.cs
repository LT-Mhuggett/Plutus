using System;
using System.Collections.Generic;
using System.Text;
using Database.Models;

namespace NatApp.Plutus.Models
{
    [Serializable]
    public class BasketAlteration : BasketNote
    {
        #region Public Properties
        public DiscountModel Discount { get; }
        public IEnumerable<BasketItem> ItemsAssocitated { get; }
        #endregion

        public BasketAlteration(NoteModel note, DiscountModel discount, IEnumerable<BasketItem> itemsAssociated, decimal price = 0, decimal priceExTax = 0) : base(note, price, priceExTax)
        {
            Discount = discount;
            ItemsAssocitated = itemsAssociated;
        }

        public BasketAlteration(NoteModel note, DiscountModel discount, BasketItem itemAssociated, decimal price = 0, decimal priceExTax = 0) : base(note, price, priceExTax)
        {
            Discount = discount;
            ItemsAssocitated = new List<BasketItem>() { itemAssociated };

        }
    }
}
