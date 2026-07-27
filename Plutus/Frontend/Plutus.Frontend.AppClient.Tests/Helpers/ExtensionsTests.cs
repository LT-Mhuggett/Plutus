using System.Data;
using System.Globalization;
using Plutus.Frontend.AppClient.Helpers.Extensions;

namespace Plutus.Frontend.AppClient.Tests.Helpers
{
    public class DateTimeExtensionsTests
    {
        [Fact]
        public void StartOfWeek_ReturnsDateOnly_AtCultureFirstDayOfWeek()
        {
            using var _ = new CultureScope("en-GB"); // FirstDayOfWeek = Monday
            var wednesday = new DateTime(2024, 1, 10, 15, 30, 0); // a Wednesday
            var monday = wednesday.StartOfWeek();

            Assert.Equal(new DateTime(2024, 1, 8), monday);
            Assert.Equal(TimeSpan.Zero, monday.TimeOfDay);
        }

        [Fact]
        public void StartOfWeek_OnFirstDayOfWeek_ReturnsSameDate()
        {
            using var _ = new CultureScope("en-GB");
            var monday = new DateTime(2024, 1, 8);
            Assert.Equal(monday, monday.StartOfWeek());
        }
    }

    public class DecimalExtensionsTests
    {
        // Normalize() divides by "1" followed by as many zeros as the value's own decimal scale
        // (e.g. scale 2 -> divide by 1.00), which is a mathematical no-op on the value itself - its
        // real effect is on the decimal's internal scale/precision, which Assert.Equal (value equality,
        // not representation equality) doesn't observe. Documents that Normalize is value-preserving.
        [Theory]
        [InlineData("1.50")]
        [InlineData("100")]
        [InlineData("12.345")]
        [InlineData("0")]
        [InlineData("-42.7")]
        public void Normalize_IsValuePreserving(string literal)
        {
            var value = decimal.Parse(literal, CultureInfo.InvariantCulture);
            Assert.Equal(value, value.Normalize());
        }
    }

    public class IEnumerableExtensionsTests
    {
        [Fact]
        public void ForEach_InvokesActionForEveryItem()
        {
            var seen = new List<int>();
            new[] { 1, 2, 3 }.ForEach(seen.Add);
            Assert.Equal(new[] { 1, 2, 3 }, seen);
        }

        [Fact]
        public void ForEachLazy_InvokesActionAndYieldsSameItems()
        {
            var seen = new List<int>();
            var result = new[] { 1, 2, 3 }.ForEachLazy(seen.Add).ToList();

            Assert.Equal(new[] { 1, 2, 3 }, seen);
            Assert.Equal(new[] { 1, 2, 3 }, result);
        }

        private class Row
        {
            public string Name { get; set; } = "";
            public int Age { get; set; }
        }

        [Fact]
        public void ToDataTable_Generic_UsesTypeNameAndPublicProperties()
        {
            var rows = new[] { new Row { Name = "Alice", Age = 30 }, new Row { Name = "Bob", Age = 25 } };
            DataTable table = rows.ToDataTable();

            Assert.Equal(nameof(Row), table.TableName);
            Assert.Equal(new[] { "Name", "Age" }, table.Columns.Cast<DataColumn>().Select(c => c.ColumnName));
            Assert.Equal(2, table.Rows.Count);
            Assert.Equal("Alice", table.Rows[0]["Name"]);
            // Columns.Add(name) with no explicit type defaults DataType to string, so numeric values
            // get implicitly coerced to their string form when the row is added.
            Assert.Equal("25", table.Rows[1]["Age"]);
        }

        [Fact]
        public void ToDataTable_Generic_WithTableName_UsesGivenName()
        {
            var table = new[] { new Row { Name = "Alice", Age = 30 } }.ToDataTable("People");
            Assert.Equal("People", table.TableName);
        }

        [Fact]
        public void ToDataTable_FromDictionaries_UsesFirstItemKeysAsColumns()
        {
            var rows = new List<IDictionary<string, object>>
            {
                new Dictionary<string, object> { ["Id"] = 1, ["Label"] = "One" },
                new Dictionary<string, object> { ["Id"] = 2, ["Label"] = "Two" },
            };

            var table = rows.ToDataTable("Numbers");

            Assert.Equal("Numbers", table.TableName);
            Assert.Equal(new[] { "Id", "Label" }, table.Columns.Cast<DataColumn>().Select(c => c.ColumnName));
            Assert.Equal(2, table.Rows.Count);
            // Columns.Add(name) with no explicit type defaults DataType to string.
            Assert.Equal("1", table.Rows[0]["Id"]);
            Assert.Equal("Two", table.Rows[1]["Label"]);
        }
    }

    public class StringExtensionsTests
    {
        [Theory]
        [InlineData("12345", true)]
        [InlineData("", true)]
        [InlineData("123a5", false)]
        [InlineData("-1", false)]
        public void IsNumeric_Check(string value, bool expected)
        {
            Assert.Equal(expected, value.IsNumeric());
        }

        // .NET's own string.Contains(string, StringComparison) instance method (added well after this
        // extension was written) has an identical signature and always wins overload resolution over
        // an extension method, so `source.Contains(...)` never actually reaches
        // StringExtensions.Contains - it's unreachable via normal call syntax in modern .NET. Calling
        // it as an explicit static method is the only way to exercise the intended implementation.
        [Theory]
        [InlineData("Hello World", "world", StringComparison.OrdinalIgnoreCase, true)]
        [InlineData("Hello World", "world", StringComparison.Ordinal, false)]
        [InlineData("Hello World", "xyz", StringComparison.OrdinalIgnoreCase, false)]
        public void Contains_WithComparer(string source, string toCheck, StringComparison comparison, bool expected)
        {
            Assert.Equal(expected, StringExtensions.Contains(source, toCheck, comparison));
        }

        [Fact]
        public void Contains_NullSource_ReturnsFalse()
        {
            Assert.False(StringExtensions.Contains(null!, "x", StringComparison.Ordinal));
        }

        [Fact]
        public void ToStream_RoundTripsOriginalText()
        {
            using var stream = "hello world".ToStream();
            using var reader = new StreamReader(stream);
            Assert.Equal("hello world", reader.ReadToEnd());
        }

        [Fact]
        public void Translate_UnknownKey_ThrowsInDebugBuilds()
        {
            // TranslateExtension only falls back to returning the key itself in Release builds;
            // in Debug (this test config) a missing resource throws ArgumentException instead.
            var ex = Record.Exception(() => "DefinitelyNotARealTranslationKey12345".Translate());
            Assert.IsType<ArgumentException>(ex);
        }

        [Fact]
        public void Translate_KnownKey_ReturnsTranslatedValue()
        {
            Assert.Equal("The {0} is not valid", "NotValid".Translate());
        }

        [Fact]
        public void Translate_WithFormatArgs_FormatsResult()
        {
            Assert.Equal("The Email is not valid", "NotValid".Translate(new[] { "Email" }));
        }
    }
}
