using Plutus.Entities.Models;
using System;

namespace Plutus.Frontend.ClientUI.Domain.Models
{
    [Serializable]
    public class BasketReturnItem : BasketItem
    {
        public string Reason { get; private set; }
        public Guid ReturnSaleId { get; private set; }

        public BasketReturnItem(Item item, int quantity = 1) : base(item, quantity)
        {
        }

        public void SetItemReturn(string reason, Guid returnSaleId)
        {
            Reason = reason;
            ReturnSaleId = returnSaleId;
        }
    }
}
