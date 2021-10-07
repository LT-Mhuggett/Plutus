using Plutus.Entities.Models;

namespace Plutus.Repository.FormBodies
{
    public class StockBody : FormBody<Stock>
    {
        public int Quantity { get; set; }
        public string ItemIdOne { get; set; }
        public string BussinessId { get; set; }
        public string StoreId { get; set; }

        public override Stock GenerateEntity() => new Stock
        {
            IdOne = ItemIdOne,
            IdTwo = BussinessId,
            IdThree = StoreId,
            Quantity = Quantity
        };
    }
}
