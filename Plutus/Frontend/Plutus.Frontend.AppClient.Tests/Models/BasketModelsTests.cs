using Database.Models;
using Plutus.Frontend.AppClient.Models;

namespace Plutus.Frontend.AppClient.Tests.Models
{
    public class BasketModelsTests
    {
        private static ItemModel MakeItem(decimal price = 10m, decimal exPrice = 8m) => new ItemModel
        {
            Id = "1",
            Name = "Widget",
            Price = price,
            ExPrice = exPrice,
            Vat = new TaxModel { Name = "Standard", Rate = 0.2 }
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

            Assert.Equal(new[] { nameof(BasketItem.Quantity), nameof(BasketItem.Price), nameof(BasketItem.PriceExTax) }, raised);
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

        [Fact]
        public void BasketReturnItem_SetItemReturn_SetsReasonAndSaleId()
        {
            var returnItem = new BasketReturnItem(MakeItem(), 1);
            returnItem.SetItemReturn("Faulty", "SALE-123");

            Assert.Equal("Faulty", returnItem.Reason);
            Assert.Equal("SALE-123", returnItem.ReturnSaleId);
        }

        [Fact]
        public void BasketReturnItem_IsABasketItem()
        {
            Assert.IsAssignableFrom<BasketItem>(new BasketReturnItem(MakeItem()));
        }

        [Fact]
        public void BasketNote_Construction_UsesNoteNameAndDefaults()
        {
            var note = new BasketNote(new NoteModel("Gift wrap"));

            Assert.Equal("Gift wrap", note.Name);
            Assert.Equal(1, note.Quantity);
            Assert.Equal(0m, note.Price);
            Assert.Equal(0m, note.PriceExTax);
        }

        [Fact]
        public void BasketNote_Construction_WithExplicitPrices()
        {
            var note = new BasketNote(new NoteModel("Bag"), 1.5m, 1.25m);

            Assert.Equal(1.5m, note.Price);
            Assert.Equal(1.25m, note.PriceExTax);
        }

        [Fact]
        public void BasketNote_PropertyChanged_RaisedForQuantityPriceAndPriceExTax()
        {
            var note = new BasketNote(new NoteModel("Bag"));
            var raised = new List<string?>();
            note.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            note.Quantity = 3;
            note.Price = 2m;
            note.PriceExTax = 1.5m;

            Assert.Equal(new[] { nameof(BasketNote.Quantity), nameof(BasketNote.Price), nameof(BasketNote.PriceExTax) }, raised);
        }

        [Fact]
        public void BasketAlteration_Construction_WithSingleItem_WrapsInList()
        {
            var item = new BasketItem(MakeItem());
            var alteration = new BasketAlteration(new NoteModel("10% off"), new DiscountModel { Name = "Loyalty" }, item, 1m, 0.8m);

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
            var alteration = new BasketAlteration(new NoteModel("Bundle"), new DiscountModel(), items);

            Assert.Equal(2, alteration.ItemsAssocitated.Count());
        }

        [Fact]
        public void BasketAlteration_IsABasketNote()
        {
            Assert.IsAssignableFrom<BasketNote>(new BasketAlteration(new NoteModel("x"), new DiscountModel(), new BasketItem(MakeItem())));
        }
    }
}
