using System.Text.RegularExpressions;
using Xunit;

namespace Plutus.Tests.Architecture;

/// <summary>
/// ⚠⚠ NO COLOUR LITERALS IN THE MAUI TILL — WP-T1 T1.4, 2026-08-19.
///
/// **This is the test that stops the theming work package recurring**, and it exists because
/// `XamlResourceTests` could not have caught any of it. That one scans XAML for resource KEYS; every
/// fault the 2026-08-19 audit found was a colour that never asked for a key at all:
///
///   • `DialogHeader`'s ✕ was `Colors.Black` — the close control D4 MANDATES, measuring **1.23:1** on
///     every dark dialog, so it was invisible on exactly the dialogs it exists for;
///   • `StoreOptionsViewModel` asked for `"Error"`, which is not one of the seven slots, so `Apply`
///     could never move it and it sat at **2.12:1** on dark;
///   • the tab bar carried icons hardcoded `Colors.Black` in a converter and `Colors.White` at two call
///     sites — on ONE bar, so at most one of them could be right.
///
/// ⚠ **IT SCANS VIEW-BUILDING C# AS WELL AS XAML.** Every one of those misses is in C#, which is why a
/// XAML-only guard passed while the screens were unthemed. This till builds most of its money surfaces
/// in code precisely because MAUI bindings fail silently, so the C# is where the colours are.
///
/// ⚠ THE ALLOW-LIST IS REASONED, NOT CONVENIENT. Each entry is a colour that MUST NOT follow a shop's
/// brand, and each says why. **If you are adding to it to make this test pass, you are probably
/// introducing the fault it was written for.**
/// </summary>
public class ThemeLiteralTests
{
    private const string AppClient = "Plutus/Frontend/Plutus.Frontend.AppClient";

    /// <summary>Named colours and hex, in XAML colour attributes and in C#.</summary>
    private static readonly Regex Literal = new(
        @"Colors\.(?<named>[A-Z][A-Za-z]+)"
        + @"|Color\.FromArgb\(""(?<argb>#[0-9a-fA-F]{3,8})"""
        + @"|(?:TextColor|BackgroundColor|BorderColor|Stroke|Fill|IconColor|Color)\s*=\s*""(?<xaml>#[0-9a-fA-F]{3,8}|[A-Z][A-Za-z]+)"""
        + @"|(?<binding>AppThemeBinding)",
        RegexOptions.Compiled);

    /// <summary>
    /// ⚠⚠ DELIBERATE, PERMANENT, AND SCOPED TO ONE FILE EACH. Keyed `file:literal`, so allowing the
    /// refund row's red does not hand out a licence to use red anywhere else — which a bare list of
    /// colours would.
    ///
    /// ⚠ EVERY ENTRY IS A REASON. The reasons are the point of the list.
    /// </summary>
    private static readonly HashSet<string> Deliberate = new(StringComparer.Ordinal)
    {
        // ⚠ SEMANTIC, NEVER BRANDED. A destructive control recoloured to match a shop's logo can be
        // made to look safe, and a refund row that stops looking like a refund is a money fault.
        "ConnectionView.xaml:#C1272D",      // "Forget this till"
        "ConnectionView.xaml:White",        // …and its ink, pinned against that fixed red
        "TillView.xaml:Red",                // the refund row — red MEANS refund
        "TillView.xaml:White",              // ⚠ THE PIN the audit asked for: fixed fill + moving ink is
                                            // unbounded contrast under a portal ink

        // ⚠ THE BUSY OVERLAY sits over ANY screen, including ones this app does not own, so its scrim
        // and its ink cannot be derived from a surface it does not know. It is also the component with
        // the worst failure mode in the app — an unreadable overlay is a stranded till.
        "LoadingIndicatorView.xaml:#80000000",
        "LoadingIndicatorView.xaml:Black",
        "LoadingIndicatorView.xaml:White",

        // ⚠ Fallbacks handed to `ThemeColour(key, fallback)` used to be listed here, file by file.
        // They are covered STRUCTURALLY now — see `WithoutComments` — which is a better guard: it
        // recognises the correct shape rather than enumerating the places somebody used it.

        // ⚠ INK ON PAPER. Print paths are deliberately immune to theming (till-design C1) — printing
        // from a dark scheme once put near-white ink on paper.
        "ReceiptRenderer.cs:Colors.Black",
        "ReceiptRenderer.cs:Colors.White",
    };

    /// <summary>
    /// ⚠⚠ THE T1.3 BACKLOG — AND IT IS EMPTY, which is the point of leaving it here.
    ///
    /// It held 13 entries for a few hours on 2026-08-19: status colours across `TillConnection`,
    /// `LoginViewModel`, `ConnectionViewModel`, `CashViewModel` and `NoticeboardViewModel` — a
    /// red/amber/green/grey that MEANT something (connected, degraded, revoked; a cash variance over or
    /// under; a notice's severity). They could not simply become `ThemeInk`, because the colour carries
    /// the meaning, and they must not become portal slots, because a shop could then paint "revoked" the
    /// same green as "connected" and an operator would trust a green dot on a till that was switched off.
    ///
    /// ✅ CLOSED by the STATUS TRIO — `ThemeGood` / `ThemeWarn` / `ThemeUnknown` beside `ThemeDanger`, as
    /// mode-only keys (`Theming.ModeOnlyKeys`) with a light and a dark value each. One value cannot serve
    /// both grounds: `#1b873f` is 4.6:1 on white and **2.6:1** on the dark surface, so a shop that chose
    /// dark was reading its connection status in a colour it could barely see.
    ///
    /// ⚠ THREE, not two: *"not known yet"* is a real third state, and rendering it as good or bad is a
    /// lie — a dot that shows red before the first probe reports a fault that has not happened.
    ///
    /// ⚠ **THE EMPTY SET STAYS.** It is the ratchet: the next status colour somebody reaches for has
    /// nowhere to hide, and the only way to add one is to argue for it here in writing.
    /// </summary>
    private static readonly HashSet<string> Backlog = new(StringComparer.Ordinal);

    [Fact]
    public void No_colour_literal_escapes_the_theme()
    {
        var offenders = new List<string>();

        foreach (var path in ThemedSourceFiles())
        {
            var text = WithoutComments(File.ReadAllText(path), path);
            var relative = Path.GetRelativePath(Repo.Root(), path);

            foreach (Match m in Literal.Matches(text))
            {
                var found = m.Groups["named"].Success ? "Colors." + m.Groups["named"].Value
                    : m.Groups["argb"].Success ? m.Groups["argb"].Value
                    : m.Groups["xaml"].Success ? m.Groups["xaml"].Value
                    : "AppThemeBinding";

                // ⚠ Keyed by FILE and literal, so an allowance is scoped to the one place it was
                // reasoned about rather than granted app-wide.
                // ⚠ TRANSPARENT IS NOT A COLOUR, it is the absence of one — whatever is behind shows
                // through, so it follows the theme by definition. Allowed anywhere.
                if (found == "Colors.Transparent") continue;

                // ⚠ Keyed by FILE and literal, so an allowance is scoped to the one place it was
                // reasoned about rather than granted app-wide.
                var key = $"{Path.GetFileName(path)}:{found}";
                if (Deliberate.Contains(key) || Backlog.Contains(key)) continue;

                var line = text.Take(m.Index).Count(c => c == '\n') + 1;
                offenders.Add($"{relative}:{line} — {found}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A colour that cannot follow the portal's theme:\n  "
            + string.Join("\n  ", offenders.Take(40))
            + $"\n({offenders.Count} total)\n\n"
            + "Use a ROLE — ThemeAccent / ThemeAccentInk / ThemeSurface / ThemeSurface2 / ThemeInk / "
            + "ThemeInkMuted / ThemeLine — or ThemeDanger for something that must NOT follow a brand. "
            + "If the colour genuinely has to be fixed, add it to this test's allow-list WITH THE REASON.");
    }

    /// <summary>
    /// ⚠⚠ THE DARK STOCK MUST MATCH THE WEB TILL'S. Matt, 2026-08-17: *"Changing the theme in the portal
    /// needs to be consistent across the web and MAUI till."* A shop set to dark that gets two different
    /// dark greys on its two tills has been given two brands, and nothing flags it.
    ///
    /// ⚠ Asserted as LITERALS on purpose: the point is that changing one side fails HERE rather than
    /// diverging quietly.
    /// </summary>
    [Fact]
    public void The_dark_stock_palette_matches_the_web_tills()
    {
        var theming = File.ReadAllText(Path.Combine(
            Repo.Root(), AppClient.Replace('/', Path.DirectorySeparatorChar),
            "Services", "Theming", "Theming.cs"));

        var expected = new[]
        {
            ("ThemeSurface", "#161d26"),
            ("ThemeSurface2", "#212b38"),
            ("ThemeInk", "#eef2f6"),
            ("ThemeInkMuted", "#9aa7b4"),
            ("ThemeLine", "#33404f"),
        };

        foreach (var (slot, hex) in expected)
            Assert.Contains($"[\"{slot}\"] = Color.FromArgb(\"{hex}\")", theming);
    }

    /// <summary>
    /// Blank out comments, keeping the line count so reported line numbers stay true.
    ///
    /// ⚠⚠ **WITHOUT THIS THE TEST IS WRONG, AND WRONG IN THE WORST DIRECTION.** This codebase documents
    /// what it fixed — *"`Colors.LightGray` WAS HARD-CODED HERE, and it is the same fault that…"* — so a
    /// scanner that reads comments flags the very notes that record the repair, and the cheapest way to
    /// make it pass is to DELETE the explanation. That would trade a real asset for a green tick.
    ///
    /// ⚠ Newlines are preserved rather than the comment being removed, so `:42` still means line 42.
    /// </summary>
    private static string WithoutComments(string text, string path)
    {
        MatchEvaluator blank = m => Regex.Replace(m.Value, @"[^\n]", " ");

        if (path.EndsWith(".xaml", StringComparison.Ordinal))
            return Regex.Replace(text, @"<!--.*?-->", blank, RegexOptions.Singleline);

        // ⚠⚠ A `ThemeColour("Role", fallback)` CALL IS THE CORRECT PATTERN, so its fallback is not a
        // leak — it is reached only when the resource is missing, which is the one case a themed value
        // cannot help with. Blanking the whole call is a STRUCTURAL rule and it replaced thirteen
        // file-by-file exceptions: a rule that recognises the right shape is a better guard than a list
        // of the places somebody used it.
        text = Regex.Replace(text, @"ThemeColour\(\s*""[A-Za-z]+""\s*,[^;,)]*\)?", blank);

        // ⚠ Line comments first, then block comments. Not a C# parser — a colour literal inside a STRING
        // would still be flagged, which is the right way round: a hex in a string is usually a colour.
        var noLine = Regex.Replace(text, @"//[^\n]*", blank);
        return Regex.Replace(noLine, @"/\*.*?\*/", blank, RegexOptions.Singleline);
    }

    /// <summary>
    /// The files a theme is expected to reach: the till's XAML, plus the C# that BUILDS views.
    ///
    /// ⚠ DEAD SCREENS ARE EXCLUDED, deliberately — `AddEditView`, the L4 statistics screens and the
    /// legacy `SetupView` are L2/L4's to delete, and painting dead code is how it starts looking
    /// maintained.
    /// ⚠ `Colors.xaml` is excluded because it IS the palette, and this file because it lists the
    /// literals it forbids.
    /// </summary>
    private static IEnumerable<string> ThemedSourceFiles()
    {
        var root = Path.Combine(Repo.Root(), AppClient.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(Directory.Exists(root), $"Expected the MAUI till at {root}.");

        // ⚠ MATCHED ON THE PATH, NOT THE FILE NAME. `Statistics` is a FOLDER — `SalesReportsViewModel.cs`
        // lives inside it — so a filename test silently let four of the L4 screens' literals through and
        // then reported them as offenders. Painting dead code is how it starts looking maintained.
        var dead = new[]
        {
            $"{Path.DirectorySeparatorChar}Statistics{Path.DirectorySeparatorChar}",
            "AddEditView",
            "SetupView",
        };

        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".xaml", StringComparison.Ordinal)
                     || p.EndsWith(".cs", StringComparison.Ordinal))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(p => !p.EndsWith("Colors.xaml", StringComparison.Ordinal))
            // ⚠ `Theming.cs` IS the palette on the C# side — `DarkStock` holds the dark half of every
            // stock value, and those literals are the thing this test protects rather than a leak of it.
            // `The_dark_stock_palette_matches_the_web_tills` is what pins them.
            .Where(p => !p.EndsWith("Theming.cs", StringComparison.Ordinal))
            .Where(p => !dead.Any(d => p.Contains(d, StringComparison.Ordinal)));
    }
}
