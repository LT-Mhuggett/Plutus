using System;
using System.Collections.Generic;
using System.Text;
using Database.Models;
using Newtonsoft.Json;

namespace Plutus.Frontend.AppClient.Models
{
    [Serializable]
    public class BasketAlteration : BasketNote
    {
        #region Public Properties
        // Setters + [JsonConstructor] added (BugFix plan, Bug 3): with two constructors
        // and get-only properties Newtonsoft could not deserialize a saved basket that
        // contained a discount — recalling it crashed the app.
        public DiscountModel Discount { get; set; }
        public IEnumerable<BasketItem> ItemsAssocitated { get; set; }
        #endregion

        [JsonConstructor]
        public BasketAlteration(NoteModel note, DiscountModel discount, IEnumerable<BasketItem> itemsAssocitated, decimal price = 0, decimal priceExTax = 0) : base(note, price, priceExTax)
        {
            Discount = discount;
            ItemsAssocitated = itemsAssocitated;
        }

        public BasketAlteration(NoteModel note, DiscountModel discount, BasketItem itemAssociated, decimal price = 0, decimal priceExTax = 0) : base(note, price, priceExTax)
        {
            Discount = discount;
            ItemsAssocitated = new List<BasketItem>() { itemAssociated };

        }
    }
}
