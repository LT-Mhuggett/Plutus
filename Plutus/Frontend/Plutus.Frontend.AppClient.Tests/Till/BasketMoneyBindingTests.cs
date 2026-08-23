using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Plutus.Frontend.AppClient.Models;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Till
{
    /// <summary>
    /// ⚠⚠ THE £330.00 TRAP, PINNED IN THE XAML (step 11b).
    ///
    /// The basket stores money as integer **pence** (`PricePence`) with a `decimal` **pounds** view
    /// (`Price`) over it. Every basket row renders money as
    /// <c>{Binding X, StringFormat='{0:C}'}</c> — and `{0:C}` on a `long` of 330 produces
    /// **£330.00**, not £3.30. Nothing throws, no test fails, and the receipt is the only evidence.
    /// It is a hundredfold money error that looks like a working till.
    ///
    /// `Test Maui.md` §G24 exists because that failure can only be seen on a screen. This is the half
    /// a machine *can* check: that no currency-formatted binding is pointed at a pence field. It does
    /// not replace the hand-run — a correct binding can still be in the wrong column — but it removes
    /// the specific way this comes back, which is somebody "tidying" `Price` into `PricePence`.
    /// </summary>
    public class BasketMoneyBindingTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Plutus.slnx")))
                dir = dir.Parent;

            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static string TillViewXaml() => File.ReadAllText(Path.Combine(
            RepoRoot(), "Plutus", "Frontend", "Plutus.Frontend.AppClient",
            "Views", "MainTill", "Till", "TillView.xaml"));

        /// <summary>
        /// ⚠⚠ NO CURRENCY-FORMATTED BINDING MAY POINT AT A PENCE FIELD. This is the whole test: a
        /// `long` of 330 formatted `{0:C}` renders £330.00 and every total still balances.
        /// </summary>
        [Fact]
        public void No_money_binding_formats_a_pence_field_as_currency()
        {
            var bound = Regex.Matches(TillViewXaml(), @"\{Binding\s+(\w+),\s*StringFormat='\{0:C\}'\}")
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .ToList();

            // ⚠ If this is empty the regex has rotted and a green run would mean nothing.
            Assert.NotEmpty(bound);

            var inPence = bound.Where(n => n.EndsWith("Pence", StringComparison.Ordinal)).ToList();

            Assert.True(inPence.Count == 0,
                $"TillView.xaml formats {{{string.Join(", ", inPence)}}} as currency, but those hold "
                + "PENCE — {0:C} would render 330 as £330.00 rather than £3.30, silently.");
        }

        /// <summary>
        /// ⚠ And every currency binding must resolve to a **decimal** on the basket record. A `long`
        /// there is the same bug wearing a different name, and `IBasketRecord` is the contract all
        /// three row templates bind against.
        /// </summary>
        [Fact]
        public void Every_currency_binding_resolves_to_a_decimal_on_the_basket_record()
        {
            var bound = Regex.Matches(TillViewXaml(), @"\{Binding\s+(\w+),\s*StringFormat='\{0:C\}'\}")
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .ToList();

            Assert.NotEmpty(bound);

            foreach (var name in bound)
            {
                var prop = typeof(IBasketRecord).GetProperty(name)
                        ?? typeof(BasketItem).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

                Assert.True(prop is not null,
                    $"TillView.xaml binds '{name}' as currency, but neither IBasketRecord nor "
                    + "BasketItem has it — MAUI renders that BLANK and never throws.");

                Assert.True(prop!.PropertyType == typeof(decimal),
                    $"'{name}' is {prop.PropertyType.Name}, not decimal. A currency format on an "
                    + "integer pence value renders a hundredfold error that nothing detects.");
            }
        }

        private static global::Plutus.Frontend.AppClient.Models.TillItem AnItem() => new()
        {
            Id = "1",
            Name = "Widget",
            Price = 10m,
            ExPrice = 8m,
            VatName = "Standard",
        };

        /// <summary>
        /// ⚠⚠ AND THE PROJECTION ITSELF STILL DIVIDES. The binding being correctly aimed is worthless
        /// if `Price` stops being pounds — so pin the one conversion the whole trap rests on.
        /// </summary>
        [Fact]
        public void The_pounds_view_is_a_hundredth_of_the_pence_store()
        {
            var item = new BasketItem(AnItem()) { PricePence = 330 };

            Assert.Equal(3.30m, item.Price);
            Assert.NotEqual(330m, item.Price);   // the £330.00 reading
        }

        /// <summary>⚠ And round-trips, because the setter is what legacy callers still use.</summary>
        [Theory]
        [InlineData(3.30, 330)]
        [InlineData(0.01, 1)]
        [InlineData(1234.56, 123456)]
        public void Setting_pounds_stores_the_right_pence(double pounds, long expectedPence)
        {
            var item = new BasketItem(AnItem()) { Price = (decimal)pounds };

            Assert.Equal(expectedPence, item.PricePence);
            Assert.Equal((decimal)pounds, item.Price);
        }
    }
}
