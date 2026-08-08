using System;
using System.Reflection;

namespace Plutus.SharedKernel;

/// <summary>
/// The version of the component you are running in — <c>MAJOR.FEATURE.FIX</c>.
///
/// ⚠ EVERY DEPLOYABLE HAS ITS OWN NUMBER, and that is the point. The backend, the portal, the web
/// till, the MAUI till and the hardware agent ship on separate schedules and will diverge — a
/// Windows-only printer fix belongs to the MAUI till and to nothing else. A single platform-wide
/// version would force the web till to claim a release it had no changes in, and would leave "it's
/// broken on 1.4.2" unanswerable, because the reporter and the fixer would mean different things.
///
/// The numbers live in <c>versions/*.txt</c> at the repo root, one file per component;
/// <c>Directory.Build.targets</c> turns the project's chosen file into
/// <see cref="AssemblyInformationalVersionAttribute"/>, which is what this reads back.
/// See <c>versions/README.md</c> for which digit to bump.
/// </summary>
public static class PlutusVersion
{
    /// <summary>
    /// The running application's version — the ENTRY assembly, so the backend host reports the
    /// backend's number and the MAUI head reports that till's, rather than both reporting the
    /// shared library they happen to call through.
    ///
    /// ⚠ Use <see cref="Of"/> instead wherever the answer matters and the entry assembly is not
    /// obviously the component: under a test runner the entry assembly is the runner.
    /// </summary>
    public static string Current { get; } = Of(Assembly.GetEntryAssembly());

    /// <summary>The version a specific assembly was built from. <c>"0.0.0"</c> when it carries no
    /// informational version — a build that did not come through the release process.</summary>
    public static string Of(Assembly? assembly)
    {
        var informational = assembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational)) return "0.0.0";

        // The SDK appends "+<source-revision-id>" when the build knows its commit. Useful in a log,
        // not in a number an operator is expected to read down a phone.
        var plus = informational.IndexOf('+');
        return (plus >= 0 ? informational[..plus] : informational).Trim();
    }

    /// <summary>The version of the shared libraries (<c>versions/platform.txt</c>) — the wire
    /// contract and the rules, which ship *inside* every component rather than on their own.
    /// Worth reporting next to a component's own version when diagnosing a contract mismatch.</summary>
    public static string Platform { get; } = Of(typeof(PlutusVersion).Assembly);
}
