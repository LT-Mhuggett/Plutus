using Xunit;

namespace Plutus.Tests.Architecture;

/// <summary>Locates the repo on disk so architecture tests can inspect source/csproj
/// files without referencing (and thus coupling to) any product project.</summary>
internal static class Repo
{
    public static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Plutus.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Could not locate repo root (Plutus.slnx) above the test bin dir.");
        return dir!.FullName;
    }

    /// <summary>All .cs files under a repo-relative directory, excluding bin/obj.</summary>
    public static IEnumerable<string> CsFiles(string relativeDir)
    {
        var root = Path.Combine(Root(), relativeDir);
        if (!Directory.Exists(root)) return Enumerable.Empty<string>();
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
    }
}
