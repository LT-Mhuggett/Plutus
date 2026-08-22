using System;
using System.Collections.Generic;
using System.Linq;
using Database.Models;
using Plutus.Frontend.AppClient.Models;
using Plutus.Frontend.AppClient.Services.Storage;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Storage
{
    /// <summary>
    /// Cutover step 18 — a parked basket that survives being written down.
    ///
    /// ⚠ WHAT THIS REPLACES. Parking serialised with Newtonsoft `TypeNameHandling.Auto`, which
    /// writes .NET type names into the blob (`NatApp.Plutus.Models.BasketItem, NatApp.Plutus`).
    /// Those stop resolving the moment a namespace, assembly or class name changes — and this
    /// codebase has renamed all three. A basket parked by one build and recalled by the next threw
    /// on deserialisation, which is how discounted parked baskets came to crash the app.
    /// </summary>
    public class ParkedBasketTests
    {
        private static BasketItem Item(string idOne, decimal price, decimal ex, int qty = 1, string band = "Standard") =>
            new(new ItemModel { Id = idOne, Name = "Item " + idOne, Price = price, ExPrice = ex, Vat = new TaxModel { Name = band } }, qty);

        private static BasketItem Return(string idOne, decimal price, decimal ex, string reason, string origin)
        {
            var r = new BasketItem(
                new ItemModel { Id = idOne, Name = "Item " + idOne, Price = price, ExPrice = ex, Vat = new TaxModel { Name = "Standard" } }, 1);
            r.MarkAsReturn(reason, origin);
            return r;
        }

        /// <summary>⚠ The blob must carry NO .NET type metadata — that is the whole point.</summary>
        [Fact]
        public void The_parked_blob_contains_no_dotnet_type_names()
        {
            var json = ParkedBasket.ToJson(new IBasketRecord[] { Item("A", 12m, 10m) });

            Assert.DoesNotContain("$type", json);
            Assert.DoesNotContain("NatApp", json);
            Assert.DoesNotContain("Plutus.Frontend", json);
            Assert.Contains("\"kind\"", json);   // a plain string discriminator instead
        }

        [Fact]
        public void An_ordinary_basket_round_trips_with_its_money_intact()
        {
            var original = new IBasketRecord[] { Item("5010001", 14.99m, 12.49m, qty: 3) };

            var back = ParkedBasket.FromJson(ParkedBasket.ToJson(original));

            var line = Assert.Single(back);
            var item = Assert.IsType<BasketItem>(line);
            Assert.Equal("5010001", item.Item.Id);
            Assert.Equal(14.99m, item.Price);
            Assert.Equal(12.49m, item.PriceExTax);
            Assert.Equal(3, item.Quantity);
            Assert.Equal("Standard", item.Item.Vat.Name);
        }

        /// <summary>
        /// ⚠ A RETURN MUST COME BACK AS A RETURN. `BasketReturnItem` used to derive from `BasketItem`, so
        /// a type test in the wrong order parks every refund as an ordinary sale line — and it
        /// recalls as money owed TO the shop instead of by it, silently doubling the error.
        /// </summary>
        [Fact]
        public void A_return_round_trips_as_a_return_not_as_a_sale_line()
        {
            var original = new IBasketRecord[] { Return("5020001", 9.99m, 8.33m, "faulty", "01931f3c-0000-7000-8000-000000000001") };

            var back = ParkedBasket.FromJson(ParkedBasket.ToJson(original));

            var line = Assert.Single(back);
            // ⚠ The type test became a FLAG test (step 11b) — same guarantee, new seam. A parked
            // return coming back as a sale line is exactly what this pins.
            var ret = Assert.IsType<BasketItem>(line);
            Assert.True(ret.IsReturn, "a parked return must come back as a return");
            Assert.Equal(9.99m, ret.Price);
            Assert.Equal("faulty", ret.Reason);
            Assert.Equal("01931f3c-0000-7000-8000-000000000001", ret.ReturnSaleId);
        }

        /// <summary>A mixed basket keeps every record, in order.</summary>
        [Fact]
        public void A_mixed_basket_keeps_every_record()
        {
            var original = new IBasketRecord[]
            {
                Item("A", 10m, 8.33m),
                Return("B", 5m, 4.17m, "wrong size", "01931f3c-0000-7000-8000-000000000002"),
                Item("C", 2.50m, 2.50m, qty: 4),
            };

            var back = ParkedBasket.FromJson(ParkedBasket.ToJson(original));

            Assert.Equal(3, back.Count);
            Assert.False(Assert.IsType<BasketItem>(back[0]).IsReturn);
            Assert.True(Assert.IsType<BasketItem>(back[1]).IsReturn);
            Assert.Equal(4, back[2].Quantity);
        }

        /// <summary>⚠ A note carrying MONEY keeps it. Dropping the price would recall a basket that
        /// totals less than the customer was quoted.</summary>
        [Fact]
        public void A_note_carrying_money_keeps_it()
        {
            var original = new IBasketRecord[] { new BasketNote(new NoteModel { Note = "Card fee" }, 0.40m, 0.33m) };

            var back = ParkedBasket.FromJson(ParkedBasket.ToJson(original));

            var note = Assert.IsType<BasketNote>(Assert.Single(back));
            Assert.Equal(0.40m, note.Price);
            Assert.Equal(0.33m, note.PriceExTax);
        }

        /// <summary>
        /// ⚠ THE PARKED PRICE IS WHAT WAS QUOTED. A basket is a promise made to a customer who
        /// walked away; re-pricing it on recall would change that without anybody saying so. This
        /// pins the trade deliberately, so a future change to it has to be a decision.
        /// </summary>
        [Fact]
        public void The_recalled_price_is_the_parked_price_not_a_fresh_lookup()
        {
            var json = ParkedBasket.ToJson(new IBasketRecord[] { Item("A", 7.50m, 6.25m) });

            var back = ParkedBasket.FromJson(json);

            Assert.Equal(7.50m, Assert.Single(back).Price);
        }

        /// <summary>⚠ A blob this build cannot read must not take the till with it — an empty
        /// basket and a message beats an unhandled exception mid-shift.</summary>
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("{ not json at all")]
        [InlineData("{\"$type\":\"NatApp.Plutus.Models.BasketItem, NatApp.Plutus\"}")]
        public void An_unreadable_blob_returns_an_empty_basket_rather_than_throwing(string json)
        {
            Assert.Empty(ParkedBasket.FromJson(json));
        }

        /// <summary>Pence conversion is exact both ways — the basket is decimal, the blob is pence,
        /// and a basket that recalls a penny light is a basket the customer is quoted twice on.</summary>
        [Theory]
        [InlineData(0.01)]
        [InlineData(0.99)]
        [InlineData(14.99)]
        [InlineData(1234.56)]
        public void Money_survives_the_decimal_to_pence_round_trip(decimal price)
        {
            var back = ParkedBasket.FromJson(ParkedBasket.ToJson(new IBasketRecord[] { Item("A", price, price) }));

            Assert.Equal(price, Assert.Single(back).Price);
        }
    }
}
