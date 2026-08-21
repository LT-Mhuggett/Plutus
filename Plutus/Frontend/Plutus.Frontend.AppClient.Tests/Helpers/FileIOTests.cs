using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Helpers.FileIO;

// ⚠ `ExcelHandlingTests` (4 tests) was deleted here on 2026-08-20 along with the class it covered.
// `Helpers/FileIO/ExcelHandling.cs` was the till's `DocumentFormat.OpenXml` spreadsheet writer, called
// only by the legacy `SalesReportsViewModel` — and both went with the Syncfusion/OpenXml removal, which
// took ~91 MB (35%) off the publish. Nothing else referenced it. The CSV tests below are unrelated and
// stay: `ParsingCSV` is still used.
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
}
