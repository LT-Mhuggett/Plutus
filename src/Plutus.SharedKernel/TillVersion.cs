using System;
using System.Reflection;

namespace Plutus.SharedKernel;

/// <summary>
/// The build number every till, the backend and the portal quote.
///
/// ⚠ ONE NUMBER, SET IN ONE PLACE — <c>till-version.txt</c> at the repo root, read by
/// <c>Directory.Build.props</c> for .NET and by <c>vite.config.ts</c> for the TypeScript surfaces.
/// The whole value of a version is that two people looking at two machines can say the same thing;
/// a per-surface version is just a timestamp with extra steps.
///
/// It replaces nothing — the build timestamp stays useful for "is this actually the artefact I just
/// deployed" — but the VERSION is what goes in a bug report, and what someone can read down a phone.
/// </summary>
public static class TillVersion
{
    /// <summary>The version this assembly was built from, e.g. <c>"1.1.0"</c>. Falls back to
    /// <c>"0.0.0"</c> when the informational version is absent (a project built outside the repo,
    /// or before <c>Directory.Build.props</c> existed) — deliberately an obviously-wrong number
    /// rather than a blank, because blank reads as "the label is broken" and 0.0.0 reads as
    /// "this build did not come from the release process".</summary>
    public static string Current { get; } = Read();

    private static string Read()
    {
        var informational = typeof(TillVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational)) return "0.0.0";

        // The SDK appends "+<source-revision-id>" when the build knows its commit. Useful, but not
        // in a version an operator is expected to read out loud.
        var plus = informational.IndexOf('+');
        return (plus >= 0 ? informational[..plus] : informational).Trim();
    }
}
