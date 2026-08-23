using System.Reflection;
using CommonPOSLibrary.Exceptions;
using Moq;
using Plutus.Frontend.AppClient.Models;
using Plutus.Frontend.AppClient.Services.POSHandeling;
using Plutus.Frontend.AppClient.Services.Printing;

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

        /// <summary>⚠ A return line. Step 11b collapsed `BasketReturnItem` into a flag, so this is
        /// a `BasketItem` that has been MARKED — constructing one without the mark would make
        /// the receipt print a sale where the test says return.</summary>
        private static BasketItem Returned(Plutus.Frontend.AppClient.Models.TillItem item)
        {
            var line = new BasketItem(item);
            line.MarkAsReturn();
            return line;
        }

        private static Plutus.Frontend.AppClient.Models.TillItem MakeItem(string id, decimal price) => new Plutus.Frontend.AppClient.Models.TillItem
        {
            Id = id,
            Name = $"Item {id}",
            Price = price,
            VatName = "Standard"
        };

        /// <summary>A committed sale as the receipt now receives it (cutover step 14) — money in
        /// pence, straight off the payload the platform accepted.</summary>
        private static ReceiptSale Receipt(long gross, long ex, params ReceiptTender[] tenders) =>
            new(Guid.NewGuid(), new DateTime(2026, 8, 9, 14, 30, 0), gross, ex, gross - ex,
                tenders, Array.Empty<string>());

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

            await Assert.ThrowsAsync<POSPrinterException>(() =>
                manager.SetUpSalePrint(Receipt(1000, 800), Array.Empty<IBasketRecord>(), new Plutus.Frontend.AppClient.Models.StoreDetails()));
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

            var store = new Plutus.Frontend.AppClient.Models.StoreDetails { StoreName = "Test Store", AdLine1 = "1 Test Street", City = "Testville" };

            // Includes both a BasketItem and a BasketReturnItem so PrintTransactionAndRefundsAsync's two
            // near-identical formatting branches (sale items vs. returned items) both execute, plus a
            // tender with positive change so PrintFooterOfReceipt's payment/change loop runs.
            var basket = new IBasketRecord[]
            {
                new BasketItem(MakeItem("I1", 12m)),
                Returned(MakeItem("I2", 5m)),
            };
            var sale = Receipt(1200, 1000, new ReceiptTender("Cash", 1200, 200));

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
