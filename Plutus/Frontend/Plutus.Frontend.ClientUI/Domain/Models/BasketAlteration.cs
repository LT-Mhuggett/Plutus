using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Plutus.Entities.Models;

namespace Plutus.Frontend.ClientUI.Domain.Models
{
    [Serializable]
    public class BasketAlteration : BasketNote
    {
        #region Public Properties
        // Setters + [JsonConstructor] added (BugFix plan, Bug 3): with two constructors,
        // get-only properties AND an ItemsAssocitated/itemsAssociated name mismatch,
        // Newtonsoft could not deserialize a saved basket containing a discount —
        // recalling it crashed the app.
        public Discount Discount { get; set; }
        public IEnumerable<BasketItem> ItemsAssocitated { get; set; }
        #endregion

        [JsonConstructor]
        public BasketAlteration(Note note, Discount discount, IEnumerable<BasketItem> itemsAssocitated, decimal price = 0, decimal priceExTax = 0) : base(note, price, priceExTax)
        {
            Discount = discount;
            ItemsAssocitated = itemsAssocitated;
        }

        public BasketAlteration(Note note, Discount discount, BasketItem itemAssociated, decimal price = 0, decimal priceExTax = 0) : base(note, price, priceExTax)
        {
            Discount = discount;
            ItemsAssocitated = new List<BasketItem>() { itemAssociated };

        }
    }
}
