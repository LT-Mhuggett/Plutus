using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Till
{
    /// <summary>
    /// **The selling screen's rows — one thing per row, and the basket gets the one that stretches.**
    ///
    /// ⚠⚠ WRITTEN AFTER BREAKING IT, 2026-08-23. Moving the Loyalty lookup onto its own row pushed
    /// every row below it down by one. The basket list was renumbered; **the totals-and-buttons block
    /// was not**. Two children then drew on the same row: the basket area collapsed to nothing, the
    /// action buttons rode up under the scan box, and the row that should have held them was left as a
    /// large empty band at the bottom. Matt: *"The till view is now broken... The till buttons,
    /// discount, save transaction are now stuck at the top?"*
    ///
    /// ⚠ MAUI GIVES NO WARNING FOR THIS. Two children on one Grid row is legal — it is how you
    /// deliberately overlay things — so there is no error, no binding failure and no log line. It is
    /// visible only by looking at the screen, and this repo has no automated coverage of any MAUI
    /// screen. A source-text pin is crude, and it is the only thing available.
    ///
    /// ⚠ THE RULE IT ENFORCES IS THE ONE THE PAGE ALREADY DOCUMENTS: *"Grid.Row=2 is the star row —
    /// the ONLY thing on this screen allowed to give up height. The list scrolls; the buttons below
    /// never move."* That is the layout contract for every till: the buttons are always present, and
    /// the item area is what shrinks and scrolls.
    /// </summary>
    public class TillLayoutTests
    {
        private static string RepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !Directory.Exists(Path.Combine(dir, ".git")))
                dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            return dir!;
        }

        private static string Xaml() => File.ReadAllText(Path.Combine(
            RepoRoot(), "Plutus", "Frontend", "Plutus.Frontend.AppClient",
            "Views", "MainTill", "Till", "TillView.xaml"));

        /// <summary>The outer grid — the one whose RowDefinitions are written inline on the tag.</summary>
        private static string[] OuterRows()
        {
            var m = Regex.Match(Xaml(), @"<Grid RowDefinitions=""([^""]+)""");
            Assert.True(m.Success, "The selling screen's outer Grid no longer declares inline RowDefinitions.");
            return m.Groups[1].Value.Split(',').Select(s => s.Trim()).ToArray();
        }

        /// <summary>Every `Grid.Row="n"` on a child indented at the outer grid's own level.</summary>
        private static (string element, int row)[] TopLevelChildren()
        {
            // ⚠ Anchored on the indentation the outer grid's direct children sit at (16 spaces). A
            // nested grid's children are deeper, and matching those would make every inner layout
            // look like a collision.
            var matches = Regex.Matches(Xaml(), @"^ {16}<(\w+)[^>]*?Grid\.Row=""(\d+)""",
                RegexOptions.Multiline | RegexOptions.Singleline);

            return matches.Select(m => (m.Groups[1].Value, int.Parse(m.Groups[2].Value))).ToArray();
        }

        /// <summary>⚠ THE COLLISION. This is the bug, stated directly.</summary>
        [Fact]
        public void No_two_children_of_the_selling_screen_share_a_row()
        {
            var children = TopLevelChildren();
            Assert.NotEmpty(children);

            var clashes = children
                .GroupBy(c => c.row)
                .Where(g => g.Count() > 1)
                .Select(g => $"row {g.Key}: {string.Join(", ", g.Select(c => c.element))}")
                .ToList();

            Assert.True(clashes.Count == 0,
                "Two children of the selling screen are on the same Grid row. MAUI draws them on top of "
                + "each other with no warning — the basket area collapses and the action buttons ride up "
                + "under the scan box.\n\n"
                + string.Join("\n", clashes));
        }

        /// <summary>
        /// ⚠ THE BASKET GETS THE STRETCHY ROW. If the star row ends up under something fixed-height,
        /// the list has nowhere to grow and the page's own contract — "the list scrolls; the buttons
        /// below never move" — is silently inverted.
        /// </summary>
        [Fact]
        public void The_basket_list_is_on_the_row_that_stretches()
        {
            var rows = OuterRows();
            var star = Array.FindIndex(rows, r => r == "*");
            Assert.True(star >= 0, $"The selling screen has no star row: {string.Join(",", rows)}");

            var list = TopLevelChildren().FirstOrDefault(c => c.element == "ListView");
            Assert.True(list.element is not null, "No ListView among the selling screen's rows.");

            Assert.True(list.row == star,
                $"The basket list is on row {list.row} but the star row is {star}. The list must be the "
                + "one thing allowed to give up height — everything else is Auto and must not move.");
        }

        /// <summary>⚠ And the declared rows must cover everything placed in them.</summary>
        [Fact]
        public void Every_row_used_actually_exists()
        {
            var declared = OuterRows().Length;
            foreach (var (element, row) in TopLevelChildren())
                Assert.True(row < declared,
                    $"<{element}> is on row {row}, but the selling screen declares only {declared} rows "
                    + "(0-{declared - 1}). MAUI clamps it into the last row instead of erroring.");
        }
    }
}
