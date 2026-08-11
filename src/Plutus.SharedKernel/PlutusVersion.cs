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

    /// <summary>
    /// Is <paramref name="running"/> an OLDER release than <paramref name="expected"/>?
    ///
    /// ⚠⚠ NEVER COMPARE THESE AS STRINGS. `"1.10.0" &lt; "1.9.0"` is TRUE to a string comparer,
    /// because it stops at the second character — so a till on 1.10.0 would be told it is behind
    /// 1.9.0, for ever, and the one release where it matters (the tenth of a series) is the one it
    /// gets wrong. This project has already reached 1.46.0 on the MAUI till, so that is not a
    /// theoretical range.
    ///
    /// ⚠ SEGMENT BY SEGMENT, NUMERICALLY, and a missing segment is a ZERO — "1.4" and "1.4.0" are
    /// the same release, and one of them must not be reported as behind the other.
    ///
    /// ⚠ AN UNPARSEABLE VERSION IS NEVER "BEHIND". `0.0.0` is what a build outside the release
    /// process reports (see <see cref="Of"/>), and until 2026-08-11 it was also what every
    /// Mac-built web bundle reported because the version file could not be found. Nagging a
    /// developer's local build to upgrade — or worse, gating it — would make the tool unusable
    /// exactly where it is being worked on. Say NO unless the comparison is meaningful.
    /// </summary>
    /// <returns>True only when both parse and <paramref name="running"/> is genuinely lower.</returns>
    public static bool IsOlderThan(string? running, string? expected)
    {
        // ⚠ `0.0.0` PARSES, so it has to be refused by name rather than by the parser. It is the
        // sentinel for "this build did not come through the release process" (see `Of`), and until
        // 2026-08-11 it was ALSO what every Mac-built web bundle reported, because the version file
        // could not be found. Treating it as an ancient release would have told a whole estate of
        // correctly-updated tills to upgrade — and would nag every developer's local build for ever.
        // ⚠ It is still WORTH NOTICING; the portal's Locations & Tills column is where, which is
        // exactly how the mislabelled bundles were found. It is just not an UPGRADE prompt.
        if (running?.Trim() == "0.0.0") return false;

        if (!TryParse(running, out var a) || !TryParse(expected, out var b)) return false;

        for (var i = 0; i < 3; i++)
        {
            if (a[i] < b[i]) return true;
            if (a[i] > b[i]) return false;
        }
        return false;   // equal is not behind
    }

    /// <summary>MAJOR.FEATURE.FIX into three numbers. Shorter is padded with zeros; anything else
    /// — empty, "dev", "0.0.0-rc1" — fails, and <see cref="IsOlderThan"/> treats that as "cannot
    /// say" rather than guessing.</summary>
    private static bool TryParse(string? value, out int[] parts)
    {
        parts = new[] { 0, 0, 0 };
        if (string.IsNullOrWhiteSpace(value)) return false;

        var split = value.Trim().Split('.');
        if (split.Length is 0 or > 3) return false;

        for (var i = 0; i < split.Length; i++)
        {
            if (!int.TryParse(split[i], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var n))
                return false;
            parts[i] = n;
        }
        return true;
    }
}
