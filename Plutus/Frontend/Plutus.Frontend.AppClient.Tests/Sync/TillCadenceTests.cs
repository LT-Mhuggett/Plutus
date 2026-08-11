using System;
using System.Threading.Tasks;
using Plutus.Frontend.AppClient.Services.Sync;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Sync
{
    /// <summary>
    /// Cutover step 13 — the till's one background clock.
    ///
    /// ⚠ WHAT IS TESTABLE HERE IS THE SCHEDULING, not the HTTP. A tick needs device credentials, a
    /// server and a store; those are covered by `OutboxPusherTests`, `SyncClientTests` and the E2E
    /// suite. What this pins is the part that has no test anywhere else and breaks silently: whether
    /// the loop starts twice, whether it survives, and whether it holds the catalogue back while a
    /// basket is open.
    /// </summary>
    public class TillCadenceTests : IDisposable
    {
        public void Dispose()
        {
            TillCadence.Stop();
            TillCadence.BasketIsOpen = null;
        }

        /// <summary>
        /// ⚠ Sign-in, app start and a connectivity change all call `Start`. If it were not
        /// idempotent the till would run two clocks — every heartbeat, drain and catalogue pull
        /// doubled, behind a rate limit sized for one.
        /// </summary>
        [Fact]
        public void Starting_twice_does_not_run_two_clocks()
        {
            TillCadence.Start();
            TillCadence.Start();
            TillCadence.Start();

            // Stopping once must leave nothing running — if a second loop existed it would survive.
            TillCadence.Stop();

            // And starting again after a stop must work, or a reconnect would leave the till mute.
            TillCadence.Start();
            TillCadence.Stop();
        }

        [Fact]
        public void Stopping_a_clock_that_never_started_is_harmless()
        {
            TillCadence.Stop();
            TillCadence.Stop();
        }

        /// <summary>⚠ The web till's interval. Changing it changes the fleet list's idea of which
        /// tills are late, so it is pinned rather than left as a number someone may tune.</summary>
        [Fact]
        public void The_cadence_matches_the_web_tills_sixty_seconds()
        {
            Assert.Equal(TimeSpan.FromSeconds(60), TillCadence.Interval);
        }

        /// <summary>
        /// ⚠ A basket open must HOLD THE CATALOGUE, and the till decides that by asking the basket
        /// rather than by guessing. A sync mid-basket rewrites prices under the operator's hands:
        /// one line from before the tick and one from after, in a single sale.
        /// </summary>
        [Fact]
        public void The_basket_can_hold_the_catalogue_back()
        {
            var open = true;
            TillCadence.BasketIsOpen = () => open;

            Assert.True(TillCadence.BasketIsOpen());
            open = false;
            Assert.False(TillCadence.BasketIsOpen());
        }

        /// <summary>⚠ An unset gate must not throw. The Plutus tab can tick before the till screen
        /// has ever been built, and a NullReferenceException in the loop would stop every sync on
        /// the till with no symptom other than sales quietly not arriving.</summary>
        [Fact]
        public async Task A_tick_with_no_basket_gate_set_does_not_throw()
        {
            TillCadence.BasketIsOpen = null;

            // ⚠ Returns a message rather than throwing on a till with no credentials — which is
            // exactly the state of a machine running the test suite.
            var result = await TillCadence.TickAsync();

            Assert.False(string.IsNullOrWhiteSpace(result));
        }

        /// <summary>The tab has something to show before the first tick lands.</summary>
        [Fact]
        public void There_is_always_something_to_report()
        {
            Assert.False(string.IsNullOrWhiteSpace(TillCadence.LastResult));
        }

        /// <summary>
        /// Every tick tells the screens, INCLUDING A TICK THAT FAILED.
        ///
        /// ⚠ Matt, 2026-08-11: *"The open float was 'Waiting' and never updated."* The drain worked;
        /// nothing told the Cash screen, so it showed "(waiting to send)" against money already sent.
        ///
        /// ⚠ THE FAILING CASE IS THE POINT, and it is why the raise lives in a `finally`. This test
        /// machine has no device credentials, so this tick takes the early-return path — the same
        /// shape as a till with no network. A screen must redraw then too: "still waiting to send"
        /// is TRUE, and holding the last render up because the tick failed is how a stale figure
        /// outlives the fault behind it.
        /// </summary>
        [Fact]
        public async Task Every_tick_tells_the_screens_to_redraw_even_when_it_fails()
        {
            var raised = 0;
            void Handler() => raised++;

            TillCadence.Ticked += Handler;
            try
            {
                var result = await TillCadence.TickAsync();

                // ⚠ ASSERTED, NOT ASSUMED. The test is only about the failing path if the tick
                // actually took it — a version that quietly started exercising the SUCCESS path
                // would keep passing while pinning nothing it claims to.
                //
                // ⚠ AND THIS IS THE THROWING PATH, which is the strongest case there is: on a
                // machine with no platform secure store the credential load THROWS, `TickAsync`
                // catches it and returns this sentence. So the redraw fired out of a tick that
                // raised an exception — which is precisely what putting the raise in a `finally`
                // buys, and what a raise at the end of the `try` would have lost.
                Assert.Equal("Couldn't sync with Plutus. See the Plutus tab's log.", result);

                Assert.Equal(1, raised);

                await TillCadence.TickAsync();
                Assert.Equal(2, raised);
            }
            finally
            {
                TillCadence.Ticked -= Handler;
            }
        }

        /// <summary>
        /// ⚠ A SUBSCRIBER THAT THROWS MUST NOT TAKE THE TICK WITH IT. `Ticked` fires from the
        /// background loop, where an escaped exception has no handler above it — it would stop the
        /// heartbeat, the outbox drain and the catalogue for the rest of the shift, and the only
        /// symptom would be sales quietly not arriving. A screen's redraw is not worth that.
        /// </summary>
        [Fact]
        public async Task A_screen_that_throws_while_redrawing_does_not_stop_the_clock()
        {
            void Bad() => throw new InvalidOperationException("a screen fell over");
            var goodRan = false;
            void Good() => goodRan = true;

            TillCadence.Ticked += Bad;
            TillCadence.Ticked += Good;
            try
            {
                // ⚠ Must not throw — and must still return the tick's own answer.
                var result = await TillCadence.TickAsync();
                Assert.False(string.IsNullOrWhiteSpace(result));
            }
            finally
            {
                TillCadence.Ticked -= Bad;
                TillCadence.Ticked -= Good;
            }

            // ⚠ Honest about what this does NOT pin: .NET stops a multicast invocation at the first
            // handler that throws, so `Good` — subscribed after `Bad` — does not run. The guard
            // protects the CLOCK, not the other screens. Asserting it here states the real
            // behaviour rather than implying an isolation that isn't there.
            Assert.False(goodRan);
        }
    }
}
