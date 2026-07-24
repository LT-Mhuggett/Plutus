using System;
using System.Collections.Generic;
using System.Text;

namespace Plutus.Frontend.AppClient.Models
{
    public interface IBasketRecord
    {
        int Quantity { get; set; }

        string Name { get; }

        decimal Price { get; set; }

        decimal PriceExTax { get; set; }

        string Tax { get; }
    }
}
