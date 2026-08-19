using System.Text.RegularExpressions;
using Xunit;

namespace Plutus.Tests.Architecture;

/// <summary>
/// ⚠⚠ NO SOURCE FILE MAY CONTAIN RE-ENCODED UTF-8.
///
/// **Why this exists.** On 2026-08-11, commit `c93e0fc2` edited three files through a pipe that read
/// their bytes as Latin-1/cp1252 and re-encoded them as UTF-8. Every `⚠` in the biggest till viewmodel
/// became `ÃÂ¢ÃÂÃÂ ` and stayed that way for **eight days**, because nothing in the build had an
/// opinion about it. Matt found it at a counter: scanning an unknown barcode produced
/// *"Nothing in the catalogue matches ÃÂ¢ÃÂÃÂ759606210602ÃÂ¢ÃÂÃÂ."* on a customer-facing screen.
///
/// ⚠⚠ **IT WAS ALSO TRIAGED, AND THE TRIAGE WAS WRONG** — which is the real reason this is a test and
/// not a note. The handover of 2026-08-17 recorded it as *"Comments only, no behavioural effect, and
/// confined to that one file (checked every `.cs` and `.xaml` in the repo)"*. All three of those were
/// false: 14 of the 206 lines were code, at least six were strings a shopkeeper reads, and two OTHER
/// files were corrupt. A human eye cannot audit this reliably — the corruption is invisible in most
/// editors and renders as plausible-looking noise in the rest.
///
/// ⚠ **The repair is not a reversal loop.** The corruption depth VARIED between 1, 2 and 3 rounds
/// inside a single file, and "reverse while a C1 control remains" stops one round early for any
/// character whose own UTF-8 is `C2`/`C3 xx` — `U+00B7 MIDDLE DOT` corrupted three times passes through
/// `C3 82 C2 B7`, which holds no C1 control and is still wrong. A blanket reversal also destroys
/// CORRECT characters: a lone `C2 A3` is a pound sign and `C3 97` a multiplication sign, both of which
/// appear in this repo legitimately. The fix enumerated each (character, depth) pair by FORWARD
/// simulation and replaced those exact byte strings, longest first.
///
/// Scanned as raw BYTES on disk — not as decoded text, which is the point: a `StreamReader` would hide
/// exactly what this looks for.
/// </summary>
public class SourceEncodingTests
{
    /// <summary>Text file types worth policing. Deliberately excludes every binary format.</summary>
    private static readonly string[] TextExtensions =
    {
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".xaml", ".resx", ".json", ".md",
        ".csproj", ".slnx", ".props", ".targets", ".html", ".css", ".yml", ".yaml", ".sh", ".sql",
    };

    /// <summary>
    /// Directories that are not source: build output, dependencies, and the packaged app under
    /// `Publishing/` (msix/zip/cer, which legitimately contain arbitrary bytes).
    /// </summary>
    private static readonly string[] ExcludedSegments =
    {
        $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}Publishing{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}",
    };

    /// <summary>
    /// ⚠ Files allowed to contain the mojibake byte patterns because they DESCRIBE them. Quoting what
    /// the corruption looks like is how the next person recognises it; "fixing" these would delete the
    /// evidence. Keep this list to documentation only — never source.
    /// </summary>
    private static readonly Dictionary<string, string> DocumentsTheCorruption = new()
    {
        [Path.Combine("Build", "archive", "handover-history-to-2026-08-17.md")] =
            "Quotes `ÃÂ¢ÃÂÃÂ` verbatim while recording that the corruption was noticed on 2026-08-17 — and "
            + "wrongly dismissed as comments-only. The quotation is the evidence; it stays.",
        [Path.Combine("Build", "repo-runbook.md")] =
            "Pitfall 20 shows what the corruption looks like and how to detect it.",
        [Path.Combine("Build", "Test Maui.md")] =
            "§G52 quotes the garbled dialog verbatim so the person hand-running the till can recognise it "
            + "on screen — 'if you see any run of Ã, Â, ¢ around the number, the build is older than "
            + "1.97.0'. A description alone would not let them match what they are looking at. ⚠ Added "
            + "2026-08-19 after this very test caught §G52 minutes after it was written, which is the "
            + "test working: the doc commit had not re-run this suite.",
        [Path.Combine("tests", "Plutus.Tests.Architecture", "SourceEncodingTests.cs")] =
            "This file: the doc comment above quotes the corrupted form on purpose.",
    };

    private static IEnumerable<string> TextFiles()
    {
        var root = Repo.Root();
        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(p => TextExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
            .Where(p => !ExcludedSegments.Any(p.Contains))
            .Where(p => !DocumentsTheCorruption.Keys.Any(
                allowed => p.EndsWith(allowed, StringComparison.OrdinalIgnoreCase)));
    }

    private static string Relative(string path) =>
        Path.GetRelativePath(Repo.Root(), path);

    /// <summary>
    /// ⚠⚠ THE PRIMARY DETECTOR. A C1 control (U+0080–U+009F, encoded `C2 80`–`C2 9F`) is the
    /// unambiguous fingerprint of UTF-8 bytes having been read as Latin-1 and re-encoded: legitimate
    /// text never contains one. Zero false positives by construction.
    /// </summary>
    [Fact]
    public void No_source_file_contains_a_C1_control_character()
    {
        var offenders = new List<string>();

        foreach (var file in TextFiles())
        {
            var bytes = File.ReadAllBytes(file);
            var hits = 0;

            for (var i = 0; i < bytes.Length - 1; i++)
                if (bytes[i] == 0xC2 && bytes[i + 1] >= 0x80 && bytes[i + 1] <= 0x9F)
                    hits++;

            if (hits > 0) offenders.Add($"{Relative(file)} — {hits} occurrence(s)");
        }

        Assert.True(offenders.Count == 0,
            "Re-encoded UTF-8 found. These bytes render as ÃÂ¢ÃÂÃÂ-style noise, and if any of it sits "
            + "in a string literal a shopkeeper reads it on the shop floor.\n  "
            + string.Join("\n  ", offenders)
            + "\n\n  Do NOT repair with a reverse-while-mangled loop: the depth varies and it will stop "
            + "early on Latin-1-range characters and destroy correct ones. Enumerate the distinct "
            + "sequences, decode each by forward simulation, and replace exactly — see this class's docs.");
    }

    /// <summary>
    /// ⚠ THE SECOND DETECTOR, for the class the first one CANNOT see. Double-encoding leaves no C1
    /// control behind when the original character's own bytes avoid that range — a byte-order mark
    /// (`EF BB BF`) double-encoded becomes `Ã¯Â»Â¿`, and an accented letter behaves the same way. One of
    /// the three corrupted files hid exactly this at line 1, ahead of its first `using`.
    ///
    /// ⚠ `Ã` or `Â` IMMEDIATELY followed by another high byte is the signature. A lone `Â£` or `×` is
    /// legitimate and does not match, which is why the pattern requires the pair.
    /// </summary>
    [Fact]
    public void No_source_file_contains_double_encoded_UTF8_without_a_C1_control()
    {
        var offenders = new List<string>();

        foreach (var file in TextFiles())
        {
            var bytes = File.ReadAllBytes(file);
            var hits = 0;

            for (var i = 0; i < bytes.Length - 2; i++)
            {
                // C3 82 = "Â", C3 83 = "Ã" — the mojibake of a 2-byte character, followed by the start
                // of another mangled sequence.
                var isAHat = bytes[i] == 0xC3 && (bytes[i + 1] == 0x82 || bytes[i + 1] == 0x83);
                if (isAHat && (bytes[i + 2] == 0xC2 || bytes[i + 2] == 0xC3)) { hits++; continue; }

                // ⚠⚠ THE CASE THIS TEST MISSED ON THE DAY IT WAS WRITTEN. A 3-byte character mangles to
                // `C3 [A0-AF]` + two `C2 xx` pairs — a box-drawing `─` (E2 94 80) becomes
                // `C3 A2 C2 94 C2 80`, whose lead is "â", NOT "Â"/"Ã". The narrow rule above walked
                // straight past it, and the author only noticed because the C1 detector fired.
                //
                // ⚠ Two `C2` pairs are required, not one: "é" followed by a non-breaking space is
                // `C3 A9 C2 A0` and is perfectly legitimate, so a single pair would cry wolf on real
                // accented text. Two in a row after a `C3 [A0-AF]` lead does not occur naturally.
                if (i + 4 < bytes.Length
                    && bytes[i] == 0xC3 && bytes[i + 1] >= 0xA0 && bytes[i + 1] <= 0xAF
                    && bytes[i + 2] == 0xC2 && bytes[i + 3] >= 0x80 && bytes[i + 3] <= 0xBF
                    && bytes[i + 4] == 0xC2)
                {
                    hits++;
                }
            }

            if (hits > 0) offenders.Add($"{Relative(file)} — {hits} occurrence(s)");
        }

        Assert.True(offenders.Count == 0,
            "Double-encoded UTF-8 with no C1 control to give it away — the shape a mangled BOM or an "
            + "accented character leaves:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// ⚠ The allow-list must stay honest. A file listed as "documents the corruption" that no longer
    /// exists would silently widen the exemption, and an entry with no reason is not a decision.
    /// </summary>
    [Fact]
    public void Every_allow_listed_file_exists_and_says_why()
    {
        foreach (var (relative, reason) in DocumentsTheCorruption)
        {
            Assert.True(File.Exists(Path.Combine(Repo.Root(), relative)),
                $"Allow-listed for encoding noise but not present: {relative}. Remove the entry.");
            Assert.False(string.IsNullOrWhiteSpace(reason), $"No reason given for {relative}.");
        }
    }

    /// <summary>
    /// ⚠⚠ THE DETECTORS MUST ACTUALLY DETECT. A scanner whose file enumeration silently matches nothing
    /// passes for ever and protects nothing — the exact failure mode that let the original corruption
    /// sit for eight days. This pins the sweep to real files and proves both patterns fire on the real
    /// corrupted bytes.
    /// </summary>
    [Fact]
    public void The_detectors_are_not_vacuous()
    {
        var scanned = TextFiles().ToList();
        Assert.True(scanned.Count > 500,
            $"Only {scanned.Count} files scanned — the enumeration is probably broken, which would make "
            + "the tests above pass without checking anything.");
        Assert.Contains(scanned, p => p.EndsWith("TillViewModel.cs", StringComparison.OrdinalIgnoreCase));

        // The real bytes from the 2026-08-11 accident: `⚠` corrupted three times.
        byte[] tripleWarning =
        {
            0xC3, 0x83, 0xC2, 0x83, 0xC3, 0x82, 0xC2, 0xA2,
            0xC3, 0x83, 0xC2, 0x82, 0xC3, 0x82, 0xC2, 0x9A,
            0xC3, 0x83, 0xC2, 0x82, 0xC3, 0x82, 0xC2, 0xA0,
        };
        var c1Hits = 0;
        for (var i = 0; i < tripleWarning.Length - 1; i++)
            if (tripleWarning[i] == 0xC2 && tripleWarning[i + 1] >= 0x80 && tripleWarning[i + 1] <= 0x9F)
                c1Hits++;
        Assert.True(c1Hits > 0, "The C1 detector does not fire on the bytes that caused this test to exist.");

        // A double-encoded BOM: no C1 control anywhere, so only the second detector can see it.
        byte[] mangledBom = { 0xC3, 0x83, 0xC2, 0xAF, 0xC3, 0x82, 0xC2, 0xBB, 0xC3, 0x82, 0xC2, 0xBF };
        var bomC1 = 0;
        for (var i = 0; i < mangledBom.Length - 1; i++)
            if (mangledBom[i] == 0xC2 && mangledBom[i + 1] >= 0x80 && mangledBom[i + 1] <= 0x9F) bomC1++;
        Assert.Equal(0, bomC1);   // ⚠ invisible to detector 1 — this is why detector 2 exists

        var bomHits = 0;
        for (var i = 0; i < mangledBom.Length - 2; i++)
            if (mangledBom[i] == 0xC3 && (mangledBom[i + 1] == 0x82 || mangledBom[i + 1] == 0x83)
                && (mangledBom[i + 2] == 0xC2 || mangledBom[i + 2] == 0xC3)) bomHits++;
        Assert.True(bomHits > 0, "The second detector does not fire on a double-encoded BOM.");

        // ⚠⚠ THE REGRESSION THIS PINS. A box-drawing `─` (E2 94 80) double-encoded is
        // `C3 A2 C2 94 C2 80` — lead byte "â", not "Â"/"Ã". The first version of the second detector
        // walked past it, and the author corrupted `Ask.tsx` with exactly these bytes an hour after
        // writing the test. Only the C1 detector caught it.
        byte[] mangledRule = { 0xC3, 0xA2, 0xC2, 0x94, 0xC2, 0x80 };
        var ruleHits = 0;
        for (var i = 0; i + 4 < mangledRule.Length; i++)
            if (mangledRule[i] == 0xC3 && mangledRule[i + 1] >= 0xA0 && mangledRule[i + 1] <= 0xAF
                && mangledRule[i + 2] == 0xC2 && mangledRule[i + 3] >= 0x80 && mangledRule[i + 3] <= 0xBF
                && mangledRule[i + 4] == 0xC2) ruleHits++;
        Assert.True(ruleHits > 0,
            "The widened detector does not fire on a double-encoded 3-byte character — the exact shape "
            + "that slipped past its first version.");

        // ⚠ And it must NOT fire on legitimate accented text: "é" + a non-breaking space is
        // `C3 A9 C2 A0`, which is one C2 pair, not two.
        byte[] eAcuteNbsp = { 0xC3, 0xA9, 0xC2, 0xA0, 0x41, 0x42 };
        var falseHits = 0;
        for (var i = 0; i + 4 < eAcuteNbsp.Length; i++)
            if (eAcuteNbsp[i] == 0xC3 && eAcuteNbsp[i + 1] >= 0xA0 && eAcuteNbsp[i + 1] <= 0xAF
                && eAcuteNbsp[i + 2] == 0xC2 && eAcuteNbsp[i + 3] >= 0x80 && eAcuteNbsp[i + 3] <= 0xBF
                && eAcuteNbsp[i + 4] == 0xC2) falseHits++;
        Assert.Equal(0, falseHits);

        // ⚠ And it must NOT fire on characters this repo uses legitimately.
        byte[] pound = { 0xC2, 0xA3 };
        byte[] times = { 0xC3, 0x97 };
        foreach (var good in new[] { pound, times })
        {
            for (var i = 0; i < good.Length - 1; i++)
                Assert.False(good[i] == 0xC2 && good[i + 1] >= 0x80 && good[i + 1] <= 0x9F,
                    "A legitimate character trips the C1 detector.");
        }
    }
}
