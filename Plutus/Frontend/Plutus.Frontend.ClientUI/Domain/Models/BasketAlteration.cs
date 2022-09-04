using System;
using System.Collections.Generic;
using System.Text;
using Plutus.Entities.Models;

namespace Plutus.Frontend.ClientUI.Domain.Models
{
    [Serializable]
    public class BasketAlteration : BasketNote
    {
        #region Public Properties
        public Discount Discount { get; }
        public IEnumerable<BasketItem> ItemsAssocitated { get; }
        #endregion

        public BasketAlteration(Note note, Discount discount, IEnumerable<BasketItem> itemsAssociated, decimal price = 0, decimal priceExTax = 0) : base(note, price, priceExTax)
        {
            Discount = discount;
            ItemsAssocitated = itemsAssociated;
        }

        public BasketAlteration(Note note, Discount discount, BasketItem itemAssociated, decimal price = 0, decimal priceExTax = 0) : base(note, price, priceExTax)
        {
            Discount = discount;
            ItemsAssocitated = new List<BasketItem>() { itemAssociated };

        }
    }
}
