using System;
using System.Threading.Tasks;
using Plutus.Frontend.AppClient.Services.UIHandeling;

namespace Plutus.Frontend.AppClient.Tests;

/// <summary>
/// `Modal` serialises dialogs so WinUI never builds one while another is tearing down. It is one
/// semaphore, and on 2026-08-13 it stopped the till taking money.
///
/// ⚠⚠ THE DEFECT, because it is the reason these tests exist. `InputAlertHelper` gates every input
/// alert through `Modal` internally. The checkout's amount prompt was ALSO wrapped in `Modal` at the
/// call site — a redundant guard added a day after the internal one, in a different file — so the
/// flow waited on a semaphore it was already holding. The tender sheet closed, the amount box never
/// appeared, and because the deadlock sat inside the checkout's `try`, its
/// `finally { IsBusy = false; }` never ran: the scan box, which opens `if (IsBusy) return;`, then
/// silently stopped searching for the rest of the session. One redundant guard, three symptoms, no
/// exception and nothing in any log.
///
/// ⚠ These use plain delegates rather than MAUI dialogs on purpose — the defect is entirely about
/// SEQUENCING, and sequencing is testable without a UI host. That is also the lesson: `TenderLoop`
/// had 19 passing tests throughout, because the fault was never in the loop.
///
/// ⚠⚠ EVERY TEST HERE CARRIES A TIMEOUT, AND THAT IS NOT DECORATION. `Modal`'s gate is a private
/// STATIC semaphore, so a test that deadlocks while holding it strands every test that runs after
/// it. Proven by mutation on 2026-08-13: with the re-entrancy guard removed, this file did not fail —
/// it HUNG, and took the whole run with it for ten minutes. A regression that hangs CI is a
/// regression nobody diagnoses, so the bound is what turns it back into a red test with a sentence
/// attached. The in-test `Bounded` helper fires first and says what deadlocked; the `Timeout` is the
/// backstop for the poisoned-gate case, where the failing test is not the guilty one.
/// </summary>
public class ModalGateTests
{
    /// <summary>Fires before the xUnit timeout so the message names what hung.</summary>
    private const int BoundMs = 3_000;

    /// <summary>Backstop: only reached when the gate has already been stranded by another test.</summary>
    private const int TimeoutMs = 10_000;

    /// <summary>A dialog that takes 0ms and reports that it ran.</summary>
    private static Func<Task<int>> Returns(int value, Action onRun = null) => () =>
    {
        onRun?.Invoke();
        return Task.FromResult(value);
    };

    /// <summary>⚠ Every await in these tests is bounded. A deadlock must fail as a FAILURE, not as a
    /// test run that hangs until somebody kills it — an un-timed await would have hung CI instead of
    /// reporting this bug.</summary>
    private static async Task<T> Bounded<T>(Task<T> task, string what)
    {
        var done = await Task.WhenAny(task, Task.Delay(BoundMs));
        Assert.True(done == task, $"{what} did not complete within 3s — the gate deadlocked.");
        return await task;
    }

    [Fact(Timeout = TimeoutMs)]
    public async Task One_dialog_goes_through()
    {
        var ran = 0;
        var result = await Bounded(Modal.ShowAsync(Returns(7, () => ran++)), "a single dialog");

        Assert.Equal(7, result);
        Assert.Equal(1, ran);
    }

    /// <summary>
    /// ⚠⚠ THE BUG. A caller wraps a helper that already gates itself. Before the fix this waited for
    /// ever; the app never showed the dialog and never came back.
    /// </summary>
    [Fact(Timeout = TimeoutMs)]
    public async Task A_nested_call_passes_straight_through_instead_of_deadlocking()
    {
        var inner = 0;

        var result = await Bounded(
            Modal.ShowAsync(async () =>
            {
                // Exactly what `InputAlertHelper.ShowAsync` does inside a caller's wrap.
                return await Modal.ShowAsync(Returns(42, () => inner++));
            }),
            "a nested dialog");

        Assert.Equal(42, result);
        Assert.Equal(1, inner);
    }

    /// <summary>Three deep, because "one level" is not the rule — the rule is that the OUTERMOST call
    /// owns the gate.</summary>
    [Fact(Timeout = TimeoutMs)]
    public async Task Nesting_three_deep_still_completes()
    {
        var result = await Bounded(
            Modal.ShowAsync(async () =>
                await Modal.ShowAsync(async () =>
                    await Modal.ShowAsync(Returns(3)))),
            "a triple-nested dialog");

        Assert.Equal(3, result);
    }

    /// <summary>
    /// ⚠ The gate must still SERIALISE unrelated dialogs — that is the COMException guard, and a
    /// re-entrancy fix that let everything through would silently remove it.
    ///
    /// Two independent flows: the second must not start until the first has finished.
    /// </summary>
    [Fact(Timeout = TimeoutMs)]
    public async Task Two_separate_flows_do_not_overlap()
    {
        var firstStarted = new TaskCompletionSource<bool>();
        var releaseFirst = new TaskCompletionSource<bool>();
        var secondRan = false;

        var first = Modal.ShowAsync(async () =>
        {
            firstStarted.SetResult(true);
            await releaseFirst.Task;
            return 1;
        });

        await firstStarted.Task;

        var second = Modal.ShowAsync(Returns(2, () => secondRan = true));

        // The first is still open, so the second must be waiting at the gate.
        await Task.Delay(50);
        Assert.False(secondRan, "the second dialog ran while the first was still open");

        releaseFirst.SetResult(true);
        Assert.Equal(1, await Bounded(first, "the first dialog"));
        Assert.Equal(2, await Bounded(second, "the second dialog"));
        Assert.True(secondRan);
    }

    /// <summary>
    /// ⚠ FAIL OPEN, AND STAY USABLE. If a dialog throws, the gate must be released — otherwise one
    /// bad dialog kills every dialog in the app for the rest of the session, which is precisely how
    /// the deadlock presented.
    /// </summary>
    [Fact(Timeout = TimeoutMs)]
    public async Task A_throwing_dialog_releases_the_gate()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Modal.ShowAsync<int>(() => throw new InvalidOperationException("dialog blew up")));

        // The next one must work — this is the part that was dead.
        Assert.Equal(5, await Bounded(Modal.ShowAsync(Returns(5)), "the dialog after a failure"));
    }

    /// <summary>And a throw from INSIDE a nested call must not strand the outer flow's gate either.</summary>
    [Fact(Timeout = TimeoutMs)]
    public async Task A_throwing_nested_dialog_releases_the_gate()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Modal.ShowAsync<int>(async () =>
                await Modal.ShowAsync<int>(() => throw new InvalidOperationException("inner blew up"))));

        Assert.Equal(6, await Bounded(Modal.ShowAsync(Returns(6)), "the dialog after a nested failure"));
    }

    [Fact(Timeout = TimeoutMs)]
    public async Task A_null_dialog_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Modal.ShowAsync<int>(null));
    }

    /// <summary>
    /// ⚠⚠ CALLED FROM A POOL THREAD, which is how the till actually calls it and how it crashed.
    /// `Client.Core.TenderLoop` awaits its callbacks with `ConfigureAwait(false)` — right for a shared
    /// library — so every dialog after the first pass is raised off the UI thread. On 1.49.0 that
    /// constructed a WinUI `ContentDialog` on a pool thread, which threw a COMException that arrived
    /// via `Task.ThrowAsync` and killed the process: hand-test A4, overpay by card.
    ///
    /// ⚠ THIS TEST DOES NOT PROVE THE MARSHALLING — there is no dispatcher in the test host, so the
    /// delegate runs inline here by design. What it pins is that the OFF-THREAD PATH still completes
    /// and does not deadlock, which is the part that can regress silently. **That the dialog lands on
    /// the UI thread is USER-VERIFY (A4) and cannot be asserted without a UI host** — saying so is
    /// better than a test that pretends otherwise.
    /// </summary>
    [Fact(Timeout = TimeoutMs)]
    public async Task Called_from_a_pool_thread_it_still_completes()
    {
        var result = await Bounded(
            Task.Run(() => Modal.ShowAsync(Returns(9))),
            "a dialog raised from a pool thread");

        Assert.Equal(9, result);
    }

    /// <summary>And nested, from a pool thread — the exact shape of the refusal path.</summary>
    [Fact(Timeout = TimeoutMs)]
    public async Task Nested_from_a_pool_thread_still_completes()
    {
        var result = await Bounded(
            Task.Run(() => Modal.ShowAsync(async () => await Modal.ShowAsync(Returns(11)))),
            "a nested dialog raised from a pool thread");

        Assert.Equal(11, result);
    }
}
