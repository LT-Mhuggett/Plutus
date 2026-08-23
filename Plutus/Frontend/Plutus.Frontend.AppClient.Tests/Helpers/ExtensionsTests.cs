using System.Data;
using System.Globalization;
using Plutus.Frontend.AppClient.Helpers.Extensions;

namespace Plutus.Frontend.AppClient.Tests.Helpers
{
    /// <summary>
    /// ⚠ ONE TEST LEFT — L14, 2026-08-23. This class also covered `ForEachLazy` and three
    /// `ToDataTable` overloads, all deleted with the methods: `ToDataTable`'s only consumer was
    /// `ExcelHandling.cs` (gone 2026-08-21 with Syncfusion), and `ForEachLazy` never had one.
    ///
    /// ⚠⚠ THE TESTS WENT WITH THE CODE, DELIBERATELY. Green tests over code nothing runs are how dead
    /// code comes to look maintained — the same shape as L12's `ParsingCSV` and L15's three
    /// converters, which were kept alive by nothing but their own coverage.
    /// </summary>
    public class IEnumerableExtensionsTests
    {
        /// <summary>⚠ `ForEach` STAYS: its live caller holds an `IList<T>`, which has no instance
        /// `ForEach`, so this extension is what resolves there.</summary>
        [Fact]
        public void ForEach_InvokesActionForEveryItem()
        {
            var seen = new List<int>();
            new[] { 1, 2, 3 }.ForEach(seen.Add);
            Assert.Equal(new[] { 1, 2, 3 }, seen);
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
