using System;
using System.IO;
using System.Text.RegularExpressions;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// One version number, on every surface.
///
/// ⚠ WHY THIS IS A TEST AND NOT A CONVENTION. A version is only worth having if two people looking
/// at two different machines can say the same thing about them. The moment the web till, the MAUI
/// till and the backend can each report a *different* number, the version is worse than the build
/// timestamp it replaced — it looks authoritative and isn't.
///
/// So this pins the whole chain end to end: <c>till-version.txt</c> → <c>Directory.Build.props</c>
/// → <c>InformationalVersion</c> → <see cref="TillVersion.Current"/> at runtime, and separately
/// that <c>vite.config.ts</c> reads the same file rather than growing a version of its own.
/// </summary>
public class TillVersionTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Plutus.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Could not locate repo root (Plutus.slnx) above the test bin dir.");
        return dir!.FullName;
    }

    [Fact]
    public void The_runtime_version_is_the_one_in_till_version_txt()
    {
        // Catches the build silently not picking the file up — which would leave every surface
        // reporting a stale number with total confidence.
        var path = Path.Combine(RepoRoot(), "till-version.txt");
        Assert.True(File.Exists(path), "till-version.txt is missing from the repo root.");

        var onDisk = File.ReadAllText(path).Trim();
        Assert.False(string.IsNullOrWhiteSpace(onDisk), "till-version.txt is empty.");
        Assert.Equal(onDisk, TillVersion.Current);
    }

    [Fact]
    public void The_version_is_not_the_did_not_come_from_a_release_fallback()
    {
        // 0.0.0 is the deliberate "this build did not come from the release process" value. Seeing
        // it in a test run means Directory.Build.props stopped being applied.
        Assert.NotEqual("0.0.0", TillVersion.Current);
    }

    [Fact]
    public void The_dotnet_build_reads_the_shared_file()
    {
        var props = File.ReadAllText(Path.Combine(RepoRoot(), "Directory.Build.props"));
        Assert.Contains("till-version.txt", props);
        Assert.Contains("InformationalVersion", props);
    }

    [Fact]
    public void The_web_till_reads_the_SAME_file_rather_than_its_own_version()
    {
        // ⚠ The drift this stops is the expensive one: a TypeScript surface with a hardcoded
        // version string that nobody remembers to bump. It would keep reporting today's number
        // forever while the .NET side moved on, and every bug report against it would name the
        // wrong build. Checked here because the Architecture suite deliberately holds no product
        // references, and this assertion needs TillVersion.
        var vite = Path.Combine(RepoRoot(), "Plutus", "Frontend", "Plutus.Frontend.WebApp", "vite.config.ts");
        Assert.True(File.Exists(vite), $"Expected the web till's vite config at {vite}");

        var text = File.ReadAllText(vite);
        Assert.Contains("till-version.txt", text);
        Assert.Contains("__TILL_VERSION__", text);

        // A literal version in the config would defeat the whole arrangement.
        Assert.DoesNotMatch(new Regex(@"__TILL_VERSION__\s*:\s*JSON\.stringify\(\s*[""']\d"), text);
    }

    [Fact]
    public void The_version_looks_like_something_a_person_can_read_out_loud()
    {
        // Free-form on purpose (a suffix like "1.2.0-rc1" is fine), but it must not be a timestamp —
        // replacing an ISO timestamp with another ISO timestamp is the one outcome to rule out.
        Assert.Matches(new Regex(@"^\d+\.\d+(\.\d+)?([-.\w]*)?$"), TillVersion.Current);
    }
}
