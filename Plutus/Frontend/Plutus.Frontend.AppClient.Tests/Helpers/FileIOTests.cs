using System.Data;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Helpers.FileIO;

namespace Plutus.Frontend.AppClient.Tests.Helpers
{
    public class ParsingCSVTests
    {
        [Fact]
        public async Task GetDataFromCSV_DefaultDelimiter_SplitsOnComma()
        {
            using var stream = "a,b,c".ToStream();
            var result = await ParsingCSV.GetDataFromCSV(stream);
            Assert.Equal(new[] { "a", "b", "c" }, result);
        }

        [Fact]
        public async Task GetDataFromCSV_CustomDelimiter_SplitsOnGivenChar()
        {
            using var stream = "a&b&c".ToStream();
            var result = await ParsingCSV.GetDataFromCSV(stream, '&');
            Assert.Equal(new[] { "a", "b", "c" }, result);
        }

        [Fact]
        public async Task GetDataFromCSV_NoDelimiterPresent_ReturnsSingleElement()
        {
            using var stream = "no-delimiter-here".ToStream();
            var result = await ParsingCSV.GetDataFromCSV(stream);
            Assert.Equal(new[] { "no-delimiter-here" }, result);
        }
    }

    public class ExcelHandlingTests
    {
        [Fact]
        public void Finalize_ProducesValidSpreadsheetDocument()
        {
            using var excelHandling = new ExcelHandling();
            using var stream = excelHandling.Finalize();

            Assert.True(stream.Length > 0);
            using var document = SpreadsheetDocument.Open(stream, false);
            Assert.NotNull(document.WorkbookPart);
        }

        [Fact]
        public void DataTableToWorksheet_WritesColumnsAndRows()
        {
            var table = new DataTable("Sales");
            table.Columns.Add("Item");
            table.Columns.Add("Amount");
            table.Rows.Add("Widget", "10");
            table.Rows.Add("Gadget", "20");

            var dataSet = new DataSet();
            dataSet.Tables.Add(table);

            using var excelHandling = new ExcelHandling();
            excelHandling.DataTableToWorksheet(dataSet);
            using var stream = excelHandling.Finalize();

            using var document = SpreadsheetDocument.Open(stream, false);
            var sheets = document.WorkbookPart!.Workbook.Sheets!;
            Assert.Single(sheets.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>());
            Assert.Equal("Sales", sheets.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>().First().Name);
        }

        [Fact]
        public void DataTableToWorksheet_MultipleTables_AssignsIncrementingSheetIds()
        {
            var dataSet = new DataSet();
            var table1 = new DataTable("First");
            table1.Columns.Add("Col");
            var table2 = new DataTable("Second");
            table2.Columns.Add("Col");
            dataSet.Tables.Add(table1);
            dataSet.Tables.Add(table2);

            using var excelHandling = new ExcelHandling();
            excelHandling.DataTableToWorksheet(dataSet);
            using var stream = excelHandling.Finalize();

            using var document = SpreadsheetDocument.Open(stream, false);
            var sheetIds = document.WorkbookPart!.Workbook.Sheets!.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>()
                .Select(s => s.SheetId!.Value).ToList();
            Assert.Equal(new uint[] { 1, 2 }, sheetIds);
        }

        [Fact]
        public void Dispose_DoesNotThrow()
        {
            var excelHandling = new ExcelHandling();
            excelHandling.Finalize();
            var ex = Record.Exception(() => excelHandling.Dispose());
            Assert.Null(ex);
        }
    }
}
