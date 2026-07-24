using System.Reflection;
using CommonPOSLibrary.Exceptions;
using Database.Models;
using Moq;
using Plutus.Frontend.AppClient.Models;
using Plutus.Frontend.AppClient.Services.POSHandeling;

namespace Plutus.Frontend.AppClient.Tests.Services
{
    public class PosPrinterManagerTests
    {
        private static void SetDeviceEnabled(PosPrinterManager manager, bool value)
        {
            typeof(PosPrinterManager)
                .GetProperty("_deviceEnabled", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(manager, value);
        }

        private static ItemModel MakeItem(string id, decimal price) => new ItemModel
        {
            Id = id,
            Name = $"Item {id}",
            Price = price,
            Vat = new TaxModel { Name = "Standard", Rate = 0.2 }
        };

        [Fact]
        public async Task SelectPrinterAndGetPrinterId_ReturnsCommunicationResult()
        {
            var pos = new Mock<IPOSCommunication>();
            pos.Setup(p => p.SendAndGetResponseAsync(It.IsAny<KeyValuePair<string, object>>()))
                .ReturnsAsync("PRINTER-123");
            TestServices.POSCommunication = pos.Object;

            using var manager = new PosPrinterManager();
            var result = await manager.SelectPrinterAndGetPrinterId();

            Assert.Equal("PRINTER-123", result);
        }

        [Fact]
        public async Task SelectPrinterAndGetPrinterId_OnException_ReturnsDefault()
        {
            var pos = new Mock<IPOSCommunication>();
            pos.Setup(p => p.SendAndGetResponseAsync(It.IsAny<KeyValuePair<string, object>>()))
                .ThrowsAsync(new InvalidOperationException("boom"));
            TestServices.POSCommunication = pos.Object;

            using var manager = new PosPrinterManager();
            var result = await manager.SelectPrinterAndGetPrinterId();

            Assert.Null(result);
        }

        [Fact]
        public async Task OpenCashDrawer_ReturnsCommunicationResult()
        {
            var pos = new Mock<IPOSCommunication>();
            pos.Setup(p => p.SendAndGetResponseAsync(It.IsAny<KeyValuePair<string, object>>()))
                .ReturnsAsync(true);
            TestServices.POSCommunication = pos.Object;

            using var manager = new PosPrinterManager();
            Assert.True(await manager.OpenCashDrawer());
        }

        [Fact]
        public async Task CloseConnection_WhenDeviceNotEnabled_ReturnsTrueWithoutCallingCommunication()
        {
            var pos = new Mock<IPOSCommunication>(MockBehavior.Strict);
            TestServices.POSCommunication = pos.Object;

            using var manager = new PosPrinterManager();
            Assert.True(await manager.CloseConnection());
            pos.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task CloseConnection_WhenDeviceEnabled_CallsCommunicationAndClearsFlag()
        {
            var pos = new Mock<IPOSCommunication>();
            pos.Setup(p => p.SendAndGetResponseAsync(It.IsAny<KeyValuePair<string, object>>()))
                .ReturnsAsync(true);
            TestServices.POSCommunication = pos.Object;

            using var manager = new PosPrinterManager();
            SetDeviceEnabled(manager, true);

            Assert.True(await manager.CloseConnection());
            // A second close, with the flag now cleared, must not call communication again.
            Assert.True(await manager.CloseConnection());
            pos.Verify(p => p.SendAndGetResponseAsync(It.IsAny<KeyValuePair<string, object>>()), Times.Once);
        }

        [Fact]
        public async Task SetUpSalePrint_WhenDeviceNotEnabled_Throws()
        {
            using var manager = new PosPrinterManager();
            var sale = new SaleModel { Total = 10m, TotalExTax = 8m, PaySales = new List<PaymentMethod_SaleModel>(), Notes = new List<Notes_SaleModel>() };

            await Assert.ThrowsAsync<POSPrinterException>(() =>
                manager.SetUpSalePrint(sale, Array.Empty<IBasketRecord>(), new StoreModel()));
        }

        [Fact]
        public async Task SetUpSalePrint_WritesHeaderAndTransactionLines()
        {
            var pos = new Mock<IPOSCommunication>();
            pos.Setup(p => p.SendAndGetResponseAsync(It.IsAny<KeyValuePair<string, object>>()))
                .ReturnsAsync((uint)40);
            TestServices.POSCommunication = pos.Object;

            var manager = new PosPrinterManager();
            SetDeviceEnabled(manager, true);

            var store = new StoreModel { StoreName = "Test Store", FullAddress = "1 Test Street" };
            var sale = new SaleModel
            {
                Total = 12m,
                TotalExTax = 10m,
                PaySales = new List<PaymentMethod_SaleModel>(),
                Notes = new List<Notes_SaleModel>(),
            };
            // Includes both a BasketItem and a BasketReturnItem so PrintTransactionAndRefundsAsync's two
            // near-identical formatting branches (sale items vs. returned items) both execute, plus a
            // PaySales entry with a positive Change so PrintFooterOfReceipt's payment/change loop runs.
            var basket = new IBasketRecord[]
            {
                new BasketItem(MakeItem("I1", 12m)),
                new BasketReturnItem(MakeItem("I2", 5m)),
            };
            sale.PaySales.Add(new PaymentMethod_SaleModel
            {
                Amount = 12m,
                Change = 2m,
                TempPayMethod = new PaymentMethodModel { Name = "Cash" }
            });

            // PrintFooterOfReceipt (the last step of SetUpSalePrint) calls the static App.GetViewModel(),
            // which needs a real App instance - App extends Microsoft.Maui.Controls.Application, a
            // BindableObject, so it can't be constructed here (see ValidationGroupBehaviorTests).
            // Header/transaction formatting runs and populates Lines before that point is reached, so
            // this documents what's reachable rather than skipping the method entirely.
            await Assert.ThrowsAsync<NullReferenceException>(() => manager.SetUpSalePrint(sale, basket, store));

            Assert.Contains(manager.Lines, l => l.Value?.ToString() == "Test Store");
            Assert.Contains(manager.Lines, l => l.Key.ToString()!.StartsWith("str.cntr.true"));
            Assert.Contains(manager.Lines, l => l.Value?.ToString() == "Cash\t25");

            // Dispose() throws if the device is still "enabled" - reset the reflection-set flag first
            // rather than fighting SetUpSalePrint's own lifecycle (it doesn't disable the device itself).
            SetDeviceEnabled(manager, false);
            manager.Dispose();
        }

        [Fact]
        public async Task SetupExecutePrintMultiLine_WhenDeviceNotEnabled_Throws()
        {
            using var manager = new PosPrinterManager();
            await Assert.ThrowsAsync<POSPrinterException>(() => manager.SetupExecutePrintMultiLine());
        }

        [Fact]
        public async Task SetupExecutePrintMultiLine_WhenDeviceEnabled_SendsLinesAndCloses()
        {
            var pos = new Mock<IPOSCommunication>();
            pos.Setup(p => p.SendAndGetResponseAsync(It.IsAny<KeyValuePair<string, object>>()))
                .ReturnsAsync(true);
            TestServices.POSCommunication = pos.Object;

            var manager = new PosPrinterManager();
            SetDeviceEnabled(manager, true);

            await manager.SetupExecutePrintMultiLine();

            // CloseConnection() (called at the end of SetupExecutePrintMultiLine) clears the flag.
            pos.Verify(p => p.SendAndGetResponseAsync(It.IsAny<KeyValuePair<string, object>>()), Times.Exactly(2));
            manager.Dispose();
        }

        [Fact]
        public async Task ExecuteOposOrPdfAsync_WithNoLines_Throws()
        {
            using var manager = new PosPrinterManager();
            await Assert.ThrowsAsync<Exception>(() => manager.ExecuteOposOrPdfAsync());
        }

        [Fact]
        public async Task ExecuteOposOrPdfAsync_WithLinesAndDeviceEnabled_SendsLines()
        {
            var pos = new Mock<IPOSCommunication>();
            pos.Setup(p => p.SendAndGetResponseAsync(It.IsAny<KeyValuePair<string, object>>()))
                .ReturnsAsync(true);
            TestServices.POSCommunication = pos.Object;

            var manager = new PosPrinterManager();
            SetDeviceEnabled(manager, true);
            manager.WriteText("hello");

            await manager.ExecuteOposOrPdfAsync();

            pos.Verify(p => p.SendAndGetResponseAsync(It.IsAny<KeyValuePair<string, object>>()), Times.Once);

            SetDeviceEnabled(manager, false);
            manager.Dispose();
        }

        [Fact]
        public async Task ExecuteOposOrPdfAsync_WithLinesAndDeviceDisabled_DoesNotCallCommunication()
        {
            var pos = new Mock<IPOSCommunication>(MockBehavior.Strict);
            TestServices.POSCommunication = pos.Object;

            using var manager = new PosPrinterManager();
            manager.WriteText("hello");

            await manager.ExecuteOposOrPdfAsync();

            pos.VerifyNoOtherCalls();
        }

        [Fact]
        public void Dispose_WhenDeviceStillEnabled_Throws()
        {
            var manager = new PosPrinterManager();
            SetDeviceEnabled(manager, true);

            Assert.Throws<Exception>(() => manager.Dispose());
        }

        [Fact]
        public void Dispose_WhenDeviceNotEnabled_DoesNotThrow()
        {
            var manager = new PosPrinterManager();
            var ex = Record.Exception(() => manager.Dispose());
            Assert.Null(ex);
        }
    }
}
