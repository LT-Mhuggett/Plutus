using Xunit;

namespace Plutus.Tests.Architecture;

/// <summary>
/// The portal and the web till are separate npm apps that cannot share a package, so a handful of
/// files exist TWICE and are meant to be byte-for-byte identical. Until now the only thing asking for
/// that was a comment in each file — <c>"keep the two files byte-for-byte identical"</c> — and a
/// comment has never stopped anybody.
///
/// ⚠⚠ IT HAD ALREADY FAILED. On 2026-08-20 <c>Ask.tsx</c> had drifted: the till's imported
/// <c>DialogX</c> and rendered a close ✕, the portal's did not — so EVERY confirm / choose / prompt
/// dialog in the portal was missing the ✕ that <c>till-design.md</c> D4 makes mandatory, and had been
/// since D4 was written. Nothing flagged it, because nothing was looking. That is the whole argument
/// for this file.
///
/// ⚠ WHY IT LIVES IN .NET rather than in vitest. The web till has no <c>@types/node</c>, so a test
/// there cannot read a sibling project off disk without a new dependency, and Vite's <c>fs.allow</c>
/// root would fight a <c>?raw</c> import of a file outside the project. The architecture suite already
/// scans source text on disk and knows where the repo root is, so this is the cheap place to do it.
///
/// ⚠ Twins are compared with LINE ENDINGS NORMALISED but nothing else. A CRLF/LF difference is a git
/// checkout artefact rather than drift; anything else — a stray space, a reordered import, a comment
/// edited on one side — is exactly what we want to fail on.
/// </summary>
public class FrontendTwinTests
{
    private static readonly string WebApp = Path.Combine("Plutus", "Frontend", "Plutus.Frontend.WebApp", "src");
    private static readonly string Portal = Path.Combine("Plutus", "Frontend", "Plutus.Frontend.Portal", "src");

    /// <summary>
    /// The twinned files: the path under the web till's <c>src</c>, and the path under the portal's.
    /// They differ because the till keeps some of these in a <c>till/</c> subfolder.
    ///
    /// ⚠ ADD A ROW WHENEVER YOU COPY A FILE BETWEEN THE TWO APPS. A copy with no row here is a copy
    /// with nothing holding it together, which is the state every one of these was in until now.
    /// </summary>
    public static TheoryData<string, string> Twins() => new()
    {
        // The standard data table — Part `table-standard.md` depends on both surfaces agreeing.
        { Path.Combine(WebApp, "DataTable.tsx"), Path.Combine(Portal, "DataTable.tsx") },

        // The in-app confirm/choose/prompt host. ⚠ THE ONE THAT HAD DRIFTED — see the class remarks.
        { Path.Combine(WebApp, "Ask.tsx"), Path.Combine(Portal, "Ask.tsx") },

        // The close ✕ itself (till-design D4). The portal had NO copy at all until 2026-08-20.
        { Path.Combine(WebApp, "DialogX.tsx"), Path.Combine(Portal, "DialogX.tsx") },

        // ⚠⚠ THE UTC RULE (2026-08-21). Matt: *"Why are the sales a correct time on the portal
        // and an hour earlier on the webtill?"* — because each app had its own idea of how to read a
        // timestamp, and one of them was `new Date(bare)`. The rule is now one file, twinned, and its
        // .NET third is `Plutus.SharedKernel.ApiTime` (`till-design.md` C2).
        { Path.Combine(WebApp, "apiTime.ts"), Path.Combine(Portal, "apiTime.ts") },

        // ⚠ ITS TESTS ARE TWINNED TOO. A twinned rule with the tests on one side only is a rule the
        // other side can break silently — and the portal has no vitest, so its copy is carried here
        // for identity rather than executed. The web till runs them.
        { Path.Combine(WebApp, "apiTime.test.ts"), Path.Combine(Portal, "apiTime.test.ts") },

        // Code 39 rendering for membership cards and receipts.
        { Path.Combine(WebApp, "till", "Barcode39.tsx"), Path.Combine(Portal, "Barcode39.tsx") },

        // The live barcode check (multi-barcode, 2026-08-20). ⚠ A RULE, not just a component: it
        // decides what an operator is told about a barcode they are typing, on both surfaces.
        { Path.Combine(WebApp, "barcodeProblem.ts"), Path.Combine(Portal, "barcodeProblem.ts") },
    };

    [Theory]
    [MemberData(nameof(Twins))]
    public void Twinned_frontend_files_are_identical(string tillRelative, string portalRelative)
    {
        var root = Repo.Root();
        var tillPath = Path.Combine(root, tillRelative);
        var portalPath = Path.Combine(root, portalRelative);

        // ⚠ A MISSING TWIN IS A FAILURE, not a skip. "The portal never had a copy" is precisely the
        // fault found in DialogX.tsx, and a test that shrugs at it would have found nothing.
        Assert.True(File.Exists(tillPath), $"Twin missing from the web till: {tillRelative}");
        Assert.True(File.Exists(portalPath), $"Twin missing from the portal: {portalRelative}");

        var till = Normalise(File.ReadAllText(tillPath));
        var portal = Normalise(File.ReadAllText(portalPath));

        Assert.True(till == portal,
            $"""
            {Path.GetFileName(tillRelative)} has DRIFTED between the two frontends.

              web till: {tillRelative}
              portal:   {portalRelative}

            These files are meant to be byte-for-byte identical (they cannot share an npm package).
            Copy the intended version over the other and re-run — do NOT "fix" this by deleting the row
            in FrontendTwinTests.Twins(), because a drifted twin is how the portal lost its dialog ✕.
            First difference at character {FirstDifference(till, portal)}.
            """);
    }

    private static string Normalise(string s) => s.Replace("\r\n", "\n");

    /// <summary>Character offset of the first difference — enough to find it without a diff tool.</summary>
    private static int FirstDifference(string a, string b)
    {
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++)
            if (a[i] != b[i]) return i;
        return n;
    }
}
