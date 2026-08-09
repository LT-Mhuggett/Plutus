using System;
using System.Threading.Tasks;
using Plutus.Frontend.AppClient.Services.Loading;

namespace Plutus.Frontend.AppClient.Tests;

/// <summary>
/// The loading overlay is a modal page pushed over the whole app. If it goes up and never comes
/// down, the app is unusable and nothing is logged — it does not crash, it just stops responding,
/// on every screen, until the process is killed. That happened, and was reported as two unrelated
/// screens "hanging".
///
/// These tests pin the sequencing rule that prevents it. They use fake show/hide delegates rather
/// than MAUI navigation on purpose: the defect is entirely about ORDER, and order is testable.
/// </summary>
public class OverlayGateTests
{
    /// <summary>
    /// A recording pair of delegates whose "show" can be held open, so a test can put a hide
    /// request in flight at the exact moment the real bug needed one.
    /// </summary>
    private sealed class Fake
    {
        public int Shows;
        public int Hides;
        public TaskCompletionSource<bool> HoldShow;

        public Task Show()
        {
            Shows++;
            return HoldShow?.Task ?? Task.CompletedTask;
        }

        public Task Hide()
        {
            Hides++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Overlay_goes_up_and_comes_down()
    {
        var fake = new Fake();
        var gate = new OverlayGate(fake.Show, fake.Hide);

        await gate.RequestAsync(true);
        Assert.True(gate.IsShown);
        Assert.Equal(1, fake.Shows);

        await gate.RequestAsync(false);
        Assert.False(gate.IsShown);
        Assert.Equal(1, fake.Hides);
    }

    /// <summary>
    /// THE BUG. A screen finishes loading before the overlay has finished appearing — which a local
    /// SQLite read does routinely — so the hide arrives while the show is still in flight.
    ///
    /// The hide must still take effect, and it must happen AFTER the show, not instead of it. The
    /// original code popped the modal stack before the overlay had been pushed onto it: the pop hit
    /// nothing, the push then landed, and the overlay was up for good.
    /// </summary>
    [Fact]
    public async Task Hide_requested_while_the_show_is_still_in_flight_still_ends_hidden()
    {
        var fake = new Fake { HoldShow = new TaskCompletionSource<bool>() };
        var gate = new OverlayGate(fake.Show, fake.Hide);

        var settle = gate.RequestAsync(true);   // starts a show that has not completed
        gate.RequestAsync(false);               // ⚠ arrives mid-push, as it does in the app

        Assert.Equal(0, fake.Hides);            // nothing may be popped while the push is in flight

        fake.HoldShow.SetResult(true);
        await settle;

        Assert.False(gate.IsShown);
        Assert.Equal(1, fake.Shows);
        Assert.Equal(1, fake.Hides);
    }

    /// <summary>
    /// The consequence that made this a whole-app failure rather than one blank screen: after the
    /// race above, the ORIGINAL code could never show or hide anything again, because its state flag
    /// disagreed with the modal stack for the life of the process. Every later screen inherited a
    /// spinner it had no way to clear.
    /// </summary>
    [Fact]
    public async Task The_overlay_still_works_on_the_next_screen_after_that_race()
    {
        var fake = new Fake { HoldShow = new TaskCompletionSource<bool>() };
        var gate = new OverlayGate(fake.Show, fake.Hide);

        var settle = gate.RequestAsync(true);
        gate.RequestAsync(false);
        fake.HoldShow.SetResult(true);
        await settle;

        // Next screen. This is the part that was dead.
        await gate.RequestAsync(true);
        Assert.True(gate.IsShown);
        Assert.Equal(2, fake.Shows);

        await gate.RequestAsync(false);
        Assert.False(gate.IsShown);
        Assert.Equal(2, fake.Hides);
    }

    /// <summary>
    /// Repeated requests for a state the overlay is already in must not queue up work — a screen
    /// that clears its spinner twice is common, and must not pop a page it does not own.
    /// </summary>
    [Fact]
    public async Task Asking_for_a_state_it_is_already_in_does_nothing()
    {
        var fake = new Fake();
        var gate = new OverlayGate(fake.Show, fake.Hide);

        await gate.RequestAsync(false);
        Assert.Equal(0, fake.Hides);

        await gate.RequestAsync(true);
        await gate.RequestAsync(true);
        Assert.Equal(1, fake.Shows);

        await gate.RequestAsync(false);
        await gate.RequestAsync(false);
        Assert.Equal(1, fake.Hides);
    }

    /// <summary>
    /// ⚠ FAIL OPEN. If the push itself throws, the gate must report it and leave itself usable
    /// rather than believing an overlay is up that never appeared — the failure mode has to be a
    /// missing spinner, never a locked screen.
    /// </summary>
    [Fact]
    public async Task A_failing_show_is_reported_and_leaves_the_gate_usable()
    {
        Exception caught = null;
        var attempts = 0;
        var gate = new OverlayGate(
            show: () =>
            {
                attempts++;
                if (attempts == 1) throw new InvalidOperationException("no navigation host");
                return Task.CompletedTask;
            },
            hide: () => Task.CompletedTask,
            onError: ex => caught = ex);

        await gate.RequestAsync(true);

        Assert.NotNull(caught);
        Assert.False(gate.IsShown);

        await gate.RequestAsync(true);
        Assert.True(gate.IsShown);
    }
}
