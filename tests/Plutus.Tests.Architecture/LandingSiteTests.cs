using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Plutus.Tests.Architecture;

/// <summary>
/// **The landing site's hard constraints, pinned.**
///
/// ⚠⚠ THESE WERE "VERIFIED" BY SHELL COMMANDS RUN ONCE, WHICH IS NOT VERIFICATION — it is a snapshot.
/// The DoD lines they cover (no third-party scripts, no auth, no session storage) are the reasons
/// this app is allowed to be public at all, and every one of them is a single careless import away
/// from being false. A grep somebody ran on a Monday does not stop that; a test does.
///
/// ⚠ Source-scanned, matching the rest of this suite — no product references, and the Windows box
/// has no Node so the built bundle is not available here. The bundle checks stay in the deploy
/// procedure (`WP-landing.md` §6); these catch the mistake before it is ever built.
/// </summary>
public class LandingSiteTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, ".git")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }

    private static string AppRoot() =>
        Path.Combine(RepoRoot(), "Plutus", "Frontend", "Plutus.Frontend.Landing");

    private static string[] SourceFiles() =>
        Directory.Exists(Path.Combine(AppRoot(), "src"))
            ? Directory.EnumerateFiles(Path.Combine(AppRoot(), "src"), "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".ts", StringComparison.Ordinal) || f.EndsWith(".tsx", StringComparison.Ordinal))
                .ToArray()
            : Array.Empty<string>();

    /// <summary>⚠ The positive control. Every assertion below is vacuous if the scan reads nothing,
    /// and a test that passes because it found no files is worse than no test.</summary>
    [Fact]
    public void The_landing_app_is_where_this_test_expects_it()
    {
        Assert.True(Directory.Exists(AppRoot()),
            "The landing app is not where this test looks. Repoint it — do not delete the test.");
        Assert.True(SourceFiles().Length >= 4,
            $"Only {SourceFiles().Length} source files found; the scan is not reading what it thinks.");
    }

    /// <summary>
    /// ⚠⚠ NO THIRD-PARTY ORIGINS. WP-signup §3.2 refused a CAPTCHA because *"it puts a third-party
    /// script on the front door of a payments product"*, and that argument covers analytics tags,
    /// chat widgets and font CDNs equally. This is the only Plutus app a stranger loads, so it is the
    /// only one where a third-party request is a supply-chain problem rather than an internal one.
    /// </summary>
    [Fact]
    public void The_landing_site_talks_to_nobody_but_itself()
    {
        var offenders = SourceFiles()
            .Append(Path.Combine(AppRoot(), "index.html"))
            .Where(File.Exists)
            .SelectMany(f => File.ReadAllLines(f)
                .Select((text, n) => (file: Path.GetFileName(f), line: n + 1, text))
                .Where(x => Regex.IsMatch(x.text, @"https?://", RegexOptions.IgnoreCase))
                // ⚠ Allowed: the two configured app URLs (they are OUR hosts and come from env), and
                // any mention inside a comment. A URL in prose is not a request.
                .Where(x => !x.text.TrimStart().StartsWith("//", StringComparison.Ordinal)
                         && !x.text.TrimStart().StartsWith("*", StringComparison.Ordinal)
                         && !x.text.TrimStart().StartsWith("<!--", StringComparison.Ordinal)
                         && !x.text.Contains("w3.org", StringComparison.OrdinalIgnoreCase)
                         && !x.text.Contains("VITE_TILL_URL", StringComparison.Ordinal)
                         && !x.text.Contains("VITE_PORTAL_URL", StringComparison.Ordinal)))
            .Select(x => $"{x.file}:{x.line}  {x.text.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "The landing site references an external origin. It is the one Plutus app a stranger "
            + "loads, so a third-party request here is a supply-chain risk on the front door of a "
            + "payments product — the same reason WP-signup refused a CAPTCHA.\n\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// ⚠⚠ NO AUTH, NO SESSION, NO STORAGE. Architecture §12c: the login is a LINK to the two apps
    /// that already authenticate, because *"a third would be the C2 problem in a place where getting
    /// it wrong is a breach rather than an hour."*
    ///
    /// ⚠ The failure this catches is not somebody building a login screen on purpose. It is somebody
    /// adding "remember my email" to the signup form and reaching for localStorage.
    /// </summary>
    [Theory]
    [InlineData("localStorage")]
    [InlineData("sessionStorage")]
    [InlineData("document.cookie")]
    [InlineData("Authorization")]
    [InlineData("Bearer")]
    public void The_landing_site_holds_no_credential_of_any_kind(string forbidden)
    {
        var offenders = SourceFiles()
            .SelectMany(f => File.ReadAllLines(f)
                .Select((text, n) => (file: Path.GetFileName(f), line: n + 1, text))
                .Where(x => x.text.Contains(forbidden, StringComparison.Ordinal))
                .Where(x => !x.text.TrimStart().StartsWith("//", StringComparison.Ordinal)
                         && !x.text.TrimStart().StartsWith("*", StringComparison.Ordinal)))
            .Select(x => $"{x.file}:{x.line}  {x.text.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0,
            $"The landing site uses `{forbidden}`. It has no session and must not acquire one — its "
            + "sign-in is a LINK to the till and the portal (architecture §12c). If something here "
            + "needs to remember a value, ask whether it belongs on this app at all.\n\n"
            + string.Join("\n", offenders));
    }

    /// <summary>⚠ And no password input, which is the same rule with a form attached.</summary>
    [Fact]
    public void The_landing_site_has_no_password_field()
    {
        var offenders = SourceFiles()
            .SelectMany(f => File.ReadAllLines(f)
                .Select((text, n) => (file: Path.GetFileName(f), line: n + 1, text))
                .Where(x => Regex.IsMatch(x.text, "type\\s*=\\s*[\"']password[\"']")))
            .Select(x => $"{x.file}:{x.line}  {x.text.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "A password input appeared on the landing site. Its sign-in is a link to the two apps "
            + "that already authenticate.\n\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// ⚠⚠ FOUR ENDPOINTS AND NO MORE. WP-landing §0: this is a UI over the surface WP-SIGNUP already
    /// built and tested — *"if it starts growing endpoints, something has gone wrong."* A fifth call
    /// means the landing site has started being a feature, which is the moment to stop and ask why.
    /// </summary>
    [Fact]
    public void The_landing_site_calls_only_the_four_signup_endpoints()
    {
        var api = Path.Combine(AppRoot(), "src", "api.ts");
        Assert.True(File.Exists(api), "src/api.ts is missing — the scan cannot check the surface.");

        var paths = Regex.Matches(File.ReadAllText(api), @"[""`](/api/[^""`?]+)")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        var expected = new[]
        {
            "/api/v1/signup",
            "/api/v1/signup/dpa",
            "/api/v1/signup/dpa/accept",
            "/api/v1/signup/verify",
        };

        Assert.True(expected.SequenceEqual(paths),
            "The landing site's endpoint surface changed. It is meant to be a UI over the four "
            + "endpoints WP-SIGNUP already built and tested; a fifth means it has started being a "
            + "feature.\n\nexpected: " + string.Join(", ", expected)
            + "\nfound:    " + string.Join(", ", paths));
    }

    /// <summary>
    /// ⚠⚠ `noindex` STAYS UNTIL THE VHOST EXISTS. This is the ONE Plutus app meant to be indexed
    /// eventually — the till and portal are noindex forever because they are useless without a
    /// credential. ⚠ A site that is reachable and indexed before anybody decided to launch is hard
    /// to un-launch: removing the tag is part of the same change that adds the Caddy vhost, and this
    /// test is what makes that a deliberate edit rather than an oversight either way.
    /// </summary>
    [Fact]
    public void The_landing_site_is_noindex_while_it_is_unlisted()
    {
        var html = File.ReadAllText(Path.Combine(AppRoot(), "index.html"));
        Assert.Contains("noindex", html, StringComparison.OrdinalIgnoreCase);
    }
}
