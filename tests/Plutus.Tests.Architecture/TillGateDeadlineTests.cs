using System.Text.RegularExpressions;
using Xunit;

namespace Plutus.Tests.Architecture;

/// <summary>
/// **Every semaphore in the till waits with a deadline.**
///
/// ⚠⚠ WITHOUT ONE, A SINGLE HANG BRICKS A WHOLE SUBSYSTEM IN SILENCE. `SemaphoreSlim.Release()` sits
/// in a `finally`, so a call that finishes or throws always frees the gate — but a call that never
/// returns holds it for ever, and every later caller simply waits. No exception, no log, no error on
/// screen. The scan box accepts text and nothing happens; the tab never redraws. There is no way to
/// tell that from "the feature is broken", which is what makes it worth a rule.
///
/// ⚠ `TillStoreAccess.UseAsync` established the convention on 2026-08-11 with a 30-second wait, and
/// `MAUI-retrofit.md` §0.3 has carried *"the store-gate deadline convention is unpinned — the next
/// caller written without a deadline restores the fault in full, held by convention and a code
/// comment"* ever since. ⚠⚠ **BY 2026-08-22 FOUR OF THE SEVEN GATES DID NOT FOLLOW IT** — the API
/// client, both cadence caches (which hold their gate across a NETWORK call) and the modal gate,
/// whose failure means the till can never show a dialog again. A convention four sites out of seven
/// ignore is not a convention; this is the pin that makes it one.
///
/// ⚠ Source text, not reflection — this suite references no product projects, and the MAUI app does
/// not build on a test host anyway. It is a crude check and a deliberate one: it cannot prove a
/// timeout is the RIGHT length, only that somebody chose one.
///
/// ⚠ `WaitAsync(0, ...)` counts. A zero timeout is a deliberate "skip this tick if busy" — the
/// cadence services use it — and it cannot hang, which is the whole point.
/// </summary>
public class TillGateDeadlineTests
{
    private const string TillRoot = "Plutus/Frontend/Plutus.Frontend.AppClient";

    /// <summary>
    /// `Gate.WaitAsync(ct)` and `Gate.WaitAsync()` — a wait whose only argument is a cancellation
    /// token, or nothing at all. Both block for ever if the holder never releases.
    ///
    /// ⚠ A `CancellationToken` IS NOT A DEADLINE. Every one of the four sites fixed on 2026-08-22
    /// passed `ct`, and not one of those tokens was ever cancelled on the hang path — they carry the
    /// caller's cancellation, not a timeout, and reading one as "this is bounded" is precisely the
    /// mistake being pinned against.
    /// </summary>
    private static readonly Regex Undeadlined = new(
        @"\.WaitAsync\(\s*(\)|(ct|token|cancellationToken)\s*\))",
        RegexOptions.Compiled);

    [Fact]
    public void No_semaphore_in_the_till_waits_without_a_timeout()
    {
        var root = Path.Combine(Repo.Root(), TillRoot);
        Assert.True(Directory.Exists(root), $"{TillRoot} not found — has the till moved?");

        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (rel.StartsWith("obj/") || rel.StartsWith("bin/")) continue;

            var lines = File.ReadAllLines(file);
            for (var n = 0; n < lines.Length; n++)
            {
                var line = lines[n];
                if (line.TrimStart().StartsWith("//")) continue;      // a comment describing the rule
                if (!Undeadlined.IsMatch(line)) continue;

                offenders.Add($"{rel}:{n + 1}  {line.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "A semaphore in the till waits with no timeout. If the holder never releases, every later "
            + "caller waits for ever with nothing logged — the fault `TillStoreAccess.UseAsync` was "
            + "given a 30-second deadline to close.\n\n"
            + "Pass a timeout: `WaitAsync(TimeSpan.FromSeconds(30), ct)` — or `WaitAsync(0, ct)` if the "
            + "right answer is to skip this pass when busy. Then decide what a timeout MEANS here: "
            + "throw (the store gate), return the 'not available' answer the caller already handles "
            + "(the API client and the caches), or go ahead anyway (the modal gate — a till that "
            + "cannot ask a question cannot take a payment).\n\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// ⚠ The rule is only worth having if the checker can actually see a breach — so this asserts the
    /// pattern matches the exact shapes that were live in the till on 2026-08-22, before the fix.
    /// A regex that quietly stopped matching would leave the suite green and the rule gone.
    /// </summary>
    [Theory]
    [InlineData("            await Gate.WaitAsync(ct).ConfigureAwait(false);")]          // PlutusApi, and both caches
    [InlineData("            await Gate.WaitAsync().ConfigureAwait(true);")]             // Modal
    [InlineData("        await _lock.WaitAsync(cancellationToken);")]
    public void The_checker_recognises_an_undeadlined_wait(string line) =>
        Assert.Matches(Undeadlined, line);

    /// <summary>⚠ And does NOT fire on the forms that are bounded — or the rule becomes noise that
    /// somebody deletes.</summary>
    [Theory]
    [InlineData("            if (!await Gate.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false))")]
    [InlineData("            if (!await Gate.WaitAsync(0, ct).ConfigureAwait(false))")]
    [InlineData("            await Gate.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(true);")]
    public void The_checker_leaves_a_bounded_wait_alone(string line) =>
        Assert.DoesNotMatch(Undeadlined, line);
}
