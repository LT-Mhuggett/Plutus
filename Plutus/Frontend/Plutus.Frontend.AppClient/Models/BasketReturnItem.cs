using System;
using System.Collections.Generic;
using System.Text;
using Database.Models;

namespace Plutus.Frontend.AppClient.Models
{
    [Serializable]
    public class BasketReturnItem : BasketItem
    {
        public string Reason { get; private set; }
        public string ReturnSaleId { get; private set; }

        public BasketReturnItem(ItemModel item, int quantity = 1) : base(item, quantity)
        {
        }

        public void SetItemReturn(string reason, string returnSaleId)
        {
            Reason = reason;
            ReturnSaleId = returnSaleId;
        }
    }
}
