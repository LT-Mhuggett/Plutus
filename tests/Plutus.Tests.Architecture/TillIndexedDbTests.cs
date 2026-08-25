using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Plutus.Tests.Architecture;

/// <summary>
/// **The web till's IndexedDB has exactly one owner.**
///
/// ⚠⚠ WHY THIS TEST EXISTS. `cashOutbox.ts` opened the shared `plutus-till` database itself, with a
/// hardcoded `indexedDB.open("plutus-till", 3)` and **no `onupgradeneeded` handler** — written that
/// way because `offline.ts`'s opener happened to be private, and the comment above it said so as if
/// that settled the matter. When `DB_VERSION` went to 4 for multi-barcode on 2026-08-20, IndexedDB
/// began refusing that open outright:
///
///   VersionError: The requested version (3) is less than the existing version (4)
///
/// ⚠ The store it could no longer reach is the OFFLINE CASH QUEUE — floats, paid in/out, Z. It was
/// dead for five days and nothing failed loudly; Matt found it in a browser console on 2026-08-25.
///
/// ⚠ A version number and its upgrade handler have to travel together, so a second opener is always
/// a bug. This is a source scan rather than a unit test because the fault is *structural* — the code
/// that would catch it at runtime is the code that was broken.
/// </summary>
public class TillIndexedDbTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, ".git")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }

    private static string TillSrc() =>
        Path.Combine(RepoRoot(), "Plutus", "Frontend", "Plutus.Frontend.WebApp", "src");

    /// <summary>⚠ Code only — a mention inside a comment is how the fault is DOCUMENTED, and must
    /// not fail the test that documents it.</summary>
    private static (string File, int Line)[] RealOpens()
    {
        var hits = new System.Collections.Generic.List<(string, int)>();
        foreach (var f in Directory.EnumerateFiles(TillSrc(), "*.ts", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(TillSrc(), "*.tsx", SearchOption.AllDirectories)))
        {
            var lines = File.ReadAllLines(f);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("*") || trimmed.StartsWith("/*")) continue;
                if (Regex.IsMatch(line, @"indexedDB\s*\.\s*open\s*\(")) hits.Add((f, i + 1));
            }
        }
        return hits.ToArray();
    }

    [Fact]
    public void The_till_source_is_where_this_test_expects_it()
    {
        Assert.True(Directory.Exists(TillSrc()), $"web till src not found at {TillSrc()} — the scan proves nothing.");
    }

    [Fact]
    public void Exactly_one_place_opens_the_till_database()
    {
        var opens = RealOpens();
        Assert.True(
            opens.Length == 1,
            "The till's IndexedDB must have exactly ONE opener, so the version and its upgrade handler "
            + "cannot drift apart. Found " + opens.Length + ": "
            + string.Join(", ", opens.Select(o => Path.GetFileName(o.File) + ":" + o.Line))
            + ". If you need the database elsewhere, import `openDb` from offline.ts.");
    }

    [Fact]
    public void The_one_opener_is_offline_ts()
    {
        var opens = RealOpens();
        Assert.Single(opens);
        Assert.Equal("offline.ts", Path.GetFileName(opens[0].File));
    }

    /// <summary>
    /// ⚠ The opener must declare its version from the single constant, never inline — an inline
    /// literal is what the duplicate had, and it is what went stale.
    /// </summary>
    [Fact]
    public void The_opener_uses_the_shared_version_constant_not_a_literal()
    {
        var offline = File.ReadAllText(Path.Combine(TillSrc(), "offline.ts"));
        Assert.Contains("indexedDB.open(DB_NAME, DB_VERSION)", offline);
        Assert.Matches(@"const\s+DB_VERSION\s*=\s*\d+", offline);
    }
}
