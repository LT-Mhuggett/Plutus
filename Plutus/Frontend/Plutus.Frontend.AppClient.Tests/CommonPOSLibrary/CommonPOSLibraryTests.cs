using System.Text;
using CommonPOSLibrary;
using CommonPOSLibrary.Enums;
using CommonPOSLibrary.Exceptions;

namespace Plutus.Frontend.AppClient.Tests.CommonPOSLibrary
{
    public class PrinterBaseOperationsTests
    {
        [Fact]
        public void ScoreReceipt_AddsScoreLine()
        {
            var printer = new PrinterBaseOperations();
            printer.ScoreReceipt();
            Assert.Equal("score...", printer.Lines.Single().Key);
        }

        [Fact]
        public void CutPaper_AddsCutLine()
        {
            var printer = new PrinterBaseOperations();
            printer.CutPaper();
            Assert.Equal("cut...", printer.Lines.Single().Key);
        }

        [Fact]
        public void WriteText_EncodesFormattingIntoKey()
        {
            var printer = new PrinterBaseOperations();
            printer.WriteText("Hello", "cntr", "true", "true");
            Assert.Equal("str.cntr.true.true", printer.Lines.Single().Key);
            Assert.Equal("Hello", printer.Lines.Single().Value);
        }

        [Fact]
        public void WriteBarcode_EncodesFormattingIntoKey()
        {
            var printer = new PrinterBaseOperations();
            printer.WriteBarcode("12345", "Code128", 50, "cntr", "abve");
            Assert.Equal("brc.Code128.cntr.50.abve", printer.Lines.Single().Key);
            Assert.Equal("12345", printer.Lines.Single().Value);
        }

        [Fact]
        public void WriteImage_EncodesContentAsUtf8String()
        {
            var printer = new PrinterBaseOperations();
            printer.WriteImage(Encoding.UTF8.GetBytes("imgdata"), "cntr", 100);
            Assert.Equal("img.cntr.100", printer.Lines.Single().Key);
            Assert.Equal("imgdata", printer.Lines.Single().Value);
        }

        [Fact]
        public void BlankLine_AddsEmptyTextLine()
        {
            var printer = new PrinterBaseOperations();
            printer.BlankLine();
            Assert.Equal("str...", printer.Lines.Single().Key);
            Assert.Equal("", printer.Lines.Single().Value);
        }

        [Fact]
        public void MultipleOperations_AccumulateInOrder()
        {
            var printer = new PrinterBaseOperations();
            printer.WriteText("Line 1");
            printer.ScoreReceipt();
            printer.CutPaper();
            Assert.Equal(3, printer.Lines.Count);
        }
    }

    public class ExceptionsTests
    {
        [Fact]
        public void POSObjectException_CarriesTypeInfo()
        {
            var ex = new POSObjectException(POSObjectExceptionType.NotFound, POSTargetObjectType.Printer, "not found");
            Assert.Equal(POSObjectExceptionType.NotFound, ex.POSObjectExceptionType);
            Assert.Equal(POSTargetObjectType.Printer, ex.POSTargetObjectType);
            Assert.Equal("not found", ex.Message);
        }

        [Fact]
        public void POSPrinterException_CarriesTypeInfo()
        {
            var ex = new POSPrinterException(POSPrinterExceptionType.PrinterNotClaimed, "not claimed");
            Assert.Equal(POSPrinterExceptionType.PrinterNotClaimed, ex.POSPrinterExceptionType);
            Assert.Equal("not claimed", ex.Message);
        }

        [Fact]
        public void POSManagerValueInvalidException_CarriesExpectedType()
        {
            var ex = new POSManagerValueInvalidException("wrong type", typeof(string));
            Assert.Equal(typeof(string), ex.ExpectedType);
            Assert.Equal("wrong type", ex.Message);
        }

        [Fact]
        public void POSManagerCommandNotSupportedException_IsAPOSManagerException()
        {
            Assert.IsAssignableFrom<POSManagerException>(new POSManagerCommandNotSupportedException("bad command"));
        }
    }
}
