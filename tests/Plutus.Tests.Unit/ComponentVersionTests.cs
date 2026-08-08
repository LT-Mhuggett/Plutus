using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// One version per deployable component, and no component allowed to invent its own.
///
/// ⚠ WHY PER COMPONENT AND NOT ONE NUMBER. Matt, 2026-08-08: *"each till needs a specific version
/// as they will end up diverging when you have specific Windows or Linux challenges."* A shared
/// number would force the web till to claim a release containing only a MAUI printer fix, and would
/// make "it's broken on 1.4.2" unanswerable because the reporter and the fixer would mean different
/// artefacts. These tests hold the arrangement together: the files exist, they parse as X.Y.Z, each
/// project points at its own, and nobody has hardcoded a literal that will rot.
/// </summary>
public class ComponentVersionTests
{
    /// <summary>Every component that deploys on its own schedule, and the project that owns it.</summary>
    public static readonly (string File, string Project)[] Components =
    {
        ("backend.txt", "Plutus/Endpoints/Plutus.DBService/Plutus.DBService.csproj"),
        ("till-maui.txt", "Plutus/Frontend/Plutus.Frontend.AppClient/Plutus.Frontend.AppClient.csproj"),
        ("agent.txt", "tools/Plutus.TillAgent/Plutus.TillAgent.csproj"),
    };

    /// <summary>The TypeScript surfaces, which read their file in vite.config.ts instead.</summary>
    public static readonly (string File, string Config)[] ViteComponents =
    {
        ("till-web.txt", "Plutus/Frontend/Plutus.Frontend.WebApp/vite.config.ts"),
        ("portal.txt", "Plutus/Frontend/Plutus.Frontend.Portal/vite.config.ts"),
    };

    public static TheoryData<string> AllVersionFiles()
    {
        var data = new TheoryData<string>();
        foreach (var f in Components.Select(c => c.File)
                     .Concat(ViteComponents.Select(v => v.File))
                     .Append("platform.txt"))
            data.Add(f);
        return data;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Plutus.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Could not locate repo root (Plutus.slnx) above the test bin dir.");
        return dir!.FullName;
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relative)).Trim();

    [Theory]
    [MemberData(nameof(AllVersionFiles))]
    public void Every_component_has_a_version_file_and_it_is_X_Y_Z(string file)
    {
        var path = Path.Combine(RepoRoot(), "versions", file);
        Assert.True(File.Exists(path), $"versions/{file} is missing — every deployable component needs one.");

        var version = File.ReadAllText(path).Trim();

        // MAJOR.FEATURE.FIX, per Matt's instruction. A suffix like "-rc1" is allowed; a date is not,
        // because replacing a build timestamp with another timestamp was the thing being fixed.
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+(-[\w.]+)?$"), version);
    }

    [Theory]
    [MemberData(nameof(AllVersionFiles))]
    public void No_version_file_is_the_did_not_come_from_a_release_fallback(string file)
    {
        Assert.NotEqual("0.0.0", Read(Path.Combine("versions", file)));
    }

    [Fact]
    public void Every_dotnet_component_points_at_its_OWN_version_file()
    {
        // ⚠ The failure this prevents is silent: a project with no <PlutusVersionFile> falls back to
        // versions/platform.txt and reports the shared-library number as if it were its own release.
        foreach (var (file, project) in Components)
        {
            var csproj = Read(project);
            Assert.True(csproj.Contains($"versions/{file}"),
                $"{project} does not set <PlutusVersionFile>versions/{file}</PlutusVersionFile>, " +
                "so it will silently report the platform version as its own.");
        }
    }

    [Fact]
    public void Every_typescript_component_reads_its_OWN_version_file()
    {
        foreach (var (file, config) in ViteComponents)
        {
            var text = Read(config);
            Assert.True(text.Contains($"versions/{file}"), $"{config} does not read versions/{file}.");
            Assert.Contains("__APP_VERSION__", text);

            // A literal in the config would defeat the whole arrangement — it would keep reporting
            // today's number forever while everything else moved on.
            Assert.DoesNotMatch(new Regex(@"__APP_VERSION__\s*:\s*JSON\.stringify\(\s*[""']\d"), text);
        }
    }

    [Fact]
    public void Components_are_allowed_to_disagree_and_the_wiring_does_not_force_them_together()
    {
        // The positive statement of the whole design: set two components to different numbers and
        // nothing should object. Guards against someone "simplifying" this back to one shared file.
        var distinctFiles = Components.Select(c => c.File)
            .Concat(ViteComponents.Select(v => v.File))
            .Distinct()
            .Count();

        Assert.Equal(Components.Length + ViteComponents.Length, distinctFiles);
    }

    [Fact]
    public void The_shared_libraries_report_the_platform_version()
    {
        // SharedKernel ships INSIDE the others and has no release of its own, so it takes
        // platform.txt — the version of the wire contract and the rules.
        Assert.Equal(Read(Path.Combine("versions", "platform.txt")), PlutusVersion.Platform);
    }

    [Fact]
    public void The_build_wiring_lives_in_targets_not_props()
    {
        // ⚠ Load-bearing and easy to "tidy" wrongly. Directory.Build.props is imported BEFORE the
        // project body, so a <PlutusVersionFile> set in a .csproj would not be visible and every
        // component would silently take the default. Only .targets is imported late enough.
        var root = RepoRoot();
        Assert.True(File.Exists(Path.Combine(root, "Directory.Build.targets")),
            "Directory.Build.targets is missing — per-project version selection cannot work without it.");
        Assert.False(File.Exists(Path.Combine(root, "Directory.Build.props")),
            "Version wiring must NOT move to Directory.Build.props: it is imported before the project " +
            "body, so per-project <PlutusVersionFile> would be ignored and every component would " +
            "report the platform version.");

        var targets = Read("Directory.Build.targets");
        Assert.Contains("PlutusVersionFile", targets);
        Assert.Contains("InformationalVersion", targets);
    }
}
