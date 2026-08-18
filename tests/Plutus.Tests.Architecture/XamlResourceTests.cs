using System.Text.RegularExpressions;
using Xunit;

namespace Plutus.Tests.Architecture;

/// <summary>
/// Every resource the MAUI till's XAML asks for must actually exist.
///
/// ⚠⚠ **THIS TEST EXISTS BECAUSE TWO STYLES WERE REFERENCED TWENTY TIMES AND DEFINED NOWHERE.** Every
/// label on every basket row carried `Style="{DynamicResource ListItemDetailTextStyle}"`, and there was
/// no such key in the application — so the money screen's rows were **unstyled for as long as those
/// templates have existed**, and nobody could have noticed by reading the code.
///
/// ⚠ **THE REASON IT WAS INVISIBLE IS THE REASON THIS TEST IS WORTH HAVING.** `DynamicResource` to a
/// missing key does not throw, does not warn and does not log: it silently applies nothing. `dotnet
/// build` is clean, XamlC is clean, and the screen renders — just with platform defaults instead of
/// what the author asked for. It is the same silent-failure class as a MAUI binding to a property that
/// does not exist, which this codebase has been bitten by repeatedly.
///
/// ⚠ It is also the guard on **theming**. MAUI's screens consume the portal's colours through these
/// keys (§5c item 10); a slot name typed slightly wrong would leave a screen quietly unthemed while
/// looking finished.
/// </summary>
public class XamlResourceTests
{
    private const string AppClient = "Plutus/Frontend/Plutus.Frontend.AppClient";

    /// <summary>`{DynamicResource Foo}` / `{StaticResource Foo}` — the key only.</summary>
    private static readonly Regex Reference =
        new(@"\{(?:Dynamic|Static)Resource\s+([A-Za-z0-9_]+)\s*\}", RegexOptions.Compiled);

    private static readonly Regex Definition =
        new(@"x:Key\s*=\s*""([A-Za-z0-9_]+)""", RegexOptions.Compiled);

    private static IEnumerable<string> XamlFiles()
    {
        var root = Path.Combine(Repo.Root(), AppClient.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(Directory.Exists(root), $"Expected the MAUI till at {root}.");

        return Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
    }

    [Fact]
    public void Every_resource_the_xaml_asks_for_is_defined_somewhere()
    {
        var files = XamlFiles().ToList();

        // ⚠ A guard on the guard. If the glob ever stops matching — a moved project, a renamed folder —
        // this test would pass by examining nothing at all, which is the failure mode of every
        // convention test written without one.
        Assert.True(files.Count > 10, $"Only found {files.Count} XAML files; the search is wrong.");

        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
            foreach (Match m in Definition.Matches(File.ReadAllText(file)))
                defined.Add(m.Groups[1].Value);

        // ⚠ Sanity: the seven theme slots are the ones this must never fail to see.
        Assert.Contains("ThemeInk", defined);

        var missing = new List<string>();

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Reference.Matches(text))
            {
                var key = m.Groups[1].Value;

                // ⚠ Keys the PLATFORM provides, which will never appear as an `x:Key` in our source.
                // Keep this list short and justified — every entry is a thing this test stops checking.
                if (key is "MaterialIconConverter") continue;

                if (!defined.Contains(key))
                    missing.Add($"{Path.GetFileName(file)} → {{DynamicResource {key}}}");
            }
        }

        Assert.True(missing.Count == 0,
            "XAML asks for resources that do not exist. `DynamicResource` to a missing key applies " +
            "NOTHING, silently — the build stays clean and the screen renders with platform defaults:"
            + Environment.NewLine + string.Join(Environment.NewLine, missing.Distinct().OrderBy(x => x)));
    }

    /// <summary>
    /// ⚠⚠ A THEME SLOT MUST BE REACHED BY `DynamicResource`, NEVER `StaticResource`.
    ///
    /// `StaticResource` resolves **once, at parse time**, so a control would keep whatever palette was
    /// current when its page was built — and a scheme applied afterwards would move some controls and
    /// leave others. **Half a themed screen is worse than none**, which is the hazard `Theming.cs`'s
    /// own header records.
    ///
    /// ⚠ `Colors.xaml` is exempt: it *defines* the slots, and the brushes it builds from them are
    /// rebuilt wholesale by `Theming.Apply` on every change.
    /// </summary>
    [Fact]
    public void Theme_slots_are_never_reached_by_StaticResource()
    {
        var slots = new[]
        {
            "ThemeAccent", "ThemeAccentInk", "ThemeSurface", "ThemeSurface2",
            "ThemeInk", "ThemeInkMuted", "ThemeLine",
        };

        var offenders = new List<string>();

        foreach (var file in XamlFiles())
        {
            if (Path.GetFileName(file).Equals("Colors.xaml", StringComparison.OrdinalIgnoreCase)) continue;

            var text = File.ReadAllText(file);

            foreach (Match m in new Regex(@"\{StaticResource\s+([A-Za-z0-9_]+)\s*\}").Matches(text))
            {
                var key = m.Groups[1].Value;
                // ⚠ The brushes too — `ThemeAccentBrush` is a slot by another name.
                if (slots.Any(s => key == s || key == s + "Brush"))
                    offenders.Add($"{Path.GetFileName(file)} → {{StaticResource {key}}}");
            }
        }

        Assert.True(offenders.Count == 0,
            "A theme slot reached by StaticResource is frozen at parse time and will not follow a "
            + "scheme change — use DynamicResource:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders.Distinct()));
    }
}
