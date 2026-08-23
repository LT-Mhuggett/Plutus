using Plutus.Frontend.AppClient.Models;

namespace Plutus.Frontend.AppClient.Tests.Models
{
    public class BasketModelsTests
    {
        private static Plutus.Frontend.AppClient.Models.TillItem MakeItem(decimal price = 10m, decimal exPrice = 8m) => new Plutus.Frontend.AppClient.Models.TillItem
        {
            Id = "1",
            Name = "Widget",
            Price = price,
            ExPrice = exPrice,
            VatName = "Standard"
        };

        [Fact]
        public void BasketItem_Construction_CopiesPriceFromItem()
        {
            var item = MakeItem(12.5m, 10m);
            var basketItem = new BasketItem(item, 3);

            Assert.Equal("Widget", basketItem.Name);
            Assert.Equal(3, basketItem.Quantity);
            Assert.Equal(12.5m, basketItem.Price);
            Assert.Equal(10m, basketItem.PriceExTax);
            Assert.Equal("Standard", basketItem.Tax);
        }

        [Fact]
        public void BasketItem_DefaultQuantity_IsOne()
        {
            Assert.Equal(1, new BasketItem(MakeItem()).Quantity);
        }

        [Fact]
        public void BasketItem_IncrementDecrementQuantity()
        {
            var basketItem = new BasketItem(MakeItem(), 5);
            basketItem.IncrementQuantity();
            Assert.Equal(6, basketItem.Quantity);
            basketItem.IncrementQuantity(4);
            Assert.Equal(10, basketItem.Quantity);
            basketItem.DecrementQuantity();
            Assert.Equal(9, basketItem.Quantity);
            basketItem.DecrementQuantity(4);
            Assert.Equal(5, basketItem.Quantity);
        }

        [Fact]
        public void BasketItem_PropertyChanged_RaisedForQuantityPriceAndPriceExTax()
        {
            var basketItem = new BasketItem(MakeItem());
            var raised = new List<string?>();
            basketItem.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            basketItem.Quantity = 2;
            basketItem.Price = 5m;
            basketItem.PriceExTax = 4m;

            // ⚠ THE BOUND NAMES MUST BE RAISED, and this no longer asserts an exact SEQUENCE.
            // Since step 11b the money lives in pence and `Price` is a view of it, so setting the
            // view raises BOTH `PricePence` and `Price`. A binding needs the name it binds to —
            // `Price` — and the extra notification is harmless; pinning the exact list would fail
            // for a change that cannot affect a screen.
            Assert.Contains(nameof(BasketItem.Quantity), raised);
            Assert.Contains(nameof(BasketItem.Price), raised);
            Assert.Contains(nameof(BasketItem.PriceExTax), raised);
        }

        /// <summary>
        /// ⚠⚠ THE POUNDS VIEW AND THE PENCE STORE CANNOT DISAGREE (step 11b). The till rows bind
        /// `Price` with `StringFormat='{0:C}'`; if it ever stopped being a pounds-shaped decimal,
        /// £3.30 would render as £330.00 SILENTLY, on every row. This is the pin on that.
        /// </summary>
        [Fact]
        public void The_pounds_view_and_the_pence_store_agree_in_both_directions()
        {
            var basketItem = new BasketItem(MakeItem());

            basketItem.PricePence = 330;
            Assert.Equal(3.30m, basketItem.Price);

            basketItem.Price = 12.99m;
            Assert.Equal(1299, basketItem.PricePence);

            basketItem.PriceExTaxPence = 275;
            Assert.Equal(2.75m, basketItem.PriceExTax);
        }

        /// <summary>⚠ A price a human typed rounds AWAY FROM ZERO into pence, so it cannot land
        /// between two pence and drift. Banker's rounding would send £0.125 down to 12p.</summary>
        [Theory]
        [InlineData(0.125, 13)]
        [InlineData(0.135, 14)]
        [InlineData(0.005, 1)]
        public void Setting_a_fractional_price_rounds_away_from_zero(double pounds, long expectedPence)
        {
            var basketItem = new BasketItem(MakeItem()) { Price = (decimal)pounds };

            Assert.Equal(expectedPence, basketItem.PricePence);
        }

        [Fact]
        public void BasketItem_Clone_ProducesShallowCopy()
        {
            var basketItem = new BasketItem(MakeItem(), 2);
            var clone = (BasketItem)basketItem.Clone();

            Assert.NotSame(basketItem, clone);
            Assert.Equal(basketItem.Quantity, clone.Quantity);
            Assert.Same(basketItem.Item, clone.Item);
        }

        /// <summary>
        /// ⚠⚠ THE FLAG CARRIES THE MEANING NOW (step 11b, 2026-08-22). This was
        /// `BasketReturnItem_SetItemReturn_SetsReasonAndSaleId`, and the subclass it named is gone —
        /// `IBasketRecord`'s seam comment called out `Reason` and `ReturnSaleId` as two of the three
        /// things that had to move before it could.
        /// </summary>
        [Fact]
        public void MarkAsReturn_SetsTheFlagTheReasonAndTheSaleId()
        {
            var returnItem = new BasketItem(MakeItem(), 1);

            // ⚠ A LINE IS A SALE UNTIL IT IS MARKED. Constructing one and forgetting the mark is the
            // hazard the whole collapse introduced, so the "before" is asserted, not assumed.
            Assert.False(returnItem.IsReturn);

            returnItem.MarkAsReturn("Faulty", "SALE-123");

            Assert.True(returnItem.IsReturn);
            Assert.Equal("Faulty", returnItem.Reason);
            Assert.Equal("SALE-123", returnItem.ReturnSaleId);
        }

        /// <summary>
        /// ⚠⚠ BOTH ARE OPTIONAL, AND THAT IS DELIBERATE — it is what the old subclass allowed. The
        /// till demands a reason before it will proceed and `CheckoutCommit` filters blank ones out
        /// of the sale note, so the guard lives THERE. Requiring them here would be stricter than
        /// the platform has ever been, and it would break `ParkedBasket` restoring a blob parked
        /// before the reason was captured.
        /// </summary>
        [Fact]
        public void MarkAsReturn_WithoutAReason_StillMarksTheLine()
        {
            var line = new BasketItem(MakeItem(), 1);

            line.MarkAsReturn();

            Assert.True(line.IsReturn);
            Assert.Null(line.Reason);
            Assert.Null(line.ReturnSaleId);
        }

        [Fact]
        public void BasketNote_Construction_UsesNoteNameAndDefaults()
        {
            var note = new BasketNote(("Gift wrap"));

            Assert.Equal("Gift wrap", note.Name);
            Assert.Equal(1, note.Quantity);
            Assert.Equal(0m, note.Price);
            Assert.Equal(0m, note.PriceExTax);
        }

        [Fact]
        public void BasketNote_Construction_WithExplicitPrices()
        {
            var note = new BasketNote(("Bag"), 1.5m, 1.25m);

            Assert.Equal(1.5m, note.Price);
            Assert.Equal(1.25m, note.PriceExTax);
        }

        [Fact]
        public void BasketNote_PropertyChanged_RaisedForQuantityPriceAndPriceExTax()
        {
            var note = new BasketNote(("Bag"));
            var raised = new List<string?>();
            note.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            note.Quantity = 3;
            note.Price = 2m;
            note.PriceExTax = 1.5m;

            // ⚠ Names, not sequence — see the BasketItem test above for why.
            Assert.Contains(nameof(BasketNote.Quantity), raised);
            Assert.Contains(nameof(BasketNote.Price), raised);
            Assert.Contains(nameof(BasketNote.PriceExTax), raised);
        }

        /// <summary>⚠ A DISCOUNT IS NEGATIVE, and the sign is load-bearing all the way to the
        /// wire — `CheckoutCommit` takes magnitudes deliberately. Pence must carry it too.</summary>
        [Fact]
        public void A_negative_note_price_stays_negative_in_pence()
        {
            var note = new BasketNote(("Discount"), -5m, -5m);

            Assert.Equal(-500, note.PricePence);
            Assert.Equal(-5m, note.Price);
        }

        [Fact]
        public void BasketAlteration_Construction_WithSingleItem_WrapsInList()
        {
            var item = new BasketItem(MakeItem());
            var alteration = new BasketAlteration(("10% off"), new Plutus.Frontend.AppClient.Models.TillDiscount { Name = "Loyalty" }, item, 1m, 0.8m);

            Assert.Equal("Loyalty", alteration.Discount.Name);
            Assert.Single(alteration.ItemsAssocitated);
            Assert.Same(item, alteration.ItemsAssocitated.First());
            Assert.Equal(1m, alteration.Price);
            Assert.Equal(0.8m, alteration.PriceExTax);
        }

        [Fact]
        public void BasketAlteration_Construction_WithMultipleItems()
        {
            var items = new[] { new BasketItem(MakeItem()), new BasketItem(MakeItem()) };
            var alteration = new BasketAlteration(("Bundle"), new Plutus.Frontend.AppClient.Models.TillDiscount(), items);

            Assert.Equal(2, alteration.ItemsAssocitated.Count());
        }

        [Fact]
        public void BasketAlteration_IsABasketNote()
        {
            Assert.IsAssignableFrom<BasketNote>(new BasketAlteration(("x"), new Plutus.Frontend.AppClient.Models.TillDiscount(), new BasketItem(MakeItem())));
        }
    }
}
